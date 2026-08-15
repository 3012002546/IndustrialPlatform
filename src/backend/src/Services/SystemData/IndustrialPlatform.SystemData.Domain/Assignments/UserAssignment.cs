using IndustrialPlatform.SharedKernel.Entities;
using IndustrialPlatform.SharedKernel.Exceptions;

namespace IndustrialPlatform.SystemData.Domain.Assignments;

/// <summary>
/// 用户任职聚合根(05 方案 §7.4 / §8.1 <c>system_data_user_assignment</c>)。
/// 时间化多任职:有效期区间、主任职标志与取消固化;投影状态由当前时间派生。
/// 同用户/岗位区间重叠与主任职覆盖由应用层在按用户 advisory lock 内结合
/// <see cref="AssignmentScheduleRules"/> 裁决;本聚合只维护自身区间与状态机不变量。
/// </summary>
public sealed class UserAssignment : AggregateRoot
{
    /// <summary>业务标识最大长度(对齐 NId)。</summary>
    public const int NIdMaxLength = 128;

    /// <summary>用户业务标识最大长度。</summary>
    public const int UserNIdMaxLength = 128;

    /// <summary>用户显示名快照最大长度。</summary>
    public const int DisplayNameSnapshotMaxLength = 256;

    /// <summary>组织业务标识最大长度。</summary>
    public const int OrganizationNIdMaxLength = 128;

    /// <summary>岗位业务标识最大长度。</summary>
    public const int PositionNIdMaxLength = 128;

    /// <summary>取消原因最大长度。</summary>
    public const int CancelReasonMaxLength = 512;

    /// <summary>租户业务标识。</summary>
    public string TenantNId { get; private set; }

    /// <summary>任职业务标识(租户内唯一)。</summary>
    public string NId { get; private set; }

    /// <summary>任职业务标识大写规范化值。</summary>
    public string NormalizedNId { get; private set; }

    /// <summary>任职用户业务标识(不建 Identity 外键)。</summary>
    public string UserNId { get; private set; }

    /// <summary>用户显示名快照(目录变化不追溯改写历史)。</summary>
    public string UserDisplayNameSnapshot { get; private set; }

    /// <summary>任职所属组织业务标识(必须与岗位当前所属组织一致)。</summary>
    public string OrganizationNId { get; private set; }

    /// <summary>任职岗位业务标识。</summary>
    public string PositionNId { get; private set; }

    /// <summary>岗位主键快照(复合外键引用)。</summary>
    public Guid PositionId { get; private set; }

    /// <summary>岗位删除状态快照(复合外键同步)。</summary>
    public bool PositionIsDeleted { get; private set; }

    /// <summary>是否主任职(每个租户同一时点最多一条有效主任职)。</summary>
    public bool IsPrimary { get; private set; }

    /// <summary>生效开始时间(含,左闭右开)。</summary>
    public DateTimeOffset EffectiveFrom { get; private set; }

    /// <summary>生效结束时间(不含,可空表示开放)。</summary>
    public DateTimeOffset? EffectiveTo { get; private set; }

    /// <summary>任职持久化状态。</summary>
    public AssignmentState State { get; private set; }

    /// <summary>取消时间(取消即固化)。</summary>
    public DateTimeOffset? CancelledOn { get; private set; }

    /// <summary>取消原因(可选)。</summary>
    public string? CancelReason { get; private set; }

    private UserAssignment()
    {
        TenantNId = string.Empty;
        NId = string.Empty;
        NormalizedNId = string.Empty;
        UserNId = string.Empty;
        UserDisplayNameSnapshot = string.Empty;
        OrganizationNId = string.Empty;
        PositionNId = string.Empty;
    }

