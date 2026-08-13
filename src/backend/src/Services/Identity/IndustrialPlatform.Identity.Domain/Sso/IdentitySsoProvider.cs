using IndustrialPlatform.SharedKernel.Entities;

namespace IndustrialPlatform.Identity.Domain.Sso;

/// <summary>
/// 租户级联邦登录 Provider(§26.3)。业务标识 NId 与规范化值创建后不可变;
/// 协议字段通过 <see cref="UpdateConfiguration"/> 整体变更。密钥只保存引用,
/// 不存储客户端 Secret 或证书私钥。
/// </summary>
public sealed class IdentitySsoProvider : AggregateRoot
{
    /// <summary>名称最大长度。</summary>
    public const int NameMaxLength = 64;

    /// <summary>Issuer/Metadata 地址最大长度。</summary>
    public const int UrlMaxLength = 1024;

    /// <summary>OAuth ClientId / SAML EntityId 最大长度。</summary>
    public const int ClientIdMaxLength = 256;

    /// <summary>Secret/证书引用最大长度。</summary>
    public const int SecretReferenceMaxLength = 256;

    /// <summary>回调路径最大长度。</summary>
    public const int CallbackPathMaxLength = 256;

    /// <summary>租户编码(不透明字符串,不做 NId 规范化)。</summary>
    public string TenantNId { get; private set; }

    /// <summary>业务标识,创建后不可变。</summary>
    public string NId { get; private set; }

    /// <summary>规范化业务标识(大写),创建后不可变。</summary>
    public string NormalizedNId { get; private set; }

    /// <summary>显示名称。</summary>
    public string Name { get; private set; }

    /// <summary>协议(Oidc / Saml2)。</summary>
    public SsoProtocol Protocol { get; private set; }

    /// <summary>OIDC Authority 或 SAML 元数据地址。</summary>
    public string AuthorityOrMetadataUrl { get; private set; }

    /// <summary>OIDC ClientId 或 SAML EntityId。</summary>
    public string ClientIdOrEntityId { get; private set; }

    /// <summary>密钥/证书引用(凭据名或引用键),绝不保存明文 Secret。</summary>
    public string? SecretOrCertificateReference { get; private set; }

    /// <summary>回调路径(相对路径,如 /identity/api/v1/sso/callback/oidc/{nId})。</summary>
    public string CallbackPath { get; private set; }

    /// <summary>是否启用。</summary>
    public bool Enabled { get; private set; }

    /// <summary>登录页是否自动跳转该 Provider。</summary>
    public bool AutoRedirect { get; private set; }

    /// <summary>账号供给策略(默认 ExistingOnly)。</summary>
    public SsoProvisioningMode ProvisioningMode { get; private set; }

    /// <summary>注销策略(默认 LocalOnly)。</summary>
    public SsoLogoutMode LogoutMode { get; private set; }

    private readonly List<string> _allowedEmailDomains = [];

    /// <summary>JIT 允许的邮箱域(小写),仅 ProvisioningMode=JustInTime 时使用。</summary>
    public IReadOnlyList<string> AllowedEmailDomains => _allowedEmailDomains;

    private readonly List<string> _jitDefaultRoleNIds = [];

    /// <summary>JIT 创建本地用户时的默认角色业务标识。</summary>
    public IReadOnlyList<string> JitDefaultRoleNIds => _jitDefaultRoleNIds;

    /// <summary>ORM 反序列化专用构造。</summary>
    private IdentitySsoProvider()
    {
        TenantNId = string.Empty;
        NId = string.Empty;
        NormalizedNId = string.Empty;
        Name = string.Empty;
        AuthorityOrMetadataUrl = string.Empty;
        ClientIdOrEntityId = string.Empty;
        CallbackPath = string.Empty;
    }

