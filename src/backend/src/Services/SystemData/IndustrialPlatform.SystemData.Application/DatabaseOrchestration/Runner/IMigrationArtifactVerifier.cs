namespace IndustrialPlatform.SystemData.Application.DatabaseOrchestration.Runner;

/// <summary>
/// 迁移产物校验端口(05 方案 §7.1.5)。执行前验证 checksum(SHA-256)与签名引用;
/// 校验失败返回 <c>false</c> 由 Runner 映射为 <see cref="DatabaseOrchestrationRunnerErrors.ArtifactInvalid"/>。
/// </summary>
public interface IMigrationArtifactVerifier
{
    /// <summary>校验产物 checksum 与可选签名引用是否与注册清单一致。</summary>
    Task<bool> VerifyAsync(
        DatabaseMigrationArtifact artifact,
        string expectedChecksum,
        string? expectedSignatureRef,
        CancellationToken cancellationToken);
}