    private UserAssignment(
        string tenantNId,
        string nId,
        string normalizedNId,
        string userNId,
        string userDisplayNameSnapshot,
        string organizationNId,
        string positionNId,
        Guid positionId,
        bool positionIsDeleted,
        bool isPrimary,
        DateTimeOffset effectiveFrom,
        DateTimeOffset? effectiveTo)
    {
        TenantNId = SystemDataDomainGuard.RequireNId(tenantNId, "任职的租户标识不能为空。").Value;
        var pair = SystemDataDomainGuard.RequireNId(nId, "任职业务标识不能为空。");
        NId = pair.Value;
        NormalizedNId = pair.Normalized;
        UserNId = SystemDataDomainGuard.RequireNId(userNId, "任职用户标识不能为空。").Value;
        UserDisplayNameSnapshot = SystemDataDomainGuard.RequireTrimmedNonEmpty(
            userDisplayNameSnapshot, "任职用户显示名快照不能为空。", DisplayNameSnapshotMaxLength,
            $"任职用户显示名快照长度不能超过 {DisplayNameSnapshotMaxLength} 个字符。");
        OrganizationNId = SystemDataDomainGuard.RequireNId(organizationNId, "任职组织标识不能为空。").Value;
        PositionNId = SystemDataDomainGuard.RequireNId(positionNId, "任职岗位标识不能为空。").Value;
        PositionId = positionId;
        PositionIsDeleted = positionIsDeleted;
        IsPrimary = isPrimary;
        EffectiveFrom = effectiveFrom;
        EffectiveTo = effectiveTo;
        State = AssignmentState.Enabled;
    }

    /// <summary>持久化层重建专用构造,不重新校验。</summary>
    internal UserAssignment(
        Guid id,
        string tenantNId,
        string nId,
        string normalizedNId,
        string userNId,
        string userDisplayNameSnapshot,
        string organizationNId,
        string positionNId,
        Guid positionId,
        bool positionIsDeleted,
        bool isPrimary,
        DateTimeOffset effectiveFrom,
        DateTimeOffset? effectiveTo,
        AssignmentState state,
        DateTimeOffset? cancelledOn,
        string? cancelReason,
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
        NId = nId;
        NormalizedNId = normalizedNId;
        UserNId = userNId;
        UserDisplayNameSnapshot = userDisplayNameSnapshot;
        OrganizationNId = organizationNId;
        PositionNId = positionNId;
        PositionId = positionId;
        PositionIsDeleted = positionIsDeleted;
        IsPrimary = isPrimary;
        EffectiveFrom = effectiveFrom;
        EffectiveTo = effectiveTo;
        State = state;
        CancelledOn = cancelledOn;
        CancelReason = cancelReason;
        IsFrozen = isFrozen;
        IsLocked = isLocked;
        IsDeleted = isDeleted;
        EntityType = entityType;
        CreatedOn = createdOn;
        LastUpdatedOn = lastUpdatedOn;
        OptimisticVersion = optimisticVersion;
        ConcurrencyVersion = concurrencyVersion;
    }

    /// <summary>
    /// 创建任职。要求组织和岗位均活动、岗位未删除、任职组织与岗位归属一致;
    /// 区间有效(结束晚于开始)且创建时未已结束。区间重叠与主任职覆盖由应用层裁决。
    /// </summary>
    public static UserAssignment Create(
        string tenantNId,
        string nId,
        string userNId,
        string userDisplayNameSnapshot,
        string organizationNId,
        string positionNId,
        Guid positionId,
        bool positionIsDeleted,
        bool organizationActive,
        bool positionActive,
        bool organizationMatchesPosition,
        bool isPrimary,
        DateTimeOffset effectiveFrom,
        DateTimeOffset? effectiveTo,
        DateTimeOffset now)
    {
        if (positionIsDeleted)
        {
            throw new BusinessException("任职不能绑定已删除的岗位。");
        }

        if (!organizationActive)
        {
            throw new BusinessException("任职只能创建在活动组织下。");
        }

        if (!positionActive)
        {
            throw new BusinessException("任职只能创建在活动岗位下。");
        }

        if (!organizationMatchesPosition)
        {
            throw new BusinessException("任职的组织标识必须与岗位所属组织一致。");
        }

        var period = EffectivePeriod.Create(effectiveFrom, effectiveTo);
        if (period.EffectiveTo is { } to && to <= now)
        {
            throw new BusinessException("任职在创建时已结束,不能创建。");
        }

        return new UserAssignment(
            tenantNId,
            nId,
            nId,
            userNId,
            userDisplayNameSnapshot,
            organizationNId,
            positionNId,
            positionId,
            positionIsDeleted,
            isPrimary,
            period.EffectiveFrom,
            period.EffectiveTo);
    }

