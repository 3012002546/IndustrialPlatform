using IndustrialPlatform.SharedKernel.Entities;
using IndustrialPlatform.SharedKernel.Exceptions;

namespace IndustrialPlatform.SystemData.Domain.Organizations;

/// <summary>
/// 行政组织聚合根(05 方案 §7.2 / §8.1 <c>system_data_organization</c>)。
/// 统一有类型树:公司仅根、同一租户允许多个根公司、父子同租户、类型矩阵约束、自引用复合外键。
/// 组织通过 <see cref="OrganizationStatus"/> 退出使用,公共 API 不提供软删除。
/// </summary>
public sealed class AdministrativeOrganization : AggregateRoot
{
    /// <summary>名称最大长度。</summary>
    public const int NameMaxLength = 128;

    /// <summary>业务标识最大长度(对齐 NId)。</summary>
    public const int NIdMaxLength = 128;

    /// <summary>租户业务标识。</summary>
    public string TenantNId { get; private set; }

    /// <summary>组织业务标识(租户内全历史唯一)。</summary>
    public string NId { get; private set; }

    /// <summary>组织业务标识大写规范化值(唯一性约束与比较)。</summary>
    public string NormalizedNId { get; private set; }

    /// <summary>组织名称。</summary>
    public string Name { get; private set; }

    /// <summary>组织类型。</summary>
    public AdministrativeOrganizationType Type { get; private set; }

    /// <summary>父组织业务标识(公司根为 null)。</summary>
    public string? ParentOrganizationNId { get; private set; }

    /// <summary>父组织主键快照(复合外键引用)。</summary>
    public Guid? ParentOrganizationId { get; private set; }

    /// <summary>父组织删除状态快照(复合外键同步)。</summary>
    public bool ParentOrganizationIsDeleted { get; private set; }

    /// <summary>同父节点内的显示顺序。</summary>
    public int DisplayOrder { get; private set; }

    /// <summary>组织状态。</summary>
    public OrganizationStatus Status { get; private set; }

    /// <summary>树修订号:每次移动等结构变化递增,供移动预览过期校验。</summary>
    public long OrganizationRevision { get; private set; }

    private AdministrativeOrganization()
    {
        TenantNId = string.Empty;
        NId = string.Empty;
        NormalizedNId = string.Empty;
        Name = string.Empty;
        Type = AdministrativeOrganizationType.Company;
        DisplayOrder = 0;
        Status = OrganizationStatus.Active;
    }

    private AdministrativeOrganization(
        string tenantNId,
        string nId,
        string normalizedNId,
        string name,
        AdministrativeOrganizationType type,
        string? parentOrganizationNId,
        Guid? parentOrganizationId,
        bool parentOrganizationIsDeleted,
        int displayOrder)
    {
        TenantNId = SystemDataDomainGuard.RequireNId(tenantNId, "组织的租户标识不能为空。").Value;
        var pair = SystemDataDomainGuard.RequireNId(nId, "组织业务标识不能为空。");
        NId = pair.Value;
        NormalizedNId = pair.Normalized;
        Name = SystemDataDomainGuard.RequireTrimmedNonEmpty(
            name, "组织名称不能为空。", NameMaxLength, $"组织名称长度不能超过 {NameMaxLength} 个字符。");
        Type = type;
        ParentOrganizationNId = parentOrganizationNId;
        ParentOrganizationId = parentOrganizationId;
        ParentOrganizationIsDeleted = parentOrganizationIsDeleted;
        DisplayOrder = SystemDataDomainGuard.RequireNonNegative(displayOrder, "显示顺序不能为负数。");
        Status = OrganizationStatus.Active;
        OrganizationRevision = 1;
    }

    /// <summary>持久化层重建专用构造,不重新校验、不发布事件。</summary>
    internal AdministrativeOrganization(
        Guid id,
        string tenantNId,
        string nId,
        string normalizedNId,
        string name,
        AdministrativeOrganizationType type,
        string? parentOrganizationNId,
        Guid? parentOrganizationId,
        bool parentOrganizationIsDeleted,
        int displayOrder,
        OrganizationStatus status,
        long organizationRevision,
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
        Name = name;
        Type = type;
        ParentOrganizationNId = parentOrganizationNId;
        ParentOrganizationId = parentOrganizationId;
        ParentOrganizationIsDeleted = parentOrganizationIsDeleted;
        DisplayOrder = displayOrder;
        Status = status;
        OrganizationRevision = organizationRevision;
        IsFrozen = isFrozen;
        IsLocked = isLocked;
        IsDeleted = isDeleted;
        EntityType = entityType;
        CreatedOn = createdOn;
        LastUpdatedOn = lastUpdatedOn;
        OptimisticVersion = optimisticVersion;
        ConcurrencyVersion = concurrencyVersion;
    }

