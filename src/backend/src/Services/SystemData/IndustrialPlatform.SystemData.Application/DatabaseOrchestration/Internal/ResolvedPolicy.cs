namespace IndustrialPlatform.SystemData.Application.DatabaseOrchestration.Internal;

/// <summary>已解析的环境策略(显式记录或按环境回退默认),供 plan/apply 门禁与超时计算使用。</summary>
internal sealed record ResolvedPolicy(
    bool ApprovalRequired,
    bool BackupRequired,
    int PlanTtlSeconds,
    int PlanTimeoutSeconds,
    int ApplyTimeoutSeconds,
    int MaxPreMigrationRetries);
