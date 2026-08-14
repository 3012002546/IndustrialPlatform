using IndustrialPlatform.SharedKernel.Entities;

namespace IndustrialPlatform.SystemData.Domain.DatabaseOrchestration;

/// <summary>
/// 环境级数据库编排策略聚合根(05 方案 §7.1.2、§8.1 <c>system_data_database_environment_policy</c>)。
/// 决定该环境是否要求审批/备份、计划与 apply 超时、预迁移重试次数;按 (TenantNId, EnvironmentNId) 唯一。
/// </summary>
public sealed class DatabaseEnvironmentPolicy : AggregateRoot
{
    /// <summary>租户业务标识。</summary>
    public string TenantNId { get; private set; }

    /// <summary>环境业务标识。</summary>
    public string EnvironmentNId { get; private set; }

    /// <summary>受信任环境种类,驱动默认门禁。</summary>
    public DatabaseEnvironmentKind EnvironmentKind { get; private set; }

    /// <summary>是否要求人工审批后才允许 apply。</summary>
    public bool ApprovalRequired { get; private set; }

    /// <summary>是否要求备份证据后才允许 apply。</summary>
    public bool BackupRequired { get; private set; }

    /// <summary>计划默认有效期(秒)。</summary>
    public int PlanTtlSeconds { get; private set; }

    /// <summary>plan 类操作超时(秒)。</summary>
    public int PlanTimeoutSeconds { get; private set; }

    /// <summary>apply 类操作超时(秒)。</summary>
    public int ApplyTimeoutSeconds { get; private set; }

    /// <summary>预迁移阶段最大重试次数。</summary>
    public int MaxPreMigrationRetries { get; private set; }

    /// <summary>策略版本号,每次更新递增。</summary>
    public int PolicyRevision { get; private set; }

    private DatabaseEnvironmentPolicy()
    {
        TenantNId = string.Empty;
        EnvironmentNId = string.Empty;
        EnvironmentKind = DatabaseEnvironmentKind.Development;
    }

    private DatabaseEnvironmentPolicy(
        string tenantNId,
        string environmentNId,
        DatabaseEnvironmentKind environmentKind,
        bool approvalRequired,
        bool backupRequired,
        int planTtlSeconds,
        int planTimeoutSeconds,
        int applyTimeoutSeconds,
        int maxPreMigrationRetries)
    {
        TenantNId = DatabaseOrchestrationGuard.RequireNId(tenantNId, "环境策略的租户标识不能为空。");
        EnvironmentNId = DatabaseOrchestrationGuard.RequireNId(environmentNId, "环境策略的环境标识不能为空。");
        EnvironmentKind = environmentKind;
        ApprovalRequired = approvalRequired;
        BackupRequired = backupRequired;
        PlanTtlSeconds = DatabaseOrchestrationGuard.RequirePositive(planTtlSeconds, "计划 TTL 必须为正整数秒。");
        PlanTimeoutSeconds = DatabaseOrchestrationGuard.RequirePositive(planTimeoutSeconds, "计划超时必须为正整数秒。");
        ApplyTimeoutSeconds = DatabaseOrchestrationGuard.RequirePositive(applyTimeoutSeconds, "apply 超时必须为正整数秒。");
        MaxPreMigrationRetries = DatabaseOrchestrationGuard.RequireNonNegative(maxPreMigrationRetries, "预迁移重试次数不能为负。");
        PolicyRevision = 1;
    }

    /// <summary>持久化层重建专用构造,不重新校验。</summary>
    internal DatabaseEnvironmentPolicy(
        Guid id,
        string tenantNId,
        string environmentNId,
        DatabaseEnvironmentKind environmentKind,
        bool approvalRequired,
        bool backupRequired,
        int planTtlSeconds,
        int planTimeoutSeconds,
        int applyTimeoutSeconds,
        int maxPreMigrationRetries,
        int policyRevision,
        bool isFrozen,
        bool isLocked,
        bool isDeleted,
        string entityType,
        DateTimeOffset createdOn,
        DateTimeOffset lastUpdatedOn,
        long optimisticVersion,
        Guid concurrencyVersion)
        : base()
    {
        Id = id;
        TenantNId = tenantNId;
        EnvironmentNId = environmentNId;
        EnvironmentKind = environmentKind;
        ApprovalRequired = approvalRequired;
        BackupRequired = backupRequired;
        PlanTtlSeconds = planTtlSeconds;
        PlanTimeoutSeconds = planTimeoutSeconds;
        ApplyTimeoutSeconds = applyTimeoutSeconds;
        MaxPreMigrationRetries = maxPreMigrationRetries;
        PolicyRevision = policyRevision;
        IsFrozen = isFrozen;
        IsLocked = isLocked;
        IsDeleted = isDeleted;
        EntityType = entityType;
        CreatedOn = createdOn;
        LastUpdatedOn = lastUpdatedOn;
        OptimisticVersion = optimisticVersion;
        ConcurrencyVersion = concurrencyVersion;
    }

    /// <summary>创建环境策略(默认 PolicyRevision = 1)。</summary>
    public static DatabaseEnvironmentPolicy Create(
        string tenantNId,
        string environmentNId,
        DatabaseEnvironmentKind environmentKind,
        bool approvalRequired,
        bool backupRequired,
        int planTtlSeconds,
        int planTimeoutSeconds,
        int applyTimeoutSeconds,
        int maxPreMigrationRetries)
        => new(
            tenantNId,
            environmentNId,
            environmentKind,
            approvalRequired,
            backupRequired,
            planTtlSeconds,
            planTimeoutSeconds,
            applyTimeoutSeconds,
            maxPreMigrationRetries);

    /// <summary>更新策略参数并递增 <see cref="PolicyRevision"/>。</summary>
    public void UpdatePolicy(
        DatabaseEnvironmentKind environmentKind,
        bool approvalRequired,
        bool backupRequired,
        int planTtlSeconds,
        int planTimeoutSeconds,
        int applyTimeoutSeconds,
        int maxPreMigrationRetries)
    {
        EnsureCanModify();
        EnvironmentKind = environmentKind;
        ApprovalRequired = approvalRequired;
        BackupRequired = backupRequired;
        PlanTtlSeconds = DatabaseOrchestrationGuard.RequirePositive(planTtlSeconds, "计划 TTL 必须为正整数秒。");
        PlanTimeoutSeconds = DatabaseOrchestrationGuard.RequirePositive(planTimeoutSeconds, "计划超时必须为正整数秒。");
        ApplyTimeoutSeconds = DatabaseOrchestrationGuard.RequirePositive(applyTimeoutSeconds, "apply 超时必须为正整数秒。");
        MaxPreMigrationRetries = DatabaseOrchestrationGuard.RequireNonNegative(maxPreMigrationRetries, "预迁移重试次数不能为负。");
        PolicyRevision++;
        Touch();
    }
}
