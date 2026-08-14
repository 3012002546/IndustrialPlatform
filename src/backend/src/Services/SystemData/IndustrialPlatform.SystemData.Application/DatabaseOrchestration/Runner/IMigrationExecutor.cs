using IndustrialPlatform.SystemData.Domain.Topology;

namespace IndustrialPlatform.SystemData.Application.DatabaseOrchestration.Runner;

/// <summary>
/// 目标迁移执行端口(05 方案 §7.1.4 Migrate 阶段)。以 Operation 为恢复边界:
/// 逐步骤事务提交并在目标库记账,未记账步骤幂等重放;迁移失败不盲重试(由 Runner 裁决)。
/// </summary>
public interface IMigrationExecutor
{
    /// <summary>在目标库应用迁移产物(仅执行未记账步骤),返回应用步骤数与结果版本。</summary>
    /// <param name="moduleKey">模块标识(账本表名 {moduleKey}_schema_migrations 的派生依据)。</param>
    Task<MigrationExecutionResult> ApplyAsync(
        ResolvedDatabaseTarget target,
        DatabaseMigrationArtifact artifact,
        TargetDatabaseConnection migrator,
        string moduleKey,
        CancellationToken cancellationToken);
}
