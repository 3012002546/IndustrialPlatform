using IndustrialPlatform.Identity.Domain.Permissions;
using IndustrialPlatform.SharedKernel.Entities;
using IndustrialPlatform.SharedKernel.Exceptions;

namespace IndustrialPlatform.Identity.Domain.Roles;

/// <summary>
/// 角色聚合根。业务标识 NId、规范化值与系统角色标记创建后不可变;
/// 权限分配/解除通过 <see cref="RolePermission"/> 关系实体维护并发布权限缓存失效事件。
/// </summary>
public sealed class Role : AggregateRoot
{
    /// <summary>角色名称最大长度。</summary>
    public const int NameMaxLength = 64;

    /// <summary>描述最大长度。</summary>
    public const int DescriptionMaxLength = 512;

    /// <summary>租户编码(不透明字符串,不做 NId 规范化)。</summary>
    public string TenantNId { get; private set; }

    /// <summary>角色业务标识,创建后不可变。</summary>
    public string NId { get; private set; }

    /// <summary>规范化业务标识(大写),创建后不可变。</summary>
    public string NormalizedNId { get; private set; }

    /// <summary>角色名称。</summary>
    public string Name { get; private set; }

    /// <summary>描述。</summary>
    public string? Description { get; private set; }

    /// <summary>是否系统角色,创建后不可变;系统角色禁止删除。</summary>
    public bool IsSystem { get; private set; }

    private readonly List<RolePermission> _permissions = [];

    /// <summary>已分配的角色权限关系(含已解除的软删除关系)。</summary>
    public IReadOnlyCollection<RolePermission> Permissions => _permissions;

    /// <summary>ORM 反序列化专用构造,非空字符串字段初始化后由持久化框架填充。</summary>
    private Role()
    {
        TenantNId = string.Empty;
        NId = string.Empty;
        NormalizedNId = string.Empty;
        Name = string.Empty;
    }

    private Role(string tenantNId, string nId, string name, string? description, bool isSystem)
        : this()
    {
        var trimmedTenantNId = RequireTrimmedNonEmpty(tenantNId, "租户编码不能为空。");
        var nIdValue = Identities.NId.Create(nId);
        var trimmedName = RequireTrimmedNonEmpty(
            name,
            "角色名称不能为空。",
            NameMaxLength,
            $"角色名称长度不能超过 {NameMaxLength} 个字符。");

        TenantNId = trimmedTenantNId;
        NId = nIdValue.Value;
        NormalizedNId = nIdValue.Normalized;
        Name = trimmedName;
        Description = TrimOrNull(description, DescriptionMaxLength, $"角色描述长度不能超过 {DescriptionMaxLength} 个字符。");
        IsSystem = isSystem;
    }

    /// <summary>
    /// 创建角色。业务标识按 NId 规则校验并规范化;系统角色标记创建后不可变。
    /// </summary>
    /// <param name="tenantNId">租户编码。</param>
    /// <param name="nId">角色业务标识。</param>
    /// <param name="name">角色名称。</param>
    /// <param name="description">描述,可为空。</param>
    /// <param name="isSystem">是否系统角色。</param>
    /// <returns>创建完成的角色聚合根。</returns>
    public static Role Create(string tenantNId, string nId, string name, string? description, bool isSystem)
        => new Role(tenantNId, nId, name, description, isSystem);

    /// <summary>
    /// 变更角色名称与描述。业务标识与系统角色标记不变,不发布领域事件。
    /// </summary>
    /// <param name="name">新名称。</param>
    /// <param name="description">新描述,可为空。</param>
    public void ChangeProfile(string name, string? description)
    {
        EnsureCanModify();

        Name = RequireTrimmedNonEmpty(
            name,
            "角色名称不能为空。",
            NameMaxLength,
            $"角色名称长度不能超过 {NameMaxLength} 个字符。");
        Description = TrimOrNull(description, DescriptionMaxLength, $"角色描述长度不能超过 {DescriptionMaxLength} 个字符。");

        Touch();
    }

    /// <summary>
    /// 分配权限:已删除或重复分配时抛出业务异常,否则新增关系并发布
    /// <see cref="RolePermissionsChangedEvent"/> 作为权限缓存失效信号。
    /// </summary>
    /// <param name="permission">待分配的平台权限聚合根。</param>
    public void AssignPermission(Permission permission)
    {
        ArgumentNullException.ThrowIfNull(permission);
        EnsureCanModify();

        if (permission.IsDeleted)
        {
            throw new BusinessException("已删除的权限不能分配。");
        }

        if (_permissions.Any(p => p.PermissionId == permission.Id && !p.IsDeleted))
        {
            throw new BusinessException("角色已拥有该权限。");
        }

        _permissions.Add(new RolePermission(Id, IsDeleted, permission.Id, permission.IsDeleted));
        AddDomainEvent(new RolePermissionsChangedEvent(TenantNId, NId, permission.NId));
        Touch();
    }

    /// <summary>
    /// 解除权限:找不到活动关系时幂等返回,否则软删除关系并发布权限缓存失效事件。
    /// </summary>
    /// <param name="permission">待解除的平台权限聚合根。</param>
    public void UnassignPermission(Permission permission)
    {
        ArgumentNullException.ThrowIfNull(permission);
        EnsureCanModify();

        var relation = _permissions.FirstOrDefault(p => p.PermissionId == permission.Id && !p.IsDeleted);
        if (relation is null)
        {
            return;
        }

        relation.MarkDeleted();
        AddDomainEvent(new RolePermissionsChangedEvent(TenantNId, NId, permission.NId));
        Touch();
    }

    /// <summary>
    /// 删除角色:系统角色禁止删除,非系统角色标记软删除。
    /// 角色的用户关系失效由持久化层的影子列批量更新处理。
    /// </summary>
    public void Delete()
    {
        EnsureCanModify();

        if (IsSystem)
        {
            throw new BusinessException("系统角色不能删除。");
        }

        MarkDeleted();
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
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ValidationException(emptyMessage);
        }

        var trimmed = value.Trim();
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
