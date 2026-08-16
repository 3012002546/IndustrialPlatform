using IndustrialPlatform.SystemData.Contracts.DatabaseOrchestration;
using IndustrialPlatform.SharedKernel.Topology;
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

    // ===== v2(TASK-SD-004:按 (ServiceKey, ModuleKey) 粒度,纳入种子声明)=====

    /// <summary>v2 注册清单规范化哈希(含 ModuleKey 与全部种子声明;重注册幂等与 drift 判定依据)。</summary>
    public static string HashManifestV2(ServiceInitializationManifestV2 manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        var canonical = string.Join("|",
        [
            manifest.ServiceKey ?? string.Empty,
            manifest.ModuleKey ?? string.Empty,
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
            HashSeedSets(manifest.SeedSets),
        ]);

        return Sha256Hex(canonical);
    }

    /// <summary>v2 计划请求规范化哈希(模块级)。</summary>
    public static string HashPlanRequestV2(string serviceKey, string moduleKey, string requestedVersion, string? desiredState)
    {
        var canonical = string.Join("|",
        [
            serviceKey,
            moduleKey,
            requestedVersion,
            desiredState ?? string.Empty,
        ]);

        return Sha256Hex(canonical);
    }

    /// <summary>v2 apply 请求规范化哈希(模块级;幂等哈希输入须与计划一致)。</summary>
    public static string HashApplyRequestV2(string planNId, string moduleKey, string? requestedVersion)
    {
        var canonical = string.Join("|",
        [
            planNId,
            moduleKey,
            requestedVersion ?? string.Empty,
        ]);

        return Sha256Hex(canonical);
    }

    private static string HashSeedSets(IReadOnlyCollection<SeedSetV1>? seedSets)
    {
        if (seedSets is null || seedSets.Count == 0)
        {
            return string.Empty;
        }

        return string.Join(";", seedSets
            .OrderBy(seed => seed.SeedKey, StringComparer.Ordinal)
            .Select(seed => string.Join("|",
            [
                seed.SeedKey ?? string.Empty,
                seed.SeedVersion ?? string.Empty,
                seed.SeedClass ?? string.Empty,
                seed.Scope ?? string.Empty,
                seed.SeedArtifactId ?? string.Empty,
                seed.SeedChecksum ?? string.Empty,
                seed.SeedSignature ?? string.Empty,
                seed.RequiredForReadiness?.ToString() ?? string.Empty,
                seed.AllowedEnvironments ?? string.Empty,
                seed.DependsOnMigrationVersion ?? string.Empty,
                seed.DependsOnSeedKeys ?? string.Empty,
                seed.BootstrapPolicy ?? string.Empty,
            ])));
    }
}
