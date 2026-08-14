namespace IndustrialPlatform.SystemData.Application.DatabaseOrchestration.Runner;

/// <summary>
/// 迁移产物获取端口(05 方案 §7.1.5)。从可信 Artifact Registry 按不可变标识获取产物。
/// 执行前必须经 <see cref="IMigrationArtifactVerifier"/> 校验 checksum/签名。
/// </summary>
public interface IMigrationArtifactStore
{
    /// <summary>按不可变标识解析迁移产物;不存在或不可读抛 <see cref="DatabaseOrchestrationException"/>。</summary>
    Task<DatabaseMigrationArtifact> ResolveAsync(string artifactId, CancellationToken cancellationToken);
}
