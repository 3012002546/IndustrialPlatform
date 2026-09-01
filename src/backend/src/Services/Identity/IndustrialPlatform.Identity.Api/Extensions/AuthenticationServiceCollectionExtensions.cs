using System.Security.Claims;
using IndustrialPlatform.Identity.Api.Authorization;
using IndustrialPlatform.Identity.Application.Authentication;
using IndustrialPlatform.Identity.Application.Authorization;
using IndustrialPlatform.Identity.Infrastructure.Security;
using IndustrialPlatform.Security;
using IndustrialPlatform.Web.Results;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Identity 服务认证扩展(§12):JwtBearer 自校验(公钥验签,iss/aud/lifetime),
/// 未经认证/无权限写统一 ApiResult 信封。声明名保持原始大小写(MapInboundClaims=false)。
/// </summary>
public static class AuthenticationServiceCollectionExtensions
{
    private const string RevocationStoreUnavailableItemsKey = "Identity.Authentication.RevocationStoreUnavailable";

    /// <summary>
    /// 注册 JwtBearer 认证与授权。签名公钥来自 <see cref="RsaSigningKeyProvider"/>,
    /// 通过 options Configure 注入,避免提前 BuildServiceProvider 触发临时密钥生成。
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <returns>服务集合。</returns>
    public static IServiceCollection AddIdentityAuthentication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer();

        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<RsaSigningKeyProvider, Microsoft.Extensions.Options.IOptions<JwtOptions>, ISessionRevocationStore>((bearer, keys, jwt, sessionRevocation) =>
            {
                bearer.MapInboundClaims = false;
                bearer.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = jwt.Value.Issuer,
                    ValidateAudience = true,
                    ValidAudience = jwt.Value.Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new RsaSecurityKey(keys.PublicKey) { KeyId = keys.KeyId },
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromSeconds(30),
                    // 与本服务签发的 claim 名对齐,使授权策略可读 user_name/role
                    NameClaimType = ClaimConstants.UserName,
                    RoleClaimType = ClaimConstants.Role,
                };
                bearer.Events = new JwtBearerEvents
                {
                    OnTokenValidated = async context =>
                    {
                        var sessionNId = context.Principal?.FindFirstValue(ClaimConstants.SessionId);
                        if (string.IsNullOrWhiteSpace(sessionNId))
                        {
                            return;
                        }

                        try
                        {
                            if (await sessionRevocation.IsRevokedAsync(
                                sessionNId,
                                context.HttpContext.RequestAborted))
                            {
                                context.HttpContext.Items[PermissionAuthorizationHandler.DenialReasonItemsKey] =
                                    AuthorizationDenialReason.SessionInvalid;
                                context.Fail("session_revoked");
                            }
                        }
                        catch (SecurityStoreUnavailableException)
                        {
                            // JWT 校验通过后仍必须以撤销存储为准;存储不可用时拒绝请求,不得放行。
                            context.HttpContext.Items[RevocationStoreUnavailableItemsKey] = true;
                            context.HttpContext.Items[PermissionAuthorizationHandler.DenialReasonItemsKey] =
                                AuthorizationDenialReason.SecurityStoreUnavailable;
                            context.Fail("session_revocation_unavailable");
                        }
                    },
                    OnChallenge = context =>
                    {
                        context.HandleResponse();
                        var revocationUnavailable = context.HttpContext.Items.TryGetValue(
                                RevocationStoreUnavailableItemsKey,
                                out var revocationFailure)
                            && revocationFailure is true;
                        var reason = context.HttpContext.Items.TryGetValue(
                                PermissionAuthorizationHandler.DenialReasonItemsKey,
                                out var denial)
                            && denial is AuthorizationDenialReason denialReason
                            ? denialReason
                            : (AuthorizationDenialReason?)null;
                        var (statusCode, code, message) = reason switch
                        {
                            AuthorizationDenialReason.SecurityStoreUnavailable =>
                                (StatusCodes.Status503ServiceUnavailable, "ID_AUTH_SECURITY_STORE_UNAVAILABLE", "认证服务暂时不可用，请稍后再试。"),
                            _ =>
                                (StatusCodes.Status401Unauthorized, "401", "未认证或登录已失效，请重新登录。"),
                        };
                        if (revocationUnavailable)
                        {
                            statusCode = StatusCodes.Status503ServiceUnavailable;
                            code = "ID_AUTH_SECURITY_STORE_UNAVAILABLE";
                            message = "认证服务暂时不可用，请稍后再试。";
                        }
                        context.Response.StatusCode = statusCode;
                        context.Response.ContentType = "application/json; charset=utf-8";
                        return context.Response.WriteAsJsonAsync(
                            ApiResult.Fail<object?>(code, message));
                    },
                    OnForbidden = context =>
                    {
                        // 按处理器写入的拒绝原因映射 401/403/503 信封(§18):会话失效 401、
                        // 账号禁用/缺权限 403、安全存储不可用 503(fail-closed)。
                        var revocationUnavailable = context.HttpContext.Items.TryGetValue(
                                RevocationStoreUnavailableItemsKey,
                                out var revocationFailure)
                            && revocationFailure is true;
                        var reason = context.HttpContext.Items[PermissionAuthorizationHandler.DenialReasonItemsKey] as AuthorizationDenialReason?;
                        var (statusCode, code, message) = reason switch
                        {
                            AuthorizationDenialReason.SessionInvalid => (StatusCodes.Status401Unauthorized, "401", "登录已失效，请重新登录。"),
                            AuthorizationDenialReason.AccountDisabled => (StatusCodes.Status403Forbidden, "ID_PERMISSION_DENIED", "账号不可用，请联系管理员。"),
                            AuthorizationDenialReason.SecurityStoreUnavailable => (StatusCodes.Status503ServiceUnavailable, "ID_AUTH_SECURITY_STORE_UNAVAILABLE", "认证服务暂时不可用，请稍后再试。"),
                            _ => (StatusCodes.Status403Forbidden, "ID_PERMISSION_DENIED", "无权限执行此操作。"),
                        };
                        if (revocationUnavailable)
                        {
                            statusCode = StatusCodes.Status503ServiceUnavailable;
                            code = "ID_AUTH_SECURITY_STORE_UNAVAILABLE";
                            message = "认证服务暂时不可用，请稍后再试。";
                        }

                        context.Response.StatusCode = statusCode;
                        context.Response.ContentType = "application/json; charset=utf-8";
                        return context.Response.WriteAsJsonAsync(ApiResult.Fail<object?>(code, message));
                    },
                };
            });

        services.AddAuthorization();
        return services;
    }
}
