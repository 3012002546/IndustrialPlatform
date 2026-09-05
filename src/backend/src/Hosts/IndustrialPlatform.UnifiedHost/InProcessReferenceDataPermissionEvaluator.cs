using IndustrialPlatform.Identity.Application.Authentication;
using IndustrialPlatform.Identity.Application.Authorization;
using IndustrialPlatform.ReferenceData.Application.Authorization;

namespace IndustrialPlatform.UnifiedHost;

/// <summary>
/// UnifiedHost 内直接复用 Identity 权限评估器，避免同进程 HTTP 回环；
/// 独立 ReferenceData.Api 仍使用其基础设施层 HTTP 适配器。
/// </summary>
public sealed class InProcessReferenceDataPermissionEvaluator : IReferenceDataPermissionEvaluator
{
    private readonly IPermissionEvaluator _identityEvaluator;

    public InProcessReferenceDataPermissionEvaluator(IPermissionEvaluator identityEvaluator)
    {
        _identityEvaluator = identityEvaluator;
    }

    public async Task<ReferenceDataPermissionDecision> EvaluateAsync(
        ReferenceDataPermissionRequest request,
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
            return new ReferenceDataPermissionDecision(result.Allowed, Map(result.Reason));
        }
        catch (SecurityStoreUnavailableException)
        {
            return new ReferenceDataPermissionDecision(false, ReferenceDataPermissionDenialReason.SecurityStoreUnavailable);
        }
    }

    private static ReferenceDataPermissionDenialReason Map(AuthorizationDenialReason reason) => reason switch
    {
        AuthorizationDenialReason.None => ReferenceDataPermissionDenialReason.None,
        AuthorizationDenialReason.SessionInvalid => ReferenceDataPermissionDenialReason.SessionInvalid,
        AuthorizationDenialReason.AccountDisabled => ReferenceDataPermissionDenialReason.AccountDisabled,
        AuthorizationDenialReason.MissingPermission => ReferenceDataPermissionDenialReason.MissingPermission,
        AuthorizationDenialReason.SecurityStoreUnavailable => ReferenceDataPermissionDenialReason.SecurityStoreUnavailable,
        _ => ReferenceDataPermissionDenialReason.SecurityStoreUnavailable,
    };
}
