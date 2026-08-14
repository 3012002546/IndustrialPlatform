using IndustrialPlatform.SystemData.Domain.DatabaseOrchestration;
using SqlSugar;

namespace IndustrialPlatform.SystemData.Infrastructure.Persistence.Entities;

/// <summary>
/// system_data_database_operation_step 表的持久化模型(9 公共列 + 复合外键影子列 + 业务列,snake_case)。
/// operation 的阶段步骤子表;OperationIsDeleted 由数据库复合外键 ON UPDATE CASCADE 同步。
/// </summary>
[SugarTable("system_data_database_operation_step")]
public sealed class DatabaseOperationStepTable
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

    [SugarColumn(ColumnName = "operation_id")]
    public Guid OperationId { get; set; }

    [SugarColumn(ColumnName = "operation_is_deleted")]
    public bool OperationIsDeleted { get; set; }

    [SugarColumn(ColumnName = "sequence")]
    public int Sequence { get; set; }

    [SugarColumn(ColumnName = "phase")]
    public OperationPhase Phase { get; set; }

    [SugarColumn(ColumnName = "attempt")]
    public int Attempt { get; set; }

    [SugarColumn(ColumnName = "status")]
    public OperationStepStatus Status { get; set; }

    [SugarColumn(ColumnName = "started_on")]
    public DateTimeOffset? StartedOn { get; set; }

    [SugarColumn(ColumnName = "completed_on")]
    public DateTimeOffset? CompletedOn { get; set; }

    [SugarColumn(ColumnName = "sanitized_error_code")]
    public string? SanitizedErrorCode { get; set; }

    [SugarColumn(ColumnName = "sanitized_error_summary")]
    public string? SanitizedErrorSummary { get; set; }
}
