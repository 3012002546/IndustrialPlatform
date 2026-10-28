namespace IndustrialPlatform.PlatformStudio.Domain;

/// <summary>授予指定看板及参数的匿名展示能力；可固定修订或跟随发布，不保存明文密钥。</summary>
public sealed class StudioScreenLink
{
    public Guid Id { get; set; }
    public string TenantNId { get; set; } = "";
    public string ResourceNId { get; set; } = "";
    public string RevisionNId { get; set; } = "";
    public string CreatedBy { get; set; } = "";
    public string IssuerKey { get; set; } = "";
    public string AuthVersion { get; set; } = "";
    public string ParametersJson { get; set; } = "{}";
    public string TokenHash { get; set; } = "";
    public DateTimeOffset CreatedOn { get; set; }
    public DateTimeOffset ExpiresOn { get; set; }
    // NeverExpires=true时忽略ExpiresOn；保留原列兼容既有有限期链接和迁移。
    public bool NeverExpires { get; set; }
    public bool FollowPublished { get; set; }
    public DateTimeOffset? RevokedOn { get; set; }
}
