using IndustrialPlatform.SystemData.Application.DatabaseOrchestration.Runner;
using IndustrialPlatform.SharedKernel.Topology;
using IndustrialPlatform.SystemData.Domain.Topology;

namespace IndustrialPlatform.SystemData.Infrastructure.DatabaseOrchestration.Runner;

/// <summary>
/// 签名迁移/种子产物校验(05 方案 §7.1.5、蓝图 §5.2):重算产物步骤内容规范化 SHA-256 与产物自带校验和比对
/// (防篡改),再与注册清单校验和/签名引用比对。密码学签名验证(私钥验签)留后续接入,本实现先保证
/// checksum 一致与签名引用匹配;校验失败由 Runner 映射 SD_DB_ARTIFACT_INVALID/SD_INIT_SEED_CHECKSUM_DRIFT。
/// </summary>
public sealed class MigrationArtifactChecksumVerifier : IMigrationArtifactVerifier, ISeedArtifactVerifier
{
    /// <inheritdoc />
    public Task<bool> VerifyAsync(
        DatabaseMigrationArtifact artifact,
        string expectedChecksum,
        string? expectedSignatureRef,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(artifact);

        var recomputed = ComputeChecksum(artifact);
        var valid = string.Equals(recomputed, artifact.Checksum, StringComparison.Ordinal)
            && string.Equals(artifact.Checksum, expectedChecksum, StringComparison.Ordinal);
        if (valid && expectedSignatureRef is not null)
        {
            valid = string.Equals(artifact.SignatureRef, expectedSignatureRef, StringComparison.Ordinal);
        }

        return Task.FromResult(valid);
    }

    /// <inheritdoc />
    public Task<bool> VerifyAsync(
        SeedArtifact artifact,
        string expectedChecksum,
        string? expectedSignatureRef,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(artifact);

        var recomputed = ComputeChecksum(artifact);
        var valid = string.Equals(recomputed, artifact.Checksum, StringComparison.Ordinal)
            && string.Equals(artifact.Checksum, expectedChecksum, StringComparison.Ordinal);
        if (valid && expectedSignatureRef is not null)
        {
            valid = string.Equals(artifact.SignatureRef, expectedSignatureRef, StringComparison.Ordinal);
        }

        return Task.FromResult(valid);
    }

    /// <summary>按迁移产物步骤内容(不含凭据)计算规范化 SHA-256 校验和。</summary>
    public static string ComputeChecksum(DatabaseMigrationArtifact artifact)
    {
        var steps = string.Join(";", artifact.Steps
            .OrderBy(step => step.Sequence)
            .Select(step => $"{step.Sequence}|{step.StepId}|{step.Sql}|{step.RollbackSql ?? string.Empty}|{(step.Destructive ? "1" : "0")}"));
        var canonical = $"{artifact.Version}|{artifact.SignatureRef ?? string.Empty}|{steps}";
        return DatabaseTopologyFingerprint.Sha256Hex(canonical);
    }

    /// <summary>按种子产物步骤内容(不含凭据/Secret 值)计算规范化 SHA-256 校验和。</summary>
    public static string ComputeChecksum(SeedArtifact artifact)
    {
        var steps = string.Join(";", artifact.Steps
            .OrderBy(step => step.Sequence)
            .Select(step => $"{step.Sequence}|{step.StepId}|{step.Sql}|{step.RollbackSql ?? string.Empty}"));
        var canonical = $"{artifact.Version}|{artifact.SignatureRef ?? string.Empty}|{steps}";
        return DatabaseTopologyFingerprint.Sha256Hex(canonical);
    }
}