    private IdentitySsoProvider(
        string tenantNId,
        string nId,
        string name,
        SsoProtocol protocol,
        string authorityOrMetadataUrl,
        string clientIdOrEntityId,
        string? secretOrCertificateReference,
        string callbackPath,
        bool autoRedirect,
        SsoProvisioningMode provisioningMode,
        SsoLogoutMode logoutMode,
        IReadOnlyList<string> allowedEmailDomains,
        IReadOnlyList<string> jitDefaultRoleNIds)
        : this()
    {
        TenantNId = SsoGuard.RequireTrimmedNonEmpty(tenantNId, "租户编码不能为空。");
        var nIdValue = Identities.NId.Create(nId);
        NId = nIdValue.Value;
        NormalizedNId = nIdValue.Normalized;

        Name = SsoGuard.RequireTrimmedNonEmpty(name, "Provider 名称不能为空。", NameMaxLength, $"Provider 名称长度不能超过 {NameMaxLength} 个字符。");
        AuthorityOrMetadataUrl = SsoGuard.RequireTrimmedNonEmpty(authorityOrMetadataUrl, "Issuer/元数据地址不能为空。", UrlMaxLength, $"地址长度不能超过 {UrlMaxLength} 个字符。");
        ClientIdOrEntityId = SsoGuard.RequireTrimmedNonEmpty(clientIdOrEntityId, "ClientId/EntityId 不能为空。", ClientIdMaxLength, $"ClientId/EntityId 长度不能超过 {ClientIdMaxLength} 个字符。");
        SecretOrCertificateReference = SsoGuard.TrimOrNull(secretOrCertificateReference, SecretReferenceMaxLength, $"密钥引用长度不能超过 {SecretReferenceMaxLength} 个字符。");
        CallbackPath = SsoGuard.RequireTrimmedNonEmpty(callbackPath, "回调路径不能为空。", CallbackPathMaxLength, $"回调路径长度不能超过 {CallbackPathMaxLength} 个字符。");

        Protocol = protocol;
        Enabled = true;
        AutoRedirect = autoRedirect;
        ProvisioningMode = provisioningMode;
        LogoutMode = logoutMode;
        _allowedEmailDomains.AddRange(NormalizeEmailDomains(allowedEmailDomains));
        _jitDefaultRoleNIds.AddRange(TrimList(jitDefaultRoleNIds, "JIT 默认角色标识不能为空。"));
    }

