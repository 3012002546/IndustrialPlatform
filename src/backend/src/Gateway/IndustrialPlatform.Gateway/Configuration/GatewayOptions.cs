namespace IndustrialPlatform.Gateway.Configuration;

/// <summary>
/// Gateway 开发期配置模型,绑定 "Gateway" 配置节。
/// </summary>
public sealed class GatewayOptions
{
    /// <summary>配置节名称。</summary>
    public const string SectionName = "Gateway";

    /// <summary>开发期 CORS 策略名称。</summary>
    public const string CorsPolicyName = "GatewayCors";

    /// <summary>下游服务路由列表。</summary>
    public List<GatewayServiceOptions> Services { get; set; } = [];

    /// <summary>开发期 CORS 配置。</summary>
    public GatewayCorsOptions Cors { get; set; } = new();

    /// <summary>下游请求活动超时(秒),超时返回 504。</summary>
    public int RequestTimeoutSeconds { get; set; } = 10;
}

/// <summary>
/// 单个下游服务的路由配置。
/// </summary>
public sealed class GatewayServiceOptions
{
    /// <summary>服务名,用作路由与集群标识。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>统一入口路径前缀,转发时剥离,如 /identity。</summary>
    public string PathPrefix { get; set; } = string.Empty;

    /// <summary>下游服务基地址,如 http://localhost:5041。</summary>
    public string DestinationUrl { get; set; } = string.Empty;
}

/// <summary>
/// 开发期 CORS 允许来源。
/// </summary>
public sealed class GatewayCorsOptions
{
    /// <summary>允许的来源列表;为空时允许任意来源。</summary>
    public List<string> AllowedOrigins { get; set; } = [];
}