    /// <summary>创建根公司(多根公司允许;父节点为空)。</summary>
    public static AdministrativeOrganization CreateRootCompany(
        string tenantNId,
        string nId,
        string name,
        int displayOrder)
        => new(tenantNId, nId, nId, name, AdministrativeOrganizationType.Company, null, null, false, displayOrder);

    /// <summary>
    /// 在活动父组织下创建子组织。父组织必须存在、同租户、活动且类型满足矩阵;
    /// 公司只能作为根,不能作为子组织创建。
    /// </summary>
    public static AdministrativeOrganization CreateChild(
        string tenantNId,
        string nId,
        string name,
        AdministrativeOrganizationType type,
        string parentTenantNId,
        string parentNId,
        Guid parentId,
        bool parentIsDeleted,
        bool parentActive,
        AdministrativeOrganizationType parentType,
        int displayOrder)
    {
        if (type == AdministrativeOrganizationType.Company)
        {
            throw new ValidationException("公司只能作为根组织创建。");
        }

        ValidateParentBinding(tenantNId, parentTenantNId, parentIsDeleted, parentActive, type, parentType);
        return new AdministrativeOrganization(
            tenantNId,
            nId,
            nId,
            name,
            type,
            parentNId,
            parentId,
            parentIsDeleted,
            displayOrder);
    }

    /// <summary>重命名(名称非空且不超长)。</summary>
    public void Rename(string name)
    {
        EnsureCanModify();
        Name = SystemDataDomainGuard.RequireTrimmedNonEmpty(
            name, "组织名称不能为空。", NameMaxLength, $"组织名称长度不能超过 {NameMaxLength} 个字符。");
        Touch();
    }

    /// <summary>调整同父显示顺序(非负)。</summary>
    public void ChangeDisplayOrder(int displayOrder)
    {
        EnsureCanModify();
        DisplayOrder = SystemDataDomainGuard.RequireNonNegative(displayOrder, "显示顺序不能为负数。");
        Touch();
    }

    /// <summary>
    /// 停用组织。存在活动子组织、活动岗位或当前/未来有效任职时拒绝,不隐式级联。
    /// 依赖计数由应用层在锁内统计后传入。
    /// </summary>
    public void Deactivate(long activeChildCount, long activePositionCount, long activeOrFutureAssignmentCount)
    {
        EnsureCanModify();
        if (Status != OrganizationStatus.Active)
        {
            throw new BusinessException("组织已处于停用状态。");
        }

        if (activeChildCount > 0 || activePositionCount > 0 || activeOrFutureAssignmentCount > 0)
        {
            throw new BusinessException("存在有效下级组织、岗位或任职,不能停用组织。");
        }

        Status = OrganizationStatus.Inactive;
        Touch();
    }

    /// <summary>恢复组织;存在父组织时父组织必须活动。</summary>
    public void Activate(bool parentIsActive)
    {
        EnsureCanModify();
        if (Status != OrganizationStatus.Inactive)
        {
            throw new BusinessException("组织已处于活动状态。");
        }

        if (ParentOrganizationId is not null && !parentIsActive)
        {
            throw new BusinessException("父组织必须处于活动状态才能恢复组织。");
        }

        Status = OrganizationStatus.Active;
        Touch();
    }

