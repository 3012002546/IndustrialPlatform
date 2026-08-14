using IndustrialPlatform.SystemData.Application.DatabaseOrchestration.Internal;
using IndustrialPlatform.SystemData.Contracts.DatabaseOrchestration;
using IndustrialPlatform.SystemData.Domain.DatabaseOrchestration;

namespace IndustrialPlatform.SystemData.Application.DatabaseOrchestration;

/// <summary>审批用例端口。</summary>
public interface IApprovalService
{
    /// <summary>为计划登记审批(校验计划存在且未过期)。</summary>
    Task<DatabaseApprovalV1> CreateAsync(string tenantNId, string actorUserNId, string planNId, DatabaseApprovalRequestV1 request, CancellationToken cancellationToken);

    /// <summary>计划当前是否有有效审批(Approved + 未过期 + 校验和/指纹匹配)。</summary>
    Task<bool> IsApprovedForAsync(string tenantNId, DatabaseProvisionPlan plan, CancellationToken cancellationToken);
}

/// <summary>审批用例(05 方案 §8.1 审批表、§9.2 审批端点)。只追加,有效性由 apply 门禁裁决。</summary>
public sealed class DatabaseApprovalService : IApprovalService
{
    private readonly IDatabaseOrchestrationStore _store;

    public DatabaseApprovalService(IDatabaseOrchestrationStore store)
    {
        _store = store;
    }

    /// <inheritdoc />
    public async Task<DatabaseApprovalV1> CreateAsync(
        string tenantNId,
        string actorUserNId,
        string planNId,
        DatabaseApprovalRequestV1 request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var planNIdTrimmed = DatabaseOrchestrationInput.Require(planNId, "计划标识不能为空。");
        var plan = await _store.GetPlanAsync(tenantNId, planNIdTrimmed, cancellationToken)
            ?? throw new NotFoundException();

        var now = DateTimeOffset.UtcNow;
        if (plan.IsExpired(now))
        {
            throw new PlanExpiredException();
        }

        var approval = DatabaseApproval.Create(
            tenantNId,
            DatabaseOrchestrationInput.NewNId("APR"),
            plan.PlanNId,
            plan.PlanChecksum,
            plan.TargetStateFingerprint,
            actorUserNId,
            DatabaseOrchestrationInput.TrimOrNull(request.Reason),
            now,
            plan.ExpiresOn);
        await WriteGuard.ExecuteAsync(() => _store.AddApprovalAsync(approval, cancellationToken));
        return ToApprovalV1(approval);
    }

    /// <inheritdoc />
    public async Task<bool> IsApprovedForAsync(string tenantNId, DatabaseProvisionPlan plan, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var approvals = await _store.GetApprovalsForPlanAsync(tenantNId, plan.PlanNId, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        return approvals.Any(approval => approval.IsValidFor(plan.PlanChecksum, plan.TargetStateFingerprint, now));
    }

    private static DatabaseApprovalV1 ToApprovalV1(DatabaseApproval approval) => new()
    {
        TenantNId = approval.TenantNId,
        ApprovalNId = approval.ApprovalNId,
        PlanNId = approval.PlanNId,
        PlanChecksum = approval.PlanChecksum,
        TargetStateFingerprint = approval.TargetStateFingerprint,
        ApprovedByUserNId = approval.ApprovedByUserNId,
        Reason = approval.Reason,
        ApprovedOn = approval.ApprovedOn,
        ExpiresOn = approval.ExpiresOn,
        Status = approval.Status.ToString(),
    };
}
