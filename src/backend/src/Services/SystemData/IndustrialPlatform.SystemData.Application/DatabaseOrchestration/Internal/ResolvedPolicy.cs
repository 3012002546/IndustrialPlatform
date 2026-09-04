using IndustrialPlatform.Application.Abstractions.Initialization;
using IndustrialPlatform.SystemData.Domain.DatabaseOrchestration;

namespace IndustrialPlatform.SystemData.Application.DatabaseOrchestration.Internal;

/// <summary>
/// 已解析的环境策略(显式记录或按环境回退默认),供 plan/apply 门禁、超时计算与
/// 种子环境门禁使用。TASK-SD-004:<see cref="EnvironmentKind"/> 用于 EnvironmentSample
/// 拒绝与 SecretBootstrap 允许判断。
/// </summary>
internal sealed record ResolvedPolicy(
    bool ApprovalRequired,
    bool BackupRequired,
    int PlanTtlSeconds,
    int PlanTimeoutSeconds,
    int ApplyTimeoutSeconds,
    int MaxPreMigrationRetries,
    DatabaseEnvironmentKind EnvironmentKind)
{
    /// <summary>生产环境始终使用 Advanced;其他环境只要存在人工门禁也使用 Advanced。</summary>
    public ServiceInitializationPolicy InitializationPolicy =>
        EnvironmentKind == DatabaseEnvironmentKind.Production || ApprovalRequired || BackupRequired
            ? ServiceInitializationPolicy.Advanced
            : ServiceInitializationPolicy.Standard;

    /// <summary>显式策略是否存在;用于管理端说明默认值还是租户覆盖。</summary>
    public bool IsExplicit { get; init; }

    /// <summary>持久化策略修订号,默认策略为 0。</summary>
    public int PolicyRevision { get; init; }
}
