using IndustrialPlatform.Identity.Application.Authentication;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace IndustrialPlatform.Identity.Infrastructure.Security;

/// <summary>
/// RS256 Access Token 签发实现(§12)。Header 携带 kid;
/// claims = sub(user NId)/user_name/tenant_id/role[](RoleNId)/sid/ver/jti,不写完整 permissions、不写数据库 Guid。
/// </summary>
public sealed class AccessTokenFactory : IAccessTokenFactory
{
    private readonly RsaSigningKeyProvider _keys;
    private readonly IOptions<JwtOptions> _options;
    private readonly JsonWebTokenHandler _handler = new();

    /// <summary>初始化签发器。</summary>
    public AccessTokenFactory(RsaSigningKeyProvider keys, IOptions<JwtOptions> options)
    {
        ArgumentNullException.ThrowIfNull(keys);
        ArgumentNullException.ThrowIfNull(options);
        _keys = keys;
        _options = options;
    }

    /// <inheritdoc/>
    public AccessTokenResult Create(AccessTokenDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        var jwt = _options.Value;
        var issuedAt = descriptor.IssuedAt;
        var expiresAt = issuedAt.AddMinutes(jwt.AccessTokenMinutes);

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Issuer = jwt.Issuer,
            Audience = jwt.Audience,
            IssuedAt = issuedAt.UtcDateTime,
            NotBefore = issuedAt.UtcDateTime,
            Expires = expiresAt.UtcDateTime,
            SigningCredentials = new SigningCredentials(
                new RsaSecurityKey(_keys.PrivateKey) { KeyId = _keys.KeyId },
                SecurityAlgorithms.RsaSha256),
            // kid 由 SigningCredentials.Key.KeyId 自动写入 Header,不得重复放入 AdditionalHeaderClaims
            Claims = new Dictionary<string, object>
            {
                { "sub", descriptor.Subject },
                { "user_name", descriptor.UserName },
                { "tenant_id", descriptor.TenantNId },
                { "role", descriptor.Roles },
                { "sid", descriptor.SessionId },
                { "ver", descriptor.AuthVersion },
                { "jti", Guid.NewGuid().ToString("N") },
            },
        };

        var token = _handler.CreateToken(tokenDescriptor);
        return new AccessTokenResult(token, expiresAt);
    }
}
