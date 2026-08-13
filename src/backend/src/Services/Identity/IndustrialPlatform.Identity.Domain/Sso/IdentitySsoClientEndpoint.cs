using IndustrialPlatform.SharedKernel.Entities;

namespace IndustrialPlatform.Identity.Domain.Sso;

/// <summary>
/// 平台 SSO Client 端点(§26.7),Client 聚合子实体。无租户独立字段,
/// 复合外键引用 SsoClient <c>(Id, IsDeleted)</c>;同一 Client 下
/// 端点业务标识与 <c>EndpointType + NormalizedUri</c> 分别唯一。
/// </summary>
public sealed class IdentitySsoClientEndpoint : Entity
{
    /// <summary>URI 最大长度。</summary>
    public const int UriMaxLength = 2048;

    /// <summary>所属 Client 主键。</summary>
    public Guid SsoClientId { get; }

    /// <summary>创建时所属 Client 软删除状态快照,由持久化层维护。</summary>
    public bool SsoClientIsDeleted { get; }

    /// <summary>端点业务标识(Client 内唯一),创建后不可变。</summary>
    public string NId { get; }

    /// <summary>端点类型。</summary>
    public SsoClientEndpointType EndpointType { get; }

    /// <summary>原始 URI(精确匹配依据)。</summary>
    public string Uri { get; private set; }

    /// <summary>规范化 URI(小写),用于同 Client 下唯一性。</summary>
    public string NormalizedUri { get; }

    /// <summary>是否启用。</summary>
    public bool Enabled { get; private set; }

    /// <summary>由 Client 聚合在登记端点时创建。</summary>
    internal IdentitySsoClientEndpoint(
        Guid ssoClientId,
        bool ssoClientIsDeleted,
        string nId,
        SsoClientEndpointType endpointType,
        string uri)
    {
        var nIdValue = Identities.NId.Create(nId);
        SsoClientId = ssoClientId;
        SsoClientIsDeleted = ssoClientIsDeleted;
        NId = nIdValue.Value;
        EndpointType = endpointType;
        Uri = SsoGuard.RequireTrimmedNonEmpty(uri, "端点 URI 不能为空。", UriMaxLength, $"端点 URI 长度不能超过 {UriMaxLength} 个字符。");
        NormalizedUri = Uri.ToLowerInvariant();
        Enabled = true;
    }

    /// <summary>持久化层重建专用构造。</summary>
    internal IdentitySsoClientEndpoint(
        Guid id,
        Guid ssoClientId,
        bool ssoClientIsDeleted,
        string nId,
        SsoClientEndpointType endpointType,
        string uri,
        string normalizedUri,
        bool enabled,
        bool isFrozen,
        bool isLocked,
        bool isDeleted,
        string entityType,
        DateTimeOffset createdOn,
        DateTimeOffset lastUpdatedOn,
        long optimisticVersion,
        Guid concurrencyVersion)
        : base(id)
    {
        SsoClientId = ssoClientId;
        SsoClientIsDeleted = ssoClientIsDeleted;
        NId = nId;
        EndpointType = endpointType;
        Uri = uri;
        NormalizedUri = normalizedUri;
        Enabled = enabled;
        IsFrozen = isFrozen;
        IsLocked = isLocked;
        IsDeleted = isDeleted;
        EntityType = entityType;
        CreatedOn = createdOn;
        LastUpdatedOn = lastUpdatedOn;
        OptimisticVersion = optimisticVersion;
        ConcurrencyVersion = concurrencyVersion;
    }

    /// <summary>启用/禁用端点。</summary>
    internal void SetEnabled(bool enabled)
    {
        if (IsDeleted)
        {
            return;
        }

        if (Enabled == enabled)
        {
            return;
        }

        Enabled = enabled;
        Touch();
    }
}
