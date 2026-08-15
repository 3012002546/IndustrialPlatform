namespace IndustrialPlatform.Identity.Application.Bootstrap;

/// <summary>
/// bootstrap 配置(§29A.4)。只含非敏感引导策略;绝不包含任何密码、密钥或凭据。
/// </summary>
public sealed class BootstrapOptions
{
    /// <summary>配置节 <c>Identity:Bootstrap</c>。</summary>
    public const string SectionName = "Identity:Bootstrap";

    /// <summary>bootstrap 租户编码(非敏感逻辑标识;密码仍由随机生成器产生)。</summary>
    public string TenantNId { get; set; } = "development";
}
