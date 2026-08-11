namespace IndustrialPlatform.Identity.Application.Authorization;

/// <summary>
/// 服务端授权配置(§14),绑定配置节 <c>Identity:Authorization</c>。
/// </summary>
public sealed class AuthorizationOptions
{
    /// <summary>配置节名。</summary>
    public const string SectionName = "Identity:Authorization";

    /// <summary>权限缓存 TTL,过期后强制数据库装载(默认 15 分钟)。</summary>
    public TimeSpan PermissionCacheTtl { get; set; } = TimeSpan.FromMinutes(15);
}