    /// <summary>
    /// 持久化层重建专用构造:恢复全部业务字段与生命周期状态,不重新校验。
    /// </summary>
    internal IdentitySsoProvider(
        Guid id,
        string tenantNId,
        string nId,
        string normalizedNId,
        string name,
        SsoProtocol protocol,
        string authorityOrMetadataUrl,
        string clientIdOrEntityId,
        string? secretOrCertificateReference,
        string callbackPath,
        bool enabled,
        bool autoRedirect,
        SsoProvisioningMode provisioningMode,
        SsoLogoutMode logoutMode,
        IReadOnlyList<string> allowedEmailDomains,
        IReadOnlyList<string> jitDefaultRoleNIds,
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
        Protocol = protocol;
        AuthorityOrMetadataUrl = authorityOrMetadataUrl;
        ClientIdOrEntityId = clientIdOrEntityId;
        SecretOrCertificateReference = secretOrCertificateReference;
        CallbackPath = callbackPath;
        Enabled = enabled;
        AutoRedirect = autoRedirect;
        ProvisioningMode = provisioningMode;
        LogoutMode = logoutMode;
        _allowedEmailDomains.AddRange(allowedEmailDomains);
        _jitDefaultRoleNIds.AddRange(jitDefaultRoleNIds);
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
    /// 创建 Provider。业务标识按 NId 规则校验并规范化;默认启用。
    /// </summary>
    public static IdentitySsoProvider Create(
        string tenantNId,
        string nId,
        string name,
        SsoProtocol protocol,
        string authorityOrMetadataUrl,
        string clientIdOrEntityId,
        string? secretOrCertificateReference,
        string callbackPath,
        bool autoRedirect,
        SsoProvisioningMode provisioningMode,
        SsoLogoutMode logoutMode,
        IReadOnlyList<string> allowedEmailDomains,
        IReadOnlyList<string> jitDefaultRoleNIds)
        => new IdentitySsoProvider(
            tenantNId,
            nId,
            name,
            protocol,
            authorityOrMetadataUrl,
            clientIdOrEntityId,
            secretOrCertificateReference,
            callbackPath,
            autoRedirect,
            provisioningMode,
            logoutMode,
            allowedEmailDomains,
            jitDefaultRoleNIds);

    /// <summary>
    /// 整体更新协议配置。业务标识、租户与密钥引用保持不变(密钥引用单独通过
    /// <see cref="ChangeSecretReference"/> 更新)。JIT 默认角色仅在 JustInTime 下使用。
    /// </summary>
    public void UpdateConfiguration(
        string name,
        SsoProtocol protocol,
        string authorityOrMetadataUrl,
        string clientIdOrEntityId,
        string callbackPath,
        bool autoRedirect,
        SsoProvisioningMode provisioningMode,
        SsoLogoutMode logoutMode,
        IReadOnlyList<string> allowedEmailDomains,
        IReadOnlyList<string> jitDefaultRoleNIds)
    {
        EnsureCanModify();

        Name = SsoGuard.RequireTrimmedNonEmpty(name, "Provider 名称不能为空。", NameMaxLength, $"Provider 名称长度不能超过 {NameMaxLength} 个字符。");
        Protocol = protocol;
        AuthorityOrMetadataUrl = SsoGuard.RequireTrimmedNonEmpty(authorityOrMetadataUrl, "Issuer/元数据地址不能为空。", UrlMaxLength, $"地址长度不能超过 {UrlMaxLength} 个字符。");
        ClientIdOrEntityId = SsoGuard.RequireTrimmedNonEmpty(clientIdOrEntityId, "ClientId/EntityId 不能为空。", ClientIdMaxLength, $"ClientId/EntityId 长度不能超过 {ClientIdMaxLength} 个字符。");
        CallbackPath = SsoGuard.RequireTrimmedNonEmpty(callbackPath, "回调路径不能为空。", CallbackPathMaxLength, $"回调路径长度不能超过 {CallbackPathMaxLength} 个字符。");
        AutoRedirect = autoRedirect;
        ProvisioningMode = provisioningMode;
        LogoutMode = logoutMode;
        _allowedEmailDomains.Clear();
        _allowedEmailDomains.AddRange(NormalizeEmailDomains(allowedEmailDomains));
        _jitDefaultRoleNIds.Clear();
        _jitDefaultRoleNIds.AddRange(TrimList(jitDefaultRoleNIds, "JIT 默认角色标识不能为空。"));

        Touch();
    }

    /// <summary>更新密钥/证书引用(不接收明文密钥)。</summary>
    public void ChangeSecretReference(string? secretOrCertificateReference)
    {
        EnsureCanModify();

        SecretOrCertificateReference = SsoGuard.TrimOrNull(
            secretOrCertificateReference,
            SecretReferenceMaxLength,
            $"密钥引用长度不能超过 {SecretReferenceMaxLength} 个字符。");

        Touch();
    }

    /// <summary>启用/禁用 Provider。</summary>
    public void SetEnabled(bool enabled)
    {
        EnsureCanModify();

        if (Enabled == enabled)
        {
            return;
        }

        Enabled = enabled;
        Touch();
    }

    /// <summary>校验邮箱是否命中允许域(JIT 匹配)。无允许域时返回 false。</summary>
    public bool IsEmailAllowed(string? email)
    {
        if (string.IsNullOrWhiteSpace(email) || _allowedEmailDomains.Count == 0)
        {
            return false;
        }

        var atIndex = email.LastIndexOf('@');
        if (atIndex < 0 || atIndex == email.Length - 1)
        {
            return false;
        }

        var domain = email[(atIndex + 1)..].ToLowerInvariant();
        return _allowedEmailDomains.Contains(domain);
    }

    private static string[] NormalizeEmailDomains(IReadOnlyList<string> domains)
        => domains
            .Where(d => !string.IsNullOrWhiteSpace(d))
            .Select(SsoGuard.NormalizeEmailDomain)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    private static string[] TrimList(IReadOnlyList<string> values, string emptyMessage)
        => values
            .Select(v => SsoGuard.RequireTrimmedNonEmpty(v, emptyMessage))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
}
