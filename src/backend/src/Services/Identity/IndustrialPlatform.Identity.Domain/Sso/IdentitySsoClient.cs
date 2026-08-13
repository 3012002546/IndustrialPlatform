using IndustrialPlatform.SharedKernel.Entities;
using IndustrialPlatform.SharedKernel.Exceptions;

namespace IndustrialPlatform.Identity.Domain.Sso;

/// <summary>
/// 平台 SSO Client(§26.7):为未来独立看板/追溯应用登记。业务标识创建后不可变;
/// 端点集合通过 <see cref="IdentitySsoClientEndpoint"/> 维护,Redirect URI 精确匹配。
/// </summary>
public sealed class IdentitySsoClient : AggregateRoot
{
    /// <summary>名称最大长度。</summary>
    public const int NameMaxLength = 64;

    /// <summary>OAuth ClientId 最大长度。</summary>
    public const int OAuthClientIdMaxLength = 128;

    /// <summary>租户编码(不透明字符串,不做 NId 规范化)。</summary>
    public string TenantNId { get; private set; }

    /// <summary>业务标识,创建后不可变。</summary>
    public string NId { get; private set; }

    /// <summary>规范化业务标识(大写),创建后不可变。</summary>
    public string NormalizedNId { get; private set; }

    /// <summary>显示名称。</summary>
    public string Name { get; private set; }

    /// <summary>OAuth ClientId(用于授权请求的 client_id 参数)。</summary>
    public string OAuthClientId { get; private set; }

    /// <summary>是否启用。</summary>
    public bool Enabled { get; private set; }

    private readonly List<IdentitySsoClientEndpoint> _endpoints = [];

    /// <summary>已登记端点(含软删除)。</summary>
    public IReadOnlyCollection<IdentitySsoClientEndpoint> Endpoints => _endpoints;

    /// <summary>ORM 反序列化专用构造。</summary>
    private IdentitySsoClient()
    {
        TenantNId = string.Empty;
        NId = string.Empty;
        NormalizedNId = string.Empty;
        Name = string.Empty;
        OAuthClientId = string.Empty;
    }

    private IdentitySsoClient(string tenantNId, string nId, string name, string oauthClientId)
        : this()
    {
        TenantNId = SsoGuard.RequireTrimmedNonEmpty(tenantNId, "租户编码不能为空。");
        var nIdValue = Identities.NId.Create(nId);
        NId = nIdValue.Value;
        NormalizedNId = nIdValue.Normalized;
        Name = SsoGuard.RequireTrimmedNonEmpty(name, "Client 名称不能为空。", NameMaxLength, $"Client 名称长度不能超过 {NameMaxLength} 个字符。");
        OAuthClientId = SsoGuard.RequireTrimmedNonEmpty(oauthClientId, "OAuth ClientId 不能为空。", OAuthClientIdMaxLength, $"OAuth ClientId 长度不能超过 {OAuthClientIdMaxLength} 个字符。");
        Enabled = true;
    }

    /// <summary>
    /// 持久化层重建专用构造:恢复全部业务字段、端点集合与生命周期状态,不重新校验。
    /// </summary>
    internal IdentitySsoClient(
        Guid id,
        string tenantNId,
        string nId,
        string normalizedNId,
        string name,
        string oauthClientId,
        bool enabled,
        IReadOnlyCollection<IdentitySsoClientEndpoint> endpoints,
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
        OAuthClientId = oauthClientId;
        Enabled = enabled;
        _endpoints.AddRange(endpoints);
        IsFrozen = isFrozen;
        IsLocked = isLocked;
        IsDeleted = isDeleted;
        EntityType = entityType;
        CreatedOn = createdOn;
        LastUpdatedOn = lastUpdatedOn;
        OptimisticVersion = optimisticVersion;
        ConcurrencyVersion = concurrencyVersion;
    }

    /// <summary>创建平台 SSO Client,默认启用。</summary>
    public static IdentitySsoClient Create(string tenantNId, string nId, string name, string oauthClientId)
        => new IdentitySsoClient(tenantNId, nId, name, oauthClientId);

    /// <summary>变更名称与 OAuth ClientId(业务标识不变)。</summary>
    public void ChangeProfile(string name, string oauthClientId)
    {
        EnsureCanModify();

        Name = SsoGuard.RequireTrimmedNonEmpty(name, "Client 名称不能为空。", NameMaxLength, $"Client 名称长度不能超过 {NameMaxLength} 个字符。");
        OAuthClientId = SsoGuard.RequireTrimmedNonEmpty(oauthClientId, "OAuth ClientId 不能为空。", OAuthClientIdMaxLength, $"OAuth ClientId 长度不能超过 {OAuthClientIdMaxLength} 个字符。");
        Touch();
    }

    /// <summary>启用/禁用 Client。</summary>
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

    /// <summary>登记端点(工厂重载):由聚合创建子实体,冲突校验同下。</summary>
    public IdentitySsoClientEndpoint AddEndpoint(string endpointNId, SsoClientEndpointType endpointType, string uri)
    {
        var endpoint = new IdentitySsoClientEndpoint(Id, IsDeleted, endpointNId, endpointType, uri);
        AddEndpoint(endpoint);
        return endpoint;
    }

    /// <summary>登记端点:类型+NId 或类型+规范化 URI 冲突时抛出业务异常。</summary>
    public void AddEndpoint(IdentitySsoClientEndpoint endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        EnsureCanModify();

        if (_endpoints.Any(e => !e.IsDeleted && e.EndpointType == endpoint.EndpointType && e.NId == endpoint.NId))
        {
            throw new BusinessException("同一 Client 下不允许重复的端点业务标识。");
        }

        if (_endpoints.Any(e => !e.IsDeleted && e.EndpointType == endpoint.EndpointType && e.NormalizedUri == endpoint.NormalizedUri))
        {
            throw new BusinessException("同一 Client 下不允许重复的端点地址。");
        }

        _endpoints.Add(endpoint);
        Touch();
    }

    /// <summary>移除端点(软删除);找不到时幂等返回。</summary>
    public void RemoveEndpoint(string endpointNId)
    {
        EnsureCanModify();

        var endpoint = _endpoints.FirstOrDefault(e => !e.IsDeleted && e.NId == endpointNId);
        if (endpoint is null)
        {
            return;
        }

        endpoint.MarkDeleted();
        Touch();
    }

    /// <summary>启用/禁用端点。</summary>
    public void SetEndpointEnabled(string endpointNId, bool enabled)
    {
        EnsureCanModify();

        var endpoint = _endpoints.FirstOrDefault(e => !e.IsDeleted && e.NId == endpointNId);
        if (endpoint is null)
        {
            return;
        }

        endpoint.SetEnabled(enabled);
        Touch();
    }

    /// <summary>精确匹配某类型的已启用端点 URI。</summary>
    public bool HasEndpoint(SsoClientEndpointType endpointType, string uri)
    {
        return _endpoints.Any(e =>
            !e.IsDeleted && e.Enabled && e.EndpointType == endpointType && e.Uri == uri);
    }
}
