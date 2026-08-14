using IndustrialPlatform.Identity.Domain.Roles;
using IndustrialPlatform.Identity.Domain.Users;
using IndustrialPlatform.SharedKernel.Entities;
using IndustrialPlatform.SharedKernel.Exceptions;

namespace IndustrialPlatform.Identity.Domain.UserGroups;

/// <summary>
/// 用户组聚合根(§29A.2)。租户级安全主体,仅用于批量组织账号并统一授予角色;
/// 不代表部门、岗位、班组或任职,不构成行政层级,第一阶段禁止用户组嵌套(成员只能是用户)。
/// 业务标识 NId 与规范化值创建后不可变;资料、状态、成员与组角色通过对应方法变更,
/// 变更受删除/锁定/冻结保护;重复成员/角色分配与解除均为幂等。
/// </summary>
public sealed class UserGroup : AggregateRoot
{
    /// <summary>用户组名称最大长度。</summary>
    public const int NameMaxLength = 64;

    /// <summary>描述最大长度。</summary>
    public const int DescriptionMaxLength = 512;

    /// <summary>租户编码(不透明字符串,不做 NId 规范化)。</summary>
    public string TenantNId { get; private set; }

    /// <summary>用户组业务标识,创建后不可变。</summary>
    public string NId { get; private set; }

    /// <summary>规范化业务标识(大写),创建后不可变。</summary>
    public string NormalizedNId { get; private set; }

    /// <summary>用户组名称。</summary>
    public string Name { get; private set; }

    /// <summary>描述。</summary>
    public string? Description { get; private set; }

    /// <summary>状态:禁用组立即停止贡献角色,但保留成员与角色配置以便恢复。</summary>
    public UserGroupStatus Status { get; private set; }

    private readonly List<UserGroupMembership> _memberships = [];

    /// <summary>成员关系(含已移除的软删除关系),成员只能是用户(禁止嵌套)。</summary>
    public IReadOnlyCollection<UserGroupMembership> Memberships => _memberships;

    private readonly List<UserGroupRole> _roles = [];

    /// <summary>组角色关系(含已解除的软删除关系),只允许角色,不允许直接分配权限。</summary>
    public IReadOnlyCollection<UserGroupRole> Roles => _roles;

    /// <summary>ORM 反序列化专用构造,非空字符串字段初始化后由持久化框架填充。</summary>
    private UserGroup()
    {
        TenantNId = string.Empty;
        NId = string.Empty;
        NormalizedNId = string.Empty;
        Name = string.Empty;
    }

    private UserGroup(string tenantNId, string nId, string name, string? description)
        : this()
    {
        var trimmedTenantNId = RequireTrimmedNonEmpty(tenantNId, "租户编码不能为空。");
        var nIdValue = Identities.NId.Create(nId);
        var trimmedName = RequireTrimmedNonEmpty(
            name,
            "用户组名称不能为空。",
            NameMaxLength,
            $"用户组名称长度不能超过 {NameMaxLength} 个字符。");

        TenantNId = trimmedTenantNId;
        NId = nIdValue.Value;
        NormalizedNId = nIdValue.Normalized;
        Name = trimmedName;
        Description = TrimOrNull(description, DescriptionMaxLength, $"用户组描述长度不能超过 {DescriptionMaxLength} 个字符。");
        Status = UserGroupStatus.Active;
    }