    /// <summary>当前时间投影(左闭右开)。</summary>
    public AssignmentProjection GetProjection(DateTimeOffset now)
    {
        if (State == AssignmentState.Cancelled)
        {
            return AssignmentProjection.Cancelled;
        }

        if (now < EffectiveFrom)
        {
            return AssignmentProjection.Scheduled;
        }

        if (EffectiveTo is { } to && now >= to)
        {
            return AssignmentProjection.Ended;
        }

        return AssignmentProjection.Current;
    }

    /// <summary>更新计划中任职的区间;只允许投影为 Scheduled 的任职,且新开始时间仍在未来。</summary>
    public void UpdateScheduledPeriod(DateTimeOffset effectiveFrom, DateTimeOffset? effectiveTo, DateTimeOffset now)
    {
        EnsureCanModify();
        if (GetProjection(now) != AssignmentProjection.Scheduled)
        {
            throw new BusinessException("只有计划中的任职可以更新区间。");
        }

        var period = EffectivePeriod.Create(effectiveFrom, effectiveTo);
        if (period.EffectiveFrom <= now)
        {
            throw new BusinessException("调整后的任职开始时间必须晚于当前时间。");
        }

        EffectiveFrom = period.EffectiveFrom;
        EffectiveTo = period.EffectiveTo;
        Touch();
    }

    /// <summary>结束当前任职(EffectiveTo = now);只允许投影为 Current 的任职。</summary>
    public void End(DateTimeOffset now)
    {
        EnsureCanModify();
        if (GetProjection(now) != AssignmentProjection.Current)
        {
            throw new BusinessException("只有当前任职可以结束。");
        }

        EffectiveTo = now;
        Touch();
    }

    /// <summary>
    /// 在未来时点结束任职(主任职原子切换的历史拆分):只调整未来端点,不改写已发生历史。
    /// 要求任职有效、结束点在开始之后且早于现有结束点。
    /// </summary>
    public void ScheduleEnd(DateTimeOffset cutoff)
    {
        EnsureCanModify();
        if (State != AssignmentState.Enabled)
        {
            throw new BusinessException("已取消的任职不能调整结束时间。");
        }

        if (cutoff <= EffectiveFrom)
        {
            throw new BusinessException("任职结束时间必须晚于开始时间。");
        }

        if (EffectiveTo is { } to && cutoff >= to)
        {
            throw new BusinessException("任职已在目标时点前结束,无需调整。");
        }

        EffectiveTo = cutoff;
        Touch();
    }

    /// <summary>取消计划中的任职(State=Cancelled 固化);只允许投影为 Scheduled 的任职。</summary>
    public void Cancel(DateTimeOffset now, string? reason)
    {
        EnsureCanModify();
        if (GetProjection(now) != AssignmentProjection.Scheduled)
        {
            throw new BusinessException("只有计划中的任职可以取消。");
        }

        State = AssignmentState.Cancelled;
        CancelledOn = now;
        CancelReason = SystemDataDomainGuard.TrimOrNull(
            reason, CancelReasonMaxLength, $"取消原因长度不能超过 {CancelReasonMaxLength} 个字符。");
        Touch();
    }

    /// <summary>标记/取消主任职;只允许未结束的有效任职(投影为 Scheduled 或 Current)。</summary>
    public void MarkPrimary(bool isPrimary, DateTimeOffset now)
    {
        EnsureCanModify();
        var projection = GetProjection(now);
        if (State != AssignmentState.Enabled || projection is AssignmentProjection.Ended or AssignmentProjection.Cancelled)
        {
            throw new BusinessException("只有未结束的有效任职可以标记主任职。");
        }

        IsPrimary = isPrimary;
        Touch();
    }
}
