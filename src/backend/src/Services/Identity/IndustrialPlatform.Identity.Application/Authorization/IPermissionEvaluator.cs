using IndustrialPlatform.Identity.Application.Authentication;

namespace IndustrialPlatform.Identity.Application.Authorization;

/// <summary>
/// 授权拒绝原因(§18):映射 Api 层 401/403 信封与审计原因。
/// </summary>
public enum AuthorizationDenialReason
{
    /// <summary>未发生拒绝(允许)。</summary>
    None = 0,

    /// <summary>会话无效:撤销会话、用户不存在、或安全版本与令牌不一致。</summary>
    SessionInvalid = 1,

    /// <summary>账号已禁用。</summary>
    AccountDisabled = 2,

    /// <summary>缺少所需权限。</summary>
    MissingPermission = 3,

    /// <summary>安全存储不可用(授权数据/撤销存储 fail-closed,映射 503)。</summary>
    SecurityStoreUnavailable = 4,
}

/// <summary>授权裁决结果。</summary>
public sealed record PermissionEvaluation(bool Allowed, AuthorizationDenialReason Reason);

/// <summary>
/// 权限评估端口(§14/§18):对照 JWT 声明(sub/tenant_id/sid/ver)与授权快照裁决单权限。
/// 会话撤销校验 fail-closed;授权数据不可用抛 <see cref="SecurityStoreUnavailableException"/>。
/// </summary>
public interface IPermissionEvaluator
{
    /// <summary>
    /// 评估用户是否拥有指定权限。返回 <see cref="PermissionEvaluation"/>;
    /// 授权数据存储不可用时抛 <see cref="SecurityStoreUnavailableException"/>(503)。
    /// </summary>
    Task<PermissionEvaluation> EvaluateAsync(
        string tenantNId,
        string userNId,
        string? sessionNId,
        int authVersion,
        string requiredPermissionNId,
        CancellationToken cancellationToken);
}
