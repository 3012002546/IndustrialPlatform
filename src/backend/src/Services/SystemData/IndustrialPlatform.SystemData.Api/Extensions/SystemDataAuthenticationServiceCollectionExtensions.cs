using System.Security.Cryptography;
using IndustrialPlatform.Security;
using IndustrialPlatform.Web.Results;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// SystemData 服务认证扩展(TASK-SD-006):JwtBearer 自校验(iss/aud/lifetime/签名),
/// 配置驱动、fail-closed —— <c>Jwt</c> 段未配置或签名公钥缺失/非法时无验签密钥,
/// 所有令牌拒绝(401)。声明名保持原始大小写(MapInboundClaims=false),与 Identity 签发令牌对齐。
/// 未认证写 401 信封;已认证但权限不足由授权管线 fail → OnForbidden 写 403 信封。
/// </summary>
public static class SystemDataAuthenticationServiceCollectionExtensions
{
    /// <summary>
    /// 注册 JwtBearer 认证。配置键: <c>Jwt:Issuer</c>、<c>Jwt:Audience</c>、
    /// <c>Jwt:SigningKey</c>(RSA 公钥 PEM;为空时 fail-closed 拒绝所有令牌)。
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <param name="configuration">配置源。</param>
    /// <returns>服务集合。</returns>
    public static IServiceCollection AddSystemDataAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                var jwt = configuration.GetSection("Jwt");
                var issuer = jwt["Issuer"];
                var audience = jwt["Audience"];

                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = !string.IsNullOrWhiteSpace(issuer),
                    ValidIssuer = issuer,
                    ValidateAudience = !string.IsNullOrWhiteSpace(audience),
                    ValidAudience = audience,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromSeconds(30),
                    RequireSignedTokens = true,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = ParsePublicKey(jwt["SigningKey"]),
                    // 与本服务 claim 名对齐,使授权策略可读 user_name/role
                    NameClaimType = ClaimConstants.UserName,
                    RoleClaimType = ClaimConstants.Role,
                };
                options.Events = new JwtBearerEvents
                {
                    OnChallenge = context =>
                    {
                        context.HandleResponse();
                        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        context.Response.ContentType = "application/json; charset=utf-8";
                        return context.Response.WriteAsJsonAsync(
                            ApiResult.Fail<object?>("401", "未认证或登录已失效，请重新登录。"));
                    },
                    OnForbidden = context =>
                    {
                        context.Response.StatusCode = StatusCodes.Status403Forbidden;
                        context.Response.ContentType = "application/json; charset=utf-8";
                        return context.Response.WriteAsJsonAsync(
                            ApiResult.Fail<object?>("SD_PERMISSION_DENIED", "无权限执行此操作。"));
                    },
                };
            });

        return services;
    }

    /// <summary>解析 RSA 公钥 PEM;缺失或非法返回 null(无验签密钥 → 全部令牌 401,fail-closed)。</summary>
    private static RsaSecurityKey? ParsePublicKey(string? pem)
    {
        if (string.IsNullOrWhiteSpace(pem))
        {
            return null;
        }

        var rsa = RSA.Create();
        try
        {
            rsa.ImportFromPem(pem);
            return new RsaSecurityKey(rsa);
        }
        catch (Exception)
        {
            rsa.Dispose();
            return null;
        }
    }
}
