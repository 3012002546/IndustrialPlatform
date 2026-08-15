namespace IndustrialPlatform.Identity.Contracts.Authentication;

/// <summary>
/// 登录请求(§15.1)。属性可空:空值由服务层校验抛出校验异常,
/// 避免 [ApiController] 推断 [Required] 抢占标准 400 ProblemDetails 破坏统一信封。
/// </summary>
public sealed record LoginRequest(string? LoginName, string? Password);

/// <summary>
/// 当前用户契约(§15.2)。标识一律使用 NId,不得暴露数据库 Guid。
/// MustChangePassword(§29A.4):普通新用户首次登录必须改密,内置 admin 为 false。
/// </summary>
public sealed record AuthUser(
    string UserNId,
    string LoginName,
    string Name,
    string TenantNId,
    IReadOnlyList<string> RoleNIds,
    IReadOnlyList<string> PermissionNIds,
    bool MustChangePassword);

/// <summary>
/// 登录响应(§15.2):Access Token、刷新令牌与当前用户。刷新令牌为一次性返回,服务端只存哈希。
/// </summary>
public sealed record AuthSession(
    string AccessToken,
    string RefreshToken,
    DateTimeOffset ExpiresAt,
    AuthUser User);

/// <summary>
/// JWKS 公钥条目。camelCase 序列化即 RFC 7517 字段(kty/kid/n/e/use/alg)。
/// </summary>
public sealed record JwksKey(string Kty, string Kid, string N, string E, string Use, string Alg);

/// <summary>JWKS 文档:GET /.well-known/jwks.json 返回 {"keys": [...]}。</summary>
public sealed record JwksDocument(IReadOnlyList<JwksKey> Keys);

/// <summary>
/// 刷新请求(§15.3)。属性可空:空值由服务层校验,避免 [ApiController] 推断 Required 破坏统一信封。
/// </summary>
public sealed record RefreshRequest(string? RefreshToken);

/// <summary>
/// 注销请求(§15.4)。注销接口要求 Bearer Access Token,重复注销保持幂等。
/// </summary>
public sealed record LogoutRequest(string? RefreshToken);

/// <summary>
/// 修改密码请求。要求 Bearer Access Token 与当前密码;成功后撤销该用户全部刷新会话并推进安全版本,前端需重新登录。
/// </summary>
public sealed record ChangePasswordRequest(string? CurrentPassword, string? NewPassword);
