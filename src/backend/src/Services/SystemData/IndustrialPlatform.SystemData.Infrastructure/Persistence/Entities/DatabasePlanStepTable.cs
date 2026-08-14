using IndustrialPlatform.SystemData.Domain.DatabaseOrchestration;
using SqlSugar;

namespace IndustrialPlatform.SystemData.Infrastructure.Persistence.Entities;

/// <summary>
/// system_data_database_plan_step 表的持久化模型(9 公共列 + 复合外键影子列 + 业务列,snake_case)。
/// plan 的不可变步骤子表;PlanIsDeleted 由数据库复合外键 ON UPDATE CASCADE 同步。
/// </summary>
[SugarTable("system_data_database_plan_step")]
public sealed class DatabasePlanStepTable
{
    [SugarColumn(ColumnName = "id", IsPrimaryKey = true)]
    public Guid Id { get; set; }

    [SugarColumn(ColumnName = "is_frozen")]
    public bool IsFrozen { get; set; }

    [SugarColumn(ColumnName = "is_locked")]
    public bool IsLocked { get; set; }

    [SugarColumn(ColumnName = "is_deleted")]
    public bool IsDeleted { get; set; }

    [SugarColumn(ColumnName = "entity_type")]
    public string EntityType { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "created_on")]
    public DateTimeOffset CreatedOn { get; set; }

    [SugarColumn(ColumnName = "last_updated_on")]
    public DateTimeOffset LastUpdatedOn { get; set; }

    [SugarColumn(ColumnName = "optimistic_version")]
    public long OptimisticVersion { get; set; }

    [SugarColumn(ColumnName = "concurrency_version")]
    public Guid ConcurrencyVersion { get; set; }

    [SugarColumn(ColumnName = "plan_id")]
    public Guid PlanId { get; set; }

    [SugarColumn(ColumnName = "plan_is_deleted")]
    public bool PlanIsDeleted { get; set; }

    [SugarColumn(ColumnName = "sequence")]
    public int Sequence { get; set; }

    [SugarColumn(ColumnName = "step_kind")]
    public string StepKind { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "input_summary")]
    public string? InputSummary { get; set; }

    [SugarColumn(ColumnName = "precondition_summary")]
    public string? PreconditionSummary { get; set; }

    [SugarColumn(ColumnName = "postcondition_summary")]
    public string? PostconditionSummary { get; set; }

    [SugarColumn(ColumnName = "risk_level")]
    public RiskLevel RiskLevel { get; set; }
}
