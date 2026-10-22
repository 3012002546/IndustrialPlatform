namespace IndustrialPlatform.PlatformStudio.Domain;

/// <summary>仅服务器可配置的身份端点；所有地址、客户端及双向TLS材料由运维登记。</summary>
public sealed class StudioAuthority
{
    public string Issuer { get; set; } = "";
    public string ClientId { get; set; } = "";
    public string AuthorizationEndpoint { get; set; } = "";
    public string ExchangeEndpoint { get; set; } = "";
    public string ContextEndpoint { get; set; } = "";
    public string? MetadataAddress { get; set; }
    public string? ClientSecretRef { get; set; }
    public string ClientCertificatePathRef { get; set; } = "";
    public string? ClientCertificatePasswordRef { get; set; }
    public string[] AllowedSigningAlgorithms { get; set; } = ["RS256"];
}
