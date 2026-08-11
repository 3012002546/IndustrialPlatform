using IndustrialPlatform.SharedKernel.Entities;
using IndustrialPlatform.SharedKernel.Exceptions;

namespace IndustrialPlatform.Identity.Domain.Permissions;

/// <summary>
/// 平台权限聚合根(§9.2)。业务标识 NId、父级 NId 与类型创建后不可变;
/// 权限由平台种子数据创建维护,不提供任意创建 UI。
/// </summary>
public sealed class Permission : AggregateRoot
{
    /// <summary>权限名称最大长度。</summary>
    public const int NameMaxLength = 128;

    /// <summary>描述最大长度。</summary>
    public const int DescriptionMaxLength = 512;

    /// <summary>业务标识,创建后不可变。</summary>
    public string NId { get; private set; }

    /// <summary>规范化业务标识(大写),创建后不可变。</summary>
    public string NormalizedNId { get; private set; }

    /// <summary>权限名称。</summary>
    public string Name { get; private set; }

    /// <summary>权限类型,创建后不可变。</summary>
    public PermissionType Type { get; private set; }

    /// <summary>父级权限业务标识,可为空;仅做格式校验,父级结构由持久化层建立。</summary>
    public string? ParentPermissionNId { get; private set; }

    /// <summary>描述。</summary>
    public string? Description { get; private set; }

    /// <summary>权限状态,创建即启用。</summary>
    public PermissionStatus Status { get; private set; }

    /// <summary>ORM 反序列化专用构造,非空字符串字段初始化后由持久化框架填充。</summary>
    private Permission()
    {
        NId = string.Empty;
        NormalizedNId = string.Empty;
        Name = string.Empty;
    }

    private Permission(
        string nId,
        string name,
        PermissionType type,
        string? parentPermissionNId,
        string? description)
        : this()
    {
        var nIdValue = Identities.NId.Create(nId);
        var trimmedName = RequireTrimmedNonEmpty(
            name,
            "权限名称不能为空。",
            NameMaxLength,
            $"权限名称长度不能超过 {NameMaxLength} 个字符。");
        var trimmedDescription = TrimOrNull(
            description,
            DescriptionMaxLength,
            $"权限描述长度不能超过 {DescriptionMaxLength} 个字符。");

        if (!Enum.IsDefined(type))
        {
            throw new ValidationException("无效的权限类型。");
        }

        string? parentPermission = null;
        if (!string.IsNullOrWhiteSpace(parentPermissionNId))
        {
            parentPermission = Identities.NId.Create(parentPermissionNId).Value;
        }

        NId = nIdValue.Value;
        NormalizedNId = nIdValue.Normalized;
        Name = trimmedName;
        Type = type;
        ParentPermissionNId = parentPermission;
        Description = trimmedDescription;
        Status = PermissionStatus.Active;
    }

    /// <summary>
    /// 创建权限。业务标识与父级 NId 按 NId 规则校验并规范化;类型须为已定义枚举值。
    /// </summary>
    /// <param name="nId">权限业务标识。</param>
    /// <param name="name">权限名称。</param>
    /// <param name="type">权限类型。</param>
    /// <param name="parentPermissionNId">父级权限业务标识,可为空。</param>
    /// <param name="description">描述,可为空。</param>
    /// <returns>创建完成的权限聚合根。</returns>
    public static Permission Create(
        string nId,
        string name,
        PermissionType type,
        string? parentPermissionNId,
        string? description)
        => new Permission(nId, name, type, parentPermissionNId, description);

    /// <summary>
    /// 变更权限名称与描述。业务标识、类型、父级与状态不变,不发布领域事件。
    /// </summary>
    /// <param name="name">新名称。</param>
    /// <param name="description">新描述,可为空。</param>
    public void ChangeProfile(string name, string? description)
    {
        EnsureCanModify();

        Name = RequireTrimmedNonEmpty(
            name,
            "权限名称不能为空。",
            NameMaxLength,
            $"权限名称长度不能超过 {NameMaxLength} 个字符。");
        Description = TrimOrNull(
            description,
            DescriptionMaxLength,
            $"权限描述长度不能超过 {DescriptionMaxLength} 个字符。");

        Touch();
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
