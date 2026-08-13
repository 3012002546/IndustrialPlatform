using IndustrialPlatform.SharedKernel.Entities;

namespace IndustrialPlatform.Identity.Domain.Sso;

/// <summary>
/// 外部账号映射(§26.3)。通过两组复合外键引用 Provider 与 User 的
/// <c>(Id, IsDeleted)</c>;业务引用一律使用 providerNId / userNId,不暴露内部 Guid。
/// 匹配优先使用不可变 external subject,禁止仅凭 displayName 绑定。
/// </summary>
public sealed class IdentityExternalAccount : AggregateRoot
{
    /// <summary>外部主体标识最大长度。</summary>
    public const int SubjectMaxLength = 256;

    /// <summary>外部名称最大长度。</summary>
    public const int ExternalNameMaxLength = 256;

    /// <summary>外部邮箱最大长度。</summary>
    public const int ExternalEmailMaxLength = 256;

    /// <summary>业务标识,创建后不可变。</summary>
    public string NId { get; private set; }

    /// <summary>规范化业务标识(大写),创建后不可变。</summary>
    public string NormalizedNId { get; private set; }

    /// <summary>所属 Provider 主键(复合外键)。</summary>
    public Guid SsoProviderId { get; private set; }

    /// <summary>创建时 Provider 软删除状态快照,由持久化层维护。</summary>
    public bool SsoProviderIsDeleted { get; private set; }

    /// <summary>不可变外部主体标识(IdP 侧稳定标识)。</summary>
    public string ExternalSubject { get; private set; }

    /// <summary>绑定平台用户主键(复合外键)。</summary>
    public Guid UserId { get; private set; }

    /// <summary>创建时用户软删除状态快照,由持久化层维护。</summary>
    public bool UserIsDeleted { get; private set; }

    /// <summary>外部显示名称。</summary>
    public string? ExternalName { get; private set; }

    /// <summary>外部邮箱。</summary>
    public string? ExternalEmail { get; private set; }

    /// <summary>最近一次成功联邦登录时间。</summary>
    public DateTimeOffset? LastLoginOn { get; private set; }

    /// <summary>ORM 反序列化专用构造。</summary>
    private IdentityExternalAccount()
    {
        NId = string.Empty;
        NormalizedNId = string.Empty;
        ExternalSubject = string.Empty;
    }

    private IdentityExternalAccount(
        string nId,
        Guid ssoProviderId,
        bool ssoProviderIsDeleted,
        string externalSubject,
        Guid userId,
        bool userIsDeleted,
        string? externalName,
        string? externalEmail)
        : this()
    {
        var nIdValue = Identities.NId.Create(nId);
        NId = nIdValue.Value;
        NormalizedNId = nIdValue.Normalized;

        SsoProviderId = ssoProviderId;
        SsoProviderIsDeleted = ssoProviderIsDeleted;
        ExternalSubject = SsoGuard.RequireTrimmedNonEmpty(externalSubject, "外部主体标识不能为空。", SubjectMaxLength, $"外部主体标识长度不能超过 {SubjectMaxLength} 个字符。");
        UserId = userId;
        UserIsDeleted = userIsDeleted;
        ExternalName = SsoGuard.TrimOrNull(externalName, ExternalNameMaxLength, $"外部名称长度不能超过 {ExternalNameMaxLength} 个字符。");
        ExternalEmail = SsoGuard.TrimOrNull(externalEmail, ExternalEmailMaxLength, $"外部邮箱长度不能超过 {ExternalEmailMaxLength} 个字符。");
        LastLoginOn = null;
    }

    /// <summary>
    /// 持久化层重建专用构造:恢复全部业务字段与生命周期状态,不重新校验。
    /// </summary>
    internal IdentityExternalAccount(
        Guid id,
        string nId,
        string normalizedNId,
        Guid ssoProviderId,
        bool ssoProviderIsDeleted,
        string externalSubject,
        Guid userId,
        bool userIsDeleted,
        string? externalName,
        string? externalEmail,
        DateTimeOffset? lastLoginOn,
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
        NId = nId;
        NormalizedNId = normalizedNId;
        SsoProviderId = ssoProviderId;
        SsoProviderIsDeleted = ssoProviderIsDeleted;
        ExternalSubject = externalSubject;
        UserId = userId;
        UserIsDeleted = userIsDeleted;
        ExternalName = externalName;
        ExternalEmail = externalEmail;
        LastLoginOn = lastLoginOn;
        IsFrozen = isFrozen;
        IsLocked = isLocked;
        IsDeleted = isDeleted;
        EntityType = entityType;
        CreatedOn = createdOn;
        LastUpdatedOn = lastUpdatedOn;
        OptimisticVersion = optimisticVersion;
        ConcurrencyVersion = concurrencyVersion;
    }

    /// <summary>创建外部账号映射。</summary>
    public static IdentityExternalAccount Create(
        string nId,
        Guid ssoProviderId,
        bool ssoProviderIsDeleted,
        string externalSubject,
        Guid userId,
        bool userIsDeleted,
        string? externalName,
        string? externalEmail)
        => new IdentityExternalAccount(
            nId,
            ssoProviderId,
            ssoProviderIsDeleted,
            externalSubject,
            userId,
            userIsDeleted,
            externalName,
            externalEmail);

    /// <summary>更新外部资料(名称、邮箱),不改变 subject。</summary>
    public void UpdateProfile(string? externalName, string? externalEmail)
    {
        EnsureCanModify();

        ExternalName = SsoGuard.TrimOrNull(externalName, ExternalNameMaxLength, $"外部名称长度不能超过 {ExternalNameMaxLength} 个字符。");
        ExternalEmail = SsoGuard.TrimOrNull(externalEmail, ExternalEmailMaxLength, $"外部邮箱长度不能超过 {ExternalEmailMaxLength} 个字符。");
        Touch();
    }

    /// <summary>记录一次成功联邦登录。</summary>
    public void RecordLogin(DateTimeOffset now)
    {
        EnsureCanModify();

        LastLoginOn = now;
        Touch();
    }

    /// <summary>解绑:标记软删除。</summary>
    public void Unbind()
    {
        EnsureCanModify();
        MarkDeleted();
    }
}
