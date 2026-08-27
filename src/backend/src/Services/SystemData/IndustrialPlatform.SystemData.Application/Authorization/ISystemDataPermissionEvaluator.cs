namespace IndustrialPlatform.SystemData.Application.Authorization;

/// <summary>SystemData 权限拒绝原因，与 Identity 的稳定 HTTP 契约语义对齐。</summary>
public enum SystemDataPermissionDenialReason
{
    None = 0,
    SessionInvalid = 1,
    AccountDisabled = 2,
    MissingPermission = 3,
    SecurityStoreUnavailable = 4,
}

/// <summary>一次权限评估所需的已认证上下文。</summary>
public sealed record SystemDataPermissionRequest(
    string TenantNId,
    string UserNId,
    string? SessionNId,
    int AuthVersion,
    string PermissionNId,
    string? AccessToken);

/// <summary>SystemData 权限裁决。</summary>
public sealed record SystemDataPermissionDecision(bool Allowed, SystemDataPermissionDenialReason Reason);

/// <summary>SystemData 依赖的权限裁决端口；由独立服务 HTTP 或 UnifiedHost 进程内适配器实现。</summary>
public interface ISystemDataPermissionEvaluator
{
    Task<SystemDataPermissionDecision> EvaluateAsync(
        SystemDataPermissionRequest request,
        CancellationToken cancellationToken);
}