    /// <summary>持久化层重建专用构造,恢复全部业务字段、成员/角色关系与生命周期状态,不重新校验。</summary>
    internal UserGroup(
        Guid id,
        string tenantNId,
        string nId,
        string normalizedNId,
        string name,
        string? description,
        UserGroupStatus status,
        IReadOnlyCollection<UserGroupMembership> memberships,
        IReadOnlyCollection<UserGroupRole> roles,
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
        Description = description;
        Status = status;
        _memberships.AddRange(memberships);
        _roles.AddRange(roles);
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
    /// 创建用户组。业务标识按 NId 规则校验并规范化,状态初始为 Active,发布创建事件。
    /// </summary>
    /// <param name="tenantNId">租户编码。</param>
    /// <param name="nId">用户组业务标识。</param>
    /// <param name="name">用户组名称。</param>
    /// <param name="description">描述,可为空。</param>
    /// <returns>创建完成的用户组聚合根。</returns>
    public static UserGroup Create(string tenantNId, string nId, string name, string? description)
    {
        var group = new UserGroup(tenantNId, nId, name, description);
        group.AddDomainEvent(new UserGroupCreatedEvent(group.TenantNId, group.NId, group.Name, group.Status));
        return group;
    }

    /// <summary>
    /// 变更用户组名称与描述。业务标识不变,发布资料变更事件。
    /// </summary>
    /// <param name="name">新名称。</param>
    /// <param name="description">新描述,可为空。</param>
    public void ChangeProfile(string name, string? description)
    {
        EnsureCanModify();

        Name = RequireTrimmedNonEmpty(
            name,
            "用户组名称不能为空。",
            NameMaxLength,
            $"用户组名称长度不能超过 {NameMaxLength} 个字符。");
        Description = TrimOrNull(description, DescriptionMaxLength, $"用户组描述长度不能超过 {DescriptionMaxLength} 个字符。");

        AddDomainEvent(new UserGroupChangedEvent(TenantNId, NId, Name, Status));
        Touch();
    }

    /// <summary>
    /// 加入成员(§29A.2):跨租户或已删除用户拒绝;重复加入幂等返回。
    /// 成员只能是用户(禁止用户组嵌套),发布成员变更事件。
    /// </summary>
    /// <param name="user">待加入的用户聚合根。</param>
    public void AssignMember(User user)
    {
        ArgumentNullException.ThrowIfNull(user);
        EnsureCanModify();

        if (user.TenantNId != TenantNId)
        {
            throw new BusinessException("不能加入其他租户的用户。");
        }

        if (user.IsDeleted)
        {
            throw new BusinessException("已删除的用户不能加入用户组。");
        }

        if (_memberships.Any(m => m.UserId == user.Id && !m.IsDeleted))
        {
            return;
        }

        _memberships.Add(new UserGroupMembership(TenantNId, Id, IsDeleted, user.Id, user.IsDeleted));
        AddDomainEvent(new UserGroupMembershipChangedEvent(TenantNId, NId, user.NId));
        Touch();
    }

    /// <summary>
    /// 移除成员:找不到活动关系时幂等返回。实际移除时发布成员变更事件。
    /// 最后系统管理员保护由应用层按权威计数守卫(§29A.3)。
    /// </summary>
    /// <param name="user">待移除的用户聚合根。</param>
    public void RemoveMember(User user)
    {
        ArgumentNullException.ThrowIfNull(user);
        EnsureCanModify();

        var relation = _memberships.FirstOrDefault(m => m.UserId == user.Id && !m.IsDeleted);
        if (relation is null)
        {
            return;
        }

        relation.MarkDeleted();
        AddDomainEvent(new UserGroupMembershipChangedEvent(TenantNId, NId, user.NId));
        Touch();
    }

    /// <summary>
    /// 分配组角色(§29A.2):跨租户或已删除角色拒绝;重复分配幂等返回。
    /// 只允许角色,不允许直接分配权限,发布组角色变更事件。
    /// </summary>
    /// <param name="role">待分配的角色聚合根。</param>
    public void AssignRole(Role role)
    {
        ArgumentNullException.ThrowIfNull(role);
        EnsureCanModify();

        if (role.TenantNId != TenantNId)
        {
            throw new BusinessException("不能分配其他租户的角色。");
        }

        if (role.IsDeleted)
        {
            throw new BusinessException("已删除的角色不能分配。");
        }

        if (_roles.Any(r => r.RoleId == role.Id && !r.IsDeleted))
        {
            return;
        }

        _roles.Add(new UserGroupRole(TenantNId, Id, IsDeleted, role.Id, role.IsDeleted));
        AddDomainEvent(new UserGroupRolesChangedEvent(TenantNId, NId));
        Touch();
    }

    /// <summary>
    /// 解除组角色:找不到活动关系时幂等返回。实际解除时发布组角色变更事件。
    /// 最后系统管理员保护由应用层按权威计数守卫(§29A.3)。
    /// </summary>
    /// <param name="role">待解除的角色聚合根。</param>
    public void RemoveRole(Role role)
    {
        ArgumentNullException.ThrowIfNull(role);
        EnsureCanModify();

        var relation = _roles.FirstOrDefault(r => r.RoleId == role.Id && !r.IsDeleted);
        if (relation is null)
        {
            return;
        }

        relation.MarkDeleted();
        AddDomainEvent(new UserGroupRolesChangedEvent(TenantNId, NId));
        Touch();
    }

    /// <summary>
    /// 禁用用户组:状态置为已禁用,立即停止贡献角色(成员与角色配置保留),发布状态变更事件。
    /// 最后系统管理员保护由应用层按权威计数守卫;已禁用时幂等。
    /// </summary>
    public void Disable()
    {
        EnsureCanModify();

        if (Status == UserGroupStatus.Disabled)
        {
            return;
        }

        Status = UserGroupStatus.Disabled;
        AddDomainEvent(new UserGroupChangedEvent(TenantNId, NId, Name, Status));
        Touch();
    }

    /// <summary>
    /// 启用用户组:状态置为正常,恢复贡献角色,发布状态变更事件。已启用时幂等。
    /// </summary>
    public void Enable()
    {
        EnsureCanModify();

        if (Status == UserGroupStatus.Active)
        {
            return;
        }

        Status = UserGroupStatus.Active;
        AddDomainEvent(new UserGroupChangedEvent(TenantNId, NId, Name, Status));
        Touch();
    }

    /// <summary>
    /// 安全删除(§29A.3):软删除全部活动成员与组角色关系并标记删除。
    /// 最后系统管理员守卫由应用层按权威计数执行;§29A.6 未定义组删除集成事件,仅写操作审计。
    /// </summary>
    public void DeleteForTombstone()
    {
        EnsureCanModify();

        foreach (var membership in _memberships.Where(m => !m.IsDeleted))
        {
            membership.MarkDeleted();
        }

        foreach (var role in _roles.Where(r => !r.IsDeleted))
        {
            role.MarkDeleted();
        }

        MarkDeleted();
    }

    /// <summary>
    /// 恢复墓碑(§29A.3):仅清除删除标记并保持禁用,不自动恢复成员/角色关系。
    /// 恢复后为 Disabled,不贡献任何角色。
    /// </summary>
    public void RestoreTombstone()
    {
        if (!IsDeleted)
        {
            throw new BusinessException("用户组未删除，无需恢复。");
        }

        Restore();
        Status = UserGroupStatus.Disabled;
        Touch();
    }

    private static string RequireTrimmedNonEmpty(string? value, string emptyMessage)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ValidationException(emptyMessage);
        }

        return value.Trim();
    }

    private static string RequireTrimmedNonEmpty(string? value, string emptyMessage, int maxLength, string tooLongMessage)
    {
        var trimmed = RequireTrimmedNonEmpty(value, emptyMessage);
        if (trimmed.Length > maxLength)
        {
            throw new ValidationException(tooLongMessage);
        }

        return trimmed;
    }

    private static string? TrimOrNull(string? value, int maxLength, string tooLongMessage)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        if (trimmed.Length > maxLength)
        {
            throw new ValidationException(tooLongMessage);
        }

        return trimmed;
    }
}
