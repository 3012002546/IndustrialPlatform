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
            .Configure<RsaSigningKeyProvider, Microsoft.Extensions.Options.IOptions<JwtOptions>>((bearer, keys, jwt) =>
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
                            ApiResult.Fail<object?>("ID_PERMISSION_DENIED", "无权限执行此操作。"));
                    },
                };
            });

        services.AddAuthorization();
        return services;
    }
}
