using System.Security.Cryptography;
using IndustrialPlatform.Security;
using IndustrialPlatform.Web.Results;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
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
                var signingKeyPem = jwt["SigningKey"];
                var signingKey = ParsePublicKey(signingKeyPem);

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
                    IssuerSigningKey = signingKey,
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

                // 独立 SystemData 不持有 Identity 私钥。没有显式公钥时，按标准 JWKS
                // 端点读取 Identity 的公开签名密钥并由 JwtBearer 缓存/刷新；两种部署模式
                // 都继续使用同一 issuer/audience 和签名校验，不因网关转发而信任任意令牌。
                if (signingKey is null
                    && string.IsNullOrWhiteSpace(signingKeyPem)
                    && !string.IsNullOrWhiteSpace(jwt["JwksUrl"]))
                {
                    options.ConfigurationManager = new ConfigurationManager<OpenIdConnectConfiguration>(
                        jwt["JwksUrl"]!,
                        new SystemDataJwksConfigurationRetriever(),
                        new HttpDocumentRetriever
                        {
                            RequireHttps = !string.Equals(
                                jwt["RequireHttpsMetadata"],
                                "false",
                                StringComparison.OrdinalIgnoreCase),
                        });
                    options.RefreshOnIssuerKeyNotFound = true;
                }
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

/// <summary>
/// 将 Identity 的 RFC 7517 JWKS 文档适配为 JwtBearer 的签名配置。
/// Identity 当前只公开 JWKS,不额外引入伪 OIDC 配置或私钥共享。
/// </summary>
internal sealed class SystemDataJwksConfigurationRetriever : IConfigurationRetriever<OpenIdConnectConfiguration>
{
    public async Task<OpenIdConnectConfiguration> GetConfigurationAsync(
        string address,
        IDocumentRetriever retriever,
        CancellationToken cancellationToken)
    {
        var document = await retriever.GetDocumentAsync(address, cancellationToken);
        var keys = new JsonWebKeySet(document).GetSigningKeys();
        if (keys.Count == 0)
        {
            throw new InvalidOperationException("Identity JWKS 文档未提供可用签名公钥。");
        }

        var configuration = new OpenIdConnectConfiguration();
        foreach (var key in keys)
        {
            configuration.SigningKeys.Add(key);
        }

        return configuration;
    }
}
