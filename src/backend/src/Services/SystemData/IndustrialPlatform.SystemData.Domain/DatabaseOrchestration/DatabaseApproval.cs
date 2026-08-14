using IndustrialPlatform.SharedKernel.Entities;

namespace IndustrialPlatform.SystemData.Domain.DatabaseOrchestration;

/// <summary>
/// 计划人工审批记录聚合根(05 方案 §8.1 <c>system_data_database_approval</c>)。
/// 只追加(append-only),创建即固化;有效性由 <see cref="IsValidFor"/> 在 apply 门禁处裁决。
/// </summary>
public sealed class DatabaseApproval : AggregateRoot
{
    /// <summary>Reason 最大长度。</summary>
    public const int ReasonMaxLength = 512;

    /// <summary>租户业务标识。</summary>
    public string TenantNId { get; private set; }

    /// <summary>审批记录业务标识。</summary>
    public string ApprovalNId { get; private set; }

    /// <summary>被审批计划业务标识。</summary>
    public string PlanNId { get; private set; }

    /// <summary>被审批计划校验和快照。</summary>
    public string PlanChecksum { get; private set; }

    /// <summary>被审批计划目标状态指纹快照。</summary>
    public string TargetStateFingerprint { get; private set; }

    /// <summary>审批人业务标识。</summary>
    public string ApprovedByUserNId { get; private set; }

    /// <summary>审批理由(可选)。</summary>
    public string? Reason { get; private set; }

    /// <summary>审批时间。</summary>
    public DateTimeOffset ApprovedOn { get; private set; }

    /// <summary>审批有效期截止(apply 时需未过期)。</summary>
    public DateTimeOffset ExpiresOn { get; private set; }

    /// <summary>审批状态(创建即 Approved;Rejected/Expired 供后续流程标记)。</summary>
    public ApprovalStatus Status { get; private set; }

    private DatabaseApproval()
    {
        TenantNId = string.Empty;
        ApprovalNId = string.Empty;
        PlanNId = string.Empty;
        PlanChecksum = string.Empty;
        TargetStateFingerprint = string.Empty;
        ApprovedByUserNId = string.Empty;
    }

    private DatabaseApproval(
        string tenantNId,
        string approvalNId,
        string planNId,
        string planChecksum,
        string targetStateFingerprint,
        string approvedByUserNId,
        string? reason,
        DateTimeOffset approvedOn,
        DateTimeOffset expiresOn)
    {
        TenantNId = DatabaseOrchestrationGuard.RequireNId(tenantNId, "审批记录的租户标识不能为空。");
        ApprovalNId = DatabaseOrchestrationGuard.RequireNId(approvalNId, "审批记录标识不能为空。");
        PlanNId = DatabaseOrchestrationGuard.RequireNId(planNId, "被审批计划标识不能为空。");
        PlanChecksum = DatabaseOrchestrationGuard.RequireSha256Hex(planChecksum, "计划校验和不能为空。");
        TargetStateFingerprint = DatabaseOrchestrationGuard.RequireSha256Hex(targetStateFingerprint, "目标状态指纹不能为空。");
        ApprovedByUserNId = DatabaseOrchestrationGuard.RequireNId(approvedByUserNId, "审批人标识不能为空。");
        Reason = DatabaseOrchestrationGuard.TrimOrNull(reason, ReasonMaxLength, $"审批理由长度不能超过 {ReasonMaxLength} 个字符。");
        ApprovedOn = approvedOn;
        ExpiresOn = expiresOn;
        Status = ApprovalStatus.Approved;
    }

    /// <summary>持久化层重建专用构造,不重新校验。</summary>
    internal DatabaseApproval(
        Guid id,
        string tenantNId,
        string approvalNId,
        string planNId,
        string planChecksum,
        string targetStateFingerprint,
        string approvedByUserNId,
        string? reason,
        DateTimeOffset approvedOn,
        DateTimeOffset expiresOn,
        ApprovalStatus status,
        bool isFrozen,
        bool isLocked,
        bool isDeleted,
        string entityType,
        DateTimeOffset createdOn,
        DateTimeOffset lastUpdatedOn,
        long optimisticVersion,
        Guid concurrencyVersion)
        : base()
    {
        Id = id;
        TenantNId = tenantNId;
        ApprovalNId = approvalNId;
        PlanNId = planNId;
        PlanChecksum = planChecksum;
        TargetStateFingerprint = targetStateFingerprint;
        ApprovedByUserNId = approvedByUserNId;
        Reason = reason;
        ApprovedOn = approvedOn;
        ExpiresOn = expiresOn;
        Status = status;
        IsFrozen = isFrozen;
        IsLocked = isLocked;
        IsDeleted = isDeleted;
        EntityType = entityType;
        CreatedOn = createdOn;
        LastUpdatedOn = lastUpdatedOn;
        OptimisticVersion = optimisticVersion;
        ConcurrencyVersion = concurrencyVersion;
    }

    /// <summary>创建审批记录(Status = Approved)。</summary>
    public static DatabaseApproval Create(
        string tenantNId,
        string approvalNId,
        string planNId,
        string planChecksum,
        string targetStateFingerprint,
        string approvedByUserNId,
        string? reason,
        DateTimeOffset approvedOn,
        DateTimeOffset expiresOn)
        => new(
            tenantNId,
            approvalNId,
            planNId,
            planChecksum,
            targetStateFingerprint,
            approvedByUserNId,
            reason,
            approvedOn,
            expiresOn);

    /// <summary>审批是否对给定计划在当前时刻有效:Approved + 未过期 + 校验和/指纹与计划一致。</summary>
    public bool IsValidFor(string planChecksum, string targetStateFingerprint, DateTimeOffset now) =>
        Status == ApprovalStatus.Approved
        && now <= ExpiresOn
        && string.Equals(PlanChecksum, planChecksum, StringComparison.Ordinal)
        && string.Equals(TargetStateFingerprint, targetStateFingerprint, StringComparison.Ordinal);
}
