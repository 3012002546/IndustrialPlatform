using SqlSugar;

namespace IndustrialPlatform.SystemData.Infrastructure.Persistence.Migrations;

/// <summary>
/// 迁移账本记录,持久化已应用迁移标识。
/// 框架内部表,不属于业务表清单,不纳入 Entity 生命周期字段定义。
/// </summary>
[SugarTable("system_data_schema_migrations")]
public sealed class SchemaMigrationRecord
{
    /// <summary>
    /// 已应用迁移标识。
    /// </summary>
    [SugarColumn(IsPrimaryKey = true, ColumnName = "migration_id")]
    public string MigrationId { get; set; } = string.Empty;

    /// <summary>
    /// 迁移说明。
    /// </summary>
    [SugarColumn(ColumnName = "description")]
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// 迁移应用时间(UTC)。
    /// </summary>
    [SugarColumn(ColumnName = "applied_on")]
    public DateTimeOffset AppliedOn { get; set; }
}
