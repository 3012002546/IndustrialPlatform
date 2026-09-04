using IndustrialPlatform.SystemData.Application.DatabaseOrchestration.Options;
using IndustrialPlatform.SystemData.Domain.DatabaseOrchestration;
using IndustrialPlatform.SharedKernel.Topology;

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

        var environmentKind = ParseEnvironmentKind(topology.EnvironmentName);
        var stored = await store.GetEnvironmentPolicyAsync(tenantNId, environmentNId, cancellationToken);
        if (stored is not null)
        {
            // Production 的安全门禁由可信拓扑强制,不能被控制面中一条较弱的旧策略关闭。
            var storedProduction = environmentKind == DatabaseEnvironmentKind.Production;
            return new ResolvedPolicy(
                storedProduction || stored.ApprovalRequired,
                storedProduction || stored.BackupRequired,
                stored.PlanTtlSeconds,
                stored.PlanTimeoutSeconds,
                stored.ApplyTimeoutSeconds,
                stored.MaxPreMigrationRetries,
                environmentKind)
            {
                IsExplicit = true,
                PolicyRevision = stored.PolicyRevision,
            };
        }

        var production = environmentKind == DatabaseEnvironmentKind.Production;
        return new ResolvedPolicy(
            production,
            production,
            options.PlanTtlSeconds,
            options.PlanTimeoutSeconds,
            options.ApplyTimeoutSeconds,
            options.MaxPreMigrationRetries,
            environmentKind)
        {
            IsExplicit = false,
            PolicyRevision = 0,
        };
    }

    /// <summary>由受信任拓扑环境名解析环境种类;未知环境回退 Development(开发基线)。</summary>
    internal static DatabaseEnvironmentKind ParseEnvironmentKind(string environmentName) =>
        Enum.TryParse<DatabaseEnvironmentKind>(environmentName, ignoreCase: true, out var kind)
            ? kind
            : DatabaseEnvironmentKind.Development;
}