    /// <summary>
    /// 移动前返回影响摘要:校验目标父组织(类型矩阵、同租户、活动、非自指、非子树成员),
    /// 并给出移动成功后的树修订号。摘要携带的修订号与提交时的双并发版本共同决定 409 过期。
    /// </summary>
    public OrganizationMovePreview PreviewMove(
        string? newParentNId,
        Guid? newParentId,
        bool newParentIsDeleted,
        string newParentTenantNId,
        AdministrativeOrganizationType? newParentType,
        bool newParentActive,
        IReadOnlyCollection<string> subtreeNIds,
        long subtreeOrganizationCount,
        long subtreePositionCount,
        long subtreeAssignmentCount)
    {
        EnsureCanModify();
        ValidateMoveTarget(
            newParentNId, newParentIsDeleted, newParentTenantNId, newParentType, newParentActive, subtreeNIds);
        return new OrganizationMovePreview(
            NId,
            OrganizationRevision + 1,
            subtreeOrganizationCount,
            subtreePositionCount,
            subtreeAssignmentCount,
            subtreeOrganizationCount + subtreePositionCount + subtreeAssignmentCount,
            DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// 提交移动。要求传入与当前修订对应的预览(预览后未发生其他结构变化),
    /// 只改变移动根的父引用并推进树修订号,后代、岗位与任职的稳定 NId 不变。
    /// </summary>
    public void Move(
        OrganizationMovePreview preview,
        string? newParentNId,
        Guid? newParentId,
        bool newParentIsDeleted,
        string newParentTenantNId,
        AdministrativeOrganizationType? newParentType,
        bool newParentActive,
        IReadOnlyCollection<string> subtreeNIds)
    {
        EnsureCanModify();
        if (preview.OrganizationRevision != OrganizationRevision + 1)
        {
            throw new BusinessException("移动预览已过期,请重新预览后提交。");
        }

        ValidateMoveTarget(
            newParentNId, newParentIsDeleted, newParentTenantNId, newParentType, newParentActive, subtreeNIds);
        ParentOrganizationNId = newParentNId;
        ParentOrganizationId = newParentId;
        ParentOrganizationIsDeleted = newParentIsDeleted;
        OrganizationRevision = preview.OrganizationRevision;
        Touch();
    }

    /// <summary>类型矩阵校验:childType 是否可挂载在 parentType 下。</summary>
    public static bool CanHaveParent(AdministrativeOrganizationType childType, AdministrativeOrganizationType parentType) =>
        childType switch
        {
            AdministrativeOrganizationType.Department =>
                parentType is AdministrativeOrganizationType.Company or AdministrativeOrganizationType.Department,
            AdministrativeOrganizationType.Section =>
                parentType is AdministrativeOrganizationType.Department or AdministrativeOrganizationType.Section,
            AdministrativeOrganizationType.Team =>
                parentType is AdministrativeOrganizationType.Department or AdministrativeOrganizationType.Section
                    or AdministrativeOrganizationType.Team,
            _ => false,
        };

    private static void ValidateParentBinding(
        string tenantNId,
        string parentTenantNId,
        bool parentIsDeleted,
        bool parentActive,
        AdministrativeOrganizationType childType,
        AdministrativeOrganizationType parentType)
    {
        if (!string.Equals(parentTenantNId, tenantNId, StringComparison.Ordinal))
        {
            throw new BusinessException("父子组织必须属于同一租户。");
        }

        if (parentIsDeleted)
        {
            throw new BusinessException("不能基于已删除的父组织创建子组织。");
        }

        if (!parentActive)
        {
            throw new BusinessException("停用组织不能新增子组织。");
        }

        if (!CanHaveParent(childType, parentType))
        {
            throw new ValidationException($"组织类型 {childType} 不能作为 {parentType} 的子组织。");
        }
    }

    private void ValidateMoveTarget(
        string? newParentNId,
        bool newParentIsDeleted,
        string newParentTenantNId,
        AdministrativeOrganizationType? newParentType,
        bool newParentActive,
        IReadOnlyCollection<string> subtreeNIds)
    {
        if (Status != OrganizationStatus.Active)
        {
            throw new BusinessException("停用组织不能移动。");
        }

        if (Type == AdministrativeOrganizationType.Company)
        {
            throw new BusinessException("公司为根节点,不能移动。");
        }

        if (string.IsNullOrWhiteSpace(newParentNId))
        {
            throw new BusinessException("非公司组织移动时必须指定新父组织。");
        }

        if (string.Equals(newParentNId, NId, StringComparison.Ordinal))
        {
            throw new BusinessException("不能将组织移动到自身之下。");
        }

        if (!string.Equals(newParentTenantNId, TenantNId, StringComparison.Ordinal))
        {
            throw new BusinessException("组织只能在同一租户内移动,禁止跨租户移动。");
        }

        if (newParentIsDeleted || !newParentActive)
        {
            throw new BusinessException("目标父组织必须处于活动状态。");
        }

        if (newParentType is not { } parentType || !CanHaveParent(Type, parentType))
        {
            throw new ValidationException($"组织类型 {Type} 不能移动到 {newParentType?.ToString() ?? "未知"} 父组织之下。");
        }

        if (subtreeNIds.Contains(newParentNId))
        {
            throw new BusinessException("移动目标不能是待移动子树的成员,否则形成祖先循环。");
        }
    }
}
