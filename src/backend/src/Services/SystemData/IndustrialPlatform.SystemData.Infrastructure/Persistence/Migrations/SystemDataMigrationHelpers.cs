using SqlSugar;

namespace IndustrialPlatform.SystemData.Infrastructure.Persistence.Migrations;

/// <summary>
/// SystemData 建表 DDL 助手(不能引用 Identity,自建等价模式)。
/// 类型词按目标数据库分支:SQLite 用 TEXT/INTEGER,PostgreSQL 用 uuid/timestamptz/BOOLEAN/BIGINT。
/// 软删除复用:业务唯一键一律建为部分唯一索引 <c>WHERE is_deleted = {f}</c>;
/// 同库父子表复合外键 <c>(id, is_deleted)</c> ON UPDATE CASCADE 同步子表父删除状态快照。
/// </summary>
internal static class SystemDataMigrationHelpers
{
    /// <summary>创建建表迁移步骤,事务由运行器包裹。</summary>
    public static SchemaMigrationStep CreateTableStep(
        string id,
        string tableName,
        Func<DbType, string> ddlBuilder) =>
        new(id, $"create {tableName}", (sugar, _) => sugar.Ado.ExecuteCommandAsync(ddlBuilder(sugar.CurrentConnectionConfig.DbType)));

    /// <summary>按目标数据库类型给出 (Guid, DateTimeOffset, Boolean, BigInt, false 字面量) 类型词。</summary>
    public static (string G, string T, string B, string Big, string F) TypeWords(DbType dbType) =>
        dbType switch
        {
            DbType.Sqlite => ("TEXT", "TEXT", "INTEGER", "INTEGER", "0"),
            DbType.PostgreSQL => ("uuid", "timestamptz", "BOOLEAN", "BIGINT", "false"),
            _ => throw new NotSupportedException($"不支持的目标数据库类型:{dbType}。"),
        };

    /// <summary>9 个公共生命周期列。</summary>
    public static string CommonColumns(string g, string t, string b, string big) =>
        string.Join(",\n",
        [
            $"id {g} PRIMARY KEY NOT NULL",
            $"is_frozen {b} NOT NULL",
            $"is_locked {b} NOT NULL",
            $"is_deleted {b} NOT NULL",
            "entity_type TEXT NOT NULL",
            $"created_on {t} NOT NULL",
            $"last_updated_on {t} NOT NULL",
            $"optimistic_version {big} NOT NULL",
            $"concurrency_version {g} NOT NULL",
        ]);
}
