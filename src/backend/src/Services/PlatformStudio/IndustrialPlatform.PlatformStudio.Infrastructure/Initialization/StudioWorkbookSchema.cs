using IndustrialPlatform.PlatformStudio.Infrastructure.Persistence;
using SqlSugar;

namespace IndustrialPlatform.PlatformStudio.Infrastructure.Initialization;

/// <summary>追加工作副本与手动历史版本迁移；不改写STU-005及已有历史数据。</summary>
internal static class StudioWorkbookSchema
{
    public const string Step = "STU-009-WorkbookWorkingCopy";
    public static readonly StudioSchema.Definition[] Additions =
    [
        new("studio_workbook", "history_sequence:i64,has_working_copy:flag", Step),
        new("studio_workbook_version", "history_version:i64?,pinned_on:time?,version_note:txt1000?", Step),
        new("studio_workbook_session", "base_content_version:i64", Step),
    ];
    public static readonly StudioSchema.Definition Retired = new("studio_workbook_retired_file", "id:id,n_id:key128,tenant_n_id:key128,created_on:time,workbook_id:id,owner_user_n_id:key128,file_n_id:key128,file_reference_n_id:key128", Step);
    private static readonly Dictionary<string, string> Descriptions = new()
    {
        ["history_sequence"] = "显式保存的历史版本序列；日常保存工作副本不递增。",
        ["has_working_copy"] = "当前文件为尚未手动归入历史的工作副本；不代表Office仍有未保存修改。",
        ["history_version"] = "历史版本序号；空值表示工作副本，显式保存版本后固定且不可覆盖。",
        ["pinned_on"] = "用户显式保存历史版本的UTC时间；旧版保留原创建时间。",
        ["version_note"] = "保存历史版本时的说明，最多1000字符。",
        ["base_content_version"] = "会话最近确认的内容修订，用于工作副本原位更新的旧写入拒绝。",
    };
    public static StudioServiceInitializer.Migration Migration(StudioDatabase db)
    {
        var sql = new List<string>();
        foreach (var table in Additions)
        foreach (var column in table.Columns.Split(','))
        {
            var name = column.Split(':')[0];
            sql.Add($"ALTER TABLE {db.Table(table.Name)} ADD {StudioSchema.Column(column, db.Provider)}" + (column.EndsWith('?') ? "" : column.EndsWith(":flag", StringComparison.Ordinal) && db.Provider == DbType.PostgreSQL ? " DEFAULT false" : " DEFAULT 0"));
            if (db.Provider == DbType.SqlServer) sql.Add(StudioSqlServerDescriptions.AddProperty(table.Name, name, Descriptions[name], db.Schema));
            if (db.Provider == DbType.PostgreSQL) sql.Add($"COMMENT ON COLUMN {db.Table(table.Name)}.{StudioSchema.Quote(name, db.Provider)} IS '{Descriptions[name]}'");
        }
        sql.Add($"UPDATE {db.Table("studio_workbook")} SET history_sequence=version_sequence");
        sql.Add($"UPDATE {db.Table("studio_workbook_version")} SET history_version=content_version,pinned_on=created_on");
        sql.Add($"UPDATE {db.Table("studio_workbook_session")} SET base_content_version=(SELECT content_version FROM {db.Table("studio_workbook_version")} WHERE id={StudioSchema.Quote("studio_workbook_session", db.Provider)}.base_version_id)");
        var versionTable = db.Provider == DbType.Sqlite ? StudioSchema.Quote("studio_workbook_version", db.Provider) : db.Table("studio_workbook_version");
        sql.Add($"CREATE UNIQUE INDEX uq_st_workbook_history ON {versionTable} (workbook_id,history_version) WHERE history_version IS NOT NULL");
        sql.Add(StudioSchema.CreateTable(Retired, db.Provider, db.Schema));
        if (db.Provider == DbType.SqlServer) sql.AddRange(StudioSqlServerDescriptions.Statements(Retired, db.Schema));
        if (db.Provider == DbType.PostgreSQL)
        {
            sql.Add($"COMMENT ON TABLE {db.Table(Retired.Name)} IS '工作副本替换后的文件回收队列；不处理历史版本或恢复副本'");
            foreach (var column in Retired.Columns.Split(',').Select(c => c.Split(':')[0])) sql.Add($"COMMENT ON COLUMN {db.Table(Retired.Name)}.{StudioSchema.Quote(column, db.Provider)} IS '{StudioSqlServerDescriptions.ColumnDescription(column)}'");
        }
        var retiredTable = db.Provider == DbType.Sqlite ? StudioSchema.Quote(Retired.Name, db.Provider) : db.Table(Retired.Name);
        sql.Add($"CREATE UNIQUE INDEX uq_st_workbook_retired_reference ON {retiredTable} (tenant_n_id,file_reference_n_id)");
        sql.Add($"CREATE INDEX ix_st_workbook_retired_created ON {retiredTable} (created_on)");
        return new(Step, "工作副本原位保存、手动历史版本及可靠临时文件回收", sql);
    }
    public static void Verify(StudioDatabase db)
    {
        foreach (var table in Additions) IndustrialPlatform.Infrastructure.Database.SchemaPhysicalDriftGuard.Validate(db.Client, table.Name, table.Columns.Split(',').Select(c => c.Split(':')[0]).ToArray(), table.Name == "studio_workbook_version" ? ["uq_st_workbook_history"] : []);
        IndustrialPlatform.Infrastructure.Database.SchemaPhysicalDriftGuard.Validate(db.Client, Retired.Name, Retired.Columns.Split(',').Select(c => c.Split(':')[0]).ToArray(), ["uq_st_workbook_retired_reference", "ix_st_workbook_retired_created"]);
    }
}
