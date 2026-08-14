using IndustrialPlatform.SystemData.Application.DatabaseOrchestration.Options;
using IndustrialPlatform.SystemData.Domain.DatabaseOrchestration;
using IndustrialPlatform.SystemData.Domain.Topology;

namespace IndustrialPlatform.SystemData.Application.DatabaseOrchestration.Internal;

/// <summary>
/// 环境策略解析(plan/apply 入队共用):显式记录优先,否则按受信任拓扑环境回退默认——
/// Development/Test 自动、Staging 无硬门禁、Production 审批+备份强制(05 方案 §7.1.2)。
/// 门禁与超时一律经由此处,保证 plan 与 apply 观察同一策略。
/// </summary>
internal static class EnvironmentPolicyResolver
{
    public static async Task<ResolvedPolicy> ResolveAsync(
        IDatabaseOrchestrationStore store,
        DatabaseOrchestrationOptions options,
        string tenantNId,
        string environmentNId,
        DatabaseTopology topology,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(topology);

        var stored = await store.GetEnvironmentPolicyAsync(tenantNId, environmentNId, cancellationToken);
        if (stored is not null)
        {
            return new ResolvedPolicy(
                stored.ApprovalRequired,
                stored.BackupRequired,
                stored.PlanTtlSeconds,
                stored.PlanTimeoutSeconds,
                stored.ApplyTimeoutSeconds,
                stored.MaxPreMigrationRetries);
        }

        var production = string.Equals(topology.EnvironmentName, "Production", StringComparison.Ordinal);
        return new ResolvedPolicy(
            production,
            production,
            options.PlanTtlSeconds,
            options.PlanTimeoutSeconds,
            options.ApplyTimeoutSeconds,
            options.MaxPreMigrationRetries);
    }
}
