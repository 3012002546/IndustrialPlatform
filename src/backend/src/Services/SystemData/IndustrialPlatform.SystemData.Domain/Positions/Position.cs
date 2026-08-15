using IndustrialPlatform.SharedKernel.Entities;
using IndustrialPlatform.SharedKernel.Exceptions;

namespace IndustrialPlatform.SystemData.Domain.Positions;

/// <summary>
/// 岗位聚合根(05 方案 §7.3 / §8.1 <c>system_data_position</c>)。
/// 岗位实例专属于一个 Active 行政组织,创建后 OrganizationNId 不可修改,不提供跨组织移动
/// (岗位调整到其他组织必须新建目标岗位并显式迁移/结束任职)。
/// </summary>
public sealed class Position : AggregateRoot
{
    /// <summary>名称最大长度。</summary>
    public const int NameMaxLength = 128;

    /// <summary>描述最大长度。</summary>
    public const int DescriptionMaxLength = 512;

    /// <summary>业务标识最大长度(对齐 NId)。</summary>
    public const int NIdMaxLength = 128;

    /// <summary>租户业务标识。</summary>
    public string TenantNId { get; private set; }

    /// <summary>岗位业务标识(租户内全历史唯一)。</summary>
    public string NId { get; private set; }

    /// <summary>岗位业务标识大写规范化值。</summary>
    public string NormalizedNId { get; private set; }

    /// <summary>所属组织业务标识(创建后不可修改)。</summary>
    public string OrganizationNId { get; private set; }

    /// <summary>所属组织主键快照(复合外键引用)。</summary>
    public Guid OrganizationId { get; private set; }

    /// <summary>所属组织删除状态快照(复合外键同步)。</summary>
    public bool OrganizationIsDeleted { get; private set; }

    /// <summary>岗位名称。</summary>
    public string Name { get; private set; }

    /// <summary>岗位描述(可选)。</summary>
    public string? Description { get; private set; }

    /// <summary>同组织内的显示顺序。</summary>
    public int DisplayOrder { get; private set; }

    /// <summary>岗位状态。</summary>
    public PositionStatus Status { get; private set; }

    private Position()
    {
        TenantNId = string.Empty;
        NId = string.Empty;
        NormalizedNId = string.Empty;
        OrganizationNId = string.Empty;
        Name = string.Empty;
    }

    private Position(
        string tenantNId,
        string nId,
        string normalizedNId,
        string organizationNId,
        Guid organizationId,
        bool organizationIsDeleted,
        string name,
        string? description,
        int displayOrder)
    {
        TenantNId = SystemDataDomainGuard.RequireNId(tenantNId, "岗位的租户标识不能为空。").Value;
        var pair = SystemDataDomainGuard.RequireNId(nId, "岗位业务标识不能为空。");
        NId = pair.Value;
        NormalizedNId = pair.Normalized;
        OrganizationNId = SystemDataDomainGuard.RequireNId(organizationNId, "岗位所属组织标识不能为空。").Value;
        OrganizationId = organizationId;
        OrganizationIsDeleted = organizationIsDeleted;
        Name = SystemDataDomainGuard.RequireTrimmedNonEmpty(
            name, "岗位名称不能为空。", NameMaxLength, $"岗位名称长度不能超过 {NameMaxLength} 个字符。");
        Description = SystemDataDomainGuard.TrimOrNull(
            description, DescriptionMaxLength, $"岗位描述长度不能超过 {DescriptionMaxLength} 个字符。");
        DisplayOrder = SystemDataDomainGuard.RequireNonNegative(displayOrder, "显示顺序不能为负数。");
        Status = PositionStatus.Active;
    }

    /// <summary>持久化层重建专用构造,不重新校验。</summary>
    internal Position(
        Guid id,
        string tenantNId,
        string nId,
        string normalizedNId,
        string organizationNId,
        Guid organizationId,
        bool organizationIsDeleted,
        string name,
        string? description,
        int displayOrder,
        PositionStatus status,
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
        OrganizationNId = organizationNId;
        OrganizationId = organizationId;
        OrganizationIsDeleted = organizationIsDeleted;
        Name = name;
        Description = description;
        DisplayOrder = displayOrder;
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

    /// <summary>
    /// 创建岗位。要求所属组织存在、同租户、活动且未删除。
    /// </summary>
    public static Position Create(
        string tenantNId,
        string nId,
        string organizationTenantNId,
        string organizationNId,
        Guid organizationId,
        bool organizationIsDeleted,
        bool organizationActive,
        string name,
        string? description,
        int displayOrder)
    {
        if (!string.Equals(organizationTenantNId, tenantNId, StringComparison.Ordinal))
        {
            throw new BusinessException("岗位必须属于组织所在的同一租户。");
        }

        if (organizationIsDeleted)
        {
            throw new BusinessException("岗位不能绑定已删除的组织。");
        }

        if (!organizationActive)
        {
            throw new BusinessException("岗位只能创建在活动组织下。");
        }

        return new Position(
            tenantNId,
            nId,
            nId,
            organizationNId,
            organizationId,
            organizationIsDeleted,
            name,
            description,
            displayOrder);
    }

    /// <summary>重命名(名称非空且不超长)。</summary>
    public void Rename(string name)
    {
        EnsureCanModify();
        Name = SystemDataDomainGuard.RequireTrimmedNonEmpty(
            name, "岗位名称不能为空。", NameMaxLength, $"岗位名称长度不能超过 {NameMaxLength} 个字符。");
        Touch();
    }

    /// <summary>修改描述(可空)。</summary>
    public void ChangeDescription(string? description)
    {
        EnsureCanModify();
        Description = SystemDataDomainGuard.TrimOrNull(
            description, DescriptionMaxLength, $"岗位描述长度不能超过 {DescriptionMaxLength} 个字符。");
        Touch();
    }

    /// <summary>调整同组织显示顺序(非负)。</summary>
    public void ChangeDisplayOrder(int displayOrder)
    {
        EnsureCanModify();
        DisplayOrder = SystemDataDomainGuard.RequireNonNegative(displayOrder, "显示顺序不能为负数。");
        Touch();
    }

    /// <summary>停用岗位。存在当前或未来有效任职时拒绝,不隐式级联。</summary>
    public void Deactivate(bool hasActiveOrFutureAssignments)
    {
        EnsureCanModify();
        if (Status != PositionStatus.Active)
        {
            throw new BusinessException("岗位已处于停用状态。");
        }

        if (hasActiveOrFutureAssignments)
        {
            throw new BusinessException("存在当前或未来有效任职,不能停用岗位。");
        }

        Status = PositionStatus.Inactive;
        Touch();
    }

    /// <summary>恢复岗位;所属组织必须活动。</summary>
    public void Activate(bool organizationActive)
    {
        EnsureCanModify();
        if (Status != PositionStatus.Inactive)
        {
            throw new BusinessException("岗位已处于活动状态。");
        }

        if (!organizationActive)
        {
            throw new BusinessException("岗位所属组织必须处于活动状态才能恢复岗位。");
        }

        Status = PositionStatus.Active;
        Touch();
    }
}
