using IndustrialPlatform.SystemData.Contracts.DatabaseOrchestration;
using IndustrialPlatform.SystemData.Domain.Topology;

namespace IndustrialPlatform.SystemData.Application.DatabaseOrchestration.Internal;

/// <summary>
/// 请求哈希工具:将规范化请求内容计算为 SHA-256 十六进制,与幂等键组合判定重放。
/// 算法稳定可复现,供入队(plan/apply)与幂等重放共用;SD-003 Runner 复用同一算法。
/// </summary>
public static class RequestHasher
{
    /// <summary>计算规范化文本的 SHA-256 十六进制摘要。</summary>
    public static string Sha256Hex(string canonical) => DatabaseTopologyFingerprint.Sha256Hex(canonical);

    /// <summary>注册清单规范化哈希(重注册幂等依据 ManifestChecksum)。</summary>
    public static string HashManifest(DatabaseRegistrationManifestV1 manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        var canonical = string.Join("|",
        [
            manifest.ServiceKey ?? string.Empty,
            manifest.LogicalDatabaseName ?? string.Empty,
            manifest.Provider ?? string.Empty,
            manifest.TopologyMode ?? string.Empty,
            manifest.MigrationArtifactId ?? string.Empty,
            manifest.RequestedVersion ?? string.Empty,
            manifest.ArtifactChecksum ?? string.Empty,
            manifest.ArtifactSignature ?? string.Empty,
            manifest.DesiredState ?? string.Empty,
            manifest.AutoProvision?.ToString() ?? string.Empty,
            manifest.AutoMigrate?.ToString() ?? string.Empty,
            manifest.OwnerNId ?? string.Empty,
            manifest.ManifestVersion ?? string.Empty,
        ]);

        return Sha256Hex(canonical);
    }

    /// <summary>计划请求规范化哈希。</summary>
    public static string HashPlanRequest(string serviceKey, string requestedVersion, string? desiredState)
    {
        var canonical = string.Join("|",
        [
            serviceKey,
            requestedVersion,
            desiredState ?? string.Empty,
        ]);

        return Sha256Hex(canonical);
    }

    /// <summary>apply 请求规范化哈希。</summary>
    public static string HashApplyRequest(string planNId, string? requestedVersion)
    {
        var canonical = string.Join("|",
        [
            planNId,
            requestedVersion ?? string.Empty,
        ]);

        return Sha256Hex(canonical);
    }
}
