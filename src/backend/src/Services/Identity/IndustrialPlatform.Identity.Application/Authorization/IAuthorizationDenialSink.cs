namespace IndustrialPlatform.Identity.Application.Authorization;

/// <summary>
/// 权限拒绝审计条目(§18.3):记录一次被拒绝的授权请求。
/// </summary>
public sealed record AuthorizationDenial(
    string TenantNId,
    string UserNId,
    string? SessionNId,
    string RequiredPermissionNId,
    AuthorizationDenialReason Reason,
    string? TraceId);

/// <summary>
/// 权限拒绝审计端口。实现为尽力而为:写失败不影响授权裁决。
/// </summary>
public interface IAuthorizationDenialSink
{
    /// <summary>记录一次拒绝审计。</summary>
    Task RecordDenialAsync(AuthorizationDenial denial, CancellationToken cancellationToken);
}
