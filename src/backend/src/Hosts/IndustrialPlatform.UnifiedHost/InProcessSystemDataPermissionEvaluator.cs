using IndustrialPlatform.Identity.Application.Authentication;
using IndustrialPlatform.Identity.Application.Authorization;
using IndustrialPlatform.SystemData.Application.Authorization;

namespace IndustrialPlatform.UnifiedHost;

/// <summary>
/// UnifiedHost 内直接复用 Identity 权限评估器，避免同进程 HTTP 回环；
/// 独立 SystemData.Api 仍使用其基础设施层 HTTP 适配器。
/// </summary>
public sealed class InProcessSystemDataPermissionEvaluator : ISystemDataPermissionEvaluator
{
    private readonly IPermissionEvaluator _identityEvaluator;

    public InProcessSystemDataPermissionEvaluator(IPermissionEvaluator identityEvaluator)
    {
        _identityEvaluator = identityEvaluator;
    }

    public async Task<SystemDataPermissionDecision> EvaluateAsync(
        SystemDataPermissionRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _identityEvaluator.EvaluateAsync(
                request.TenantNId,
                request.UserNId,
                request.SessionNId,
                request.AuthVersion,
                request.PermissionNId,
                cancellationToken);
            return new SystemDataPermissionDecision(result.Allowed, Map(result.Reason));
        }
        catch (SecurityStoreUnavailableException)
        {
            return new SystemDataPermissionDecision(false, SystemDataPermissionDenialReason.SecurityStoreUnavailable);
        }
    }

    private static SystemDataPermissionDenialReason Map(AuthorizationDenialReason reason) => reason switch
    {
        AuthorizationDenialReason.None => SystemDataPermissionDenialReason.None,
        AuthorizationDenialReason.SessionInvalid => SystemDataPermissionDenialReason.SessionInvalid,
        AuthorizationDenialReason.AccountDisabled => SystemDataPermissionDenialReason.AccountDisabled,
        AuthorizationDenialReason.MissingPermission => SystemDataPermissionDenialReason.MissingPermission,
        AuthorizationDenialReason.SecurityStoreUnavailable => SystemDataPermissionDenialReason.SecurityStoreUnavailable,
        _ => SystemDataPermissionDenialReason.SecurityStoreUnavailable,
    };
}
