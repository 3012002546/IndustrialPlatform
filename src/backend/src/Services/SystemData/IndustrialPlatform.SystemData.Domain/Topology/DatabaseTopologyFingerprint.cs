using System.Security.Cryptography;
using System.Text;

namespace IndustrialPlatform.SystemData.Domain.Topology;

/// <summary>
/// 数据库编排指纹/校验和计算(SHA-256 十六进制)。所有算法保持稳定可复现,
/// 供注册(拓扑 revision)、apply 门禁(目标状态指纹/drift)、计划(PlanChecksum)
/// 与迁移观察(readiness 身份指纹)统一使用;SD-003 Runner 复用同一算法。
/// 指纹只含公开身份与版本字段,不含连接串、Secret 值或任何凭据。
/// </summary>
public static class DatabaseTopologyFingerprint
{
    /// <summary>由可信拓扑的有效字段计算 revision(受信任环境配置变化时变化)。</summary>
    public static string ComputeTopologyRevision(DatabaseTopology topology)
    {
        ArgumentNullException.ThrowIfNull(topology);

        var mapping = string.Join(
            ",",
            topology.ServiceDatabases
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => $"{pair.Key}={pair.Value}"));
        var canonical = string.Join("|",
        [
            topology.EnvironmentName,
            topology.Mode.ToString(),
            topology.SharedDatabaseName ?? string.Empty,
            topology.SharedSqliteFile ?? string.Empty,
            mapping,
        ]);

        return Sha256Hex(canonical);
    }

    /// <summary>
    /// 目标状态指纹:绑定目标身份、拓扑模式/revision、迁移产物与请求版本与期望状态。
    /// apply 前重新计算并对比,任一输入变化均视为 drift(05 方案 §7.1.3)。
    /// </summary>
    public static string ComputeTargetStateFingerprint(
        string environmentNId,
        string serviceKey,
        string provider,
        string logicalDatabaseName,
        string physicalDatabaseName,
        string topologyMode,
        string topologyRevision,
        string artifactChecksum,
        string requestedVersion,
        string desiredState,
        bool approvalRequired,
        bool backupRequired)
    {
        var canonical = string.Join("|",
        [
            environmentNId,
            serviceKey,
            provider,
            logicalDatabaseName,
            physicalDatabaseName,
            topologyMode,
            topologyRevision,
            artifactChecksum,
            requestedVersion,
            desiredState,
            approvalRequired ? "1" : "0",
            backupRequired ? "1" : "0",
        ]);

        return Sha256Hex(canonical);
    }

    /// <summary>数据库身份指纹:绑定环境、服务、逻辑/物理身份与拓扑 revision,用于迁移观察与 readiness。</summary>
    public static string ComputeDatabaseIdentityFingerprint(
        string environmentNId,
        string serviceKey,
        string provider,
        string logicalDatabaseName,
        string physicalDatabaseName,
        string topologyRevision)
    {
        var canonical = string.Join("|",
        [
            environmentNId,
            serviceKey,
            provider,
            logicalDatabaseName,
            physicalDatabaseName,
            topologyRevision,
        ]);

        return Sha256Hex(canonical);
    }

    /// <summary>计划校验和:覆盖计划身份、目标指纹、风险与全部步骤内容;成功后不可变(§8.1 checksum 唯一)。</summary>
    public static string ComputePlanChecksum(
        string planNId,
        string tenantNId,
        string environmentNId,
        string serviceKey,
        string requestedVersion,
        string currentVersion,
        string targetStateFingerprint,
        string riskLevel,
        bool destructiveChangeDetected,
        string requiredPolicies,
        IReadOnlyCollection<string> stepCanonicals)
    {
        var steps = string.Join(";", stepCanonicals ?? []);
        var canonical = string.Join("|",
        [
            planNId,
            tenantNId,
            environmentNId,
            serviceKey,
            requestedVersion,
            currentVersion,
            targetStateFingerprint,
            riskLevel,
            destructiveChangeDetected ? "1" : "0",
            requiredPolicies,
            steps,
        ]);

        return Sha256Hex(canonical);
    }

    /// <summary>计算规范化文本的 SHA-256 十六进制摘要(小写)。</summary>
    public static string Sha256Hex(string canonical)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(canonical);

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        var builder = new StringBuilder(bytes.Length * 2);
        foreach (var b in bytes)
        {
            builder.Append(b.ToString("x2", System.Globalization.CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }
}
