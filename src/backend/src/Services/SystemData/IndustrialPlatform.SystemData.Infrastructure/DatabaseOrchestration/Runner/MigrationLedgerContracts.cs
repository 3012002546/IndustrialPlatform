using System.Text;

namespace IndustrialPlatform.SystemData.Infrastructure.DatabaseOrchestration.Runner;

/// <summary>
/// 目标库迁移账本契约(05 方案 §7.1.4、蓝图 §5.2):Executor 首次应用时在目标库自建账本表,
/// Inspector/Executor 共用表名、列名与 SQL 模板。账本只含迁移步骤标识、版本与时间,不含任何 Secret/SQL。
/// 表名按 ModuleKey 派生(<c>{moduleKey}_schema_migrations</c>),共享库内多模块各自独立账本。
/// 时间列类型按目标提供程序分支(PostgreSQL timestamptz / SQLite TEXT),与 SD-001 迁移框架一致。
/// </summary>
internal static class MigrationLedgerContracts
{
    public const string AppliedSequenceColumn = "applied_sequence";

    public const string MigrationIdColumn = "migration_id";

    public const string VersionColumn = "version";

    public const string AppliedOnColumn = "applied_on";

    /// <summary>按模块标识派生目标库迁移账本表名(标识转安全 SQL 标识,不引号包裹)。</summary>
    public static string TableName(string moduleKey) => $"{SanitizeModuleKey(moduleKey)}_schema_migrations";

    /// <summary>建账本表(幂等);<paramref name="timestampType"/> 为目标库时间列类型词。</summary>
    public static string EnsureTableSql(string moduleKey, string timestampType) => $"""
        CREATE TABLE IF NOT EXISTS {TableName(moduleKey)} (
          {AppliedSequenceColumn} INTEGER NOT NULL,
          {MigrationIdColumn} TEXT PRIMARY KEY,
          {VersionColumn} TEXT NOT NULL,
          {AppliedOnColumn} {timestampType} NOT NULL
        );
        """;

    /// <summary>目标库当前迁移版本(最后应用记录)。</summary>
    public static string LatestVersionSql(string moduleKey) => $"""
        SELECT {VersionColumn} FROM {TableName(moduleKey)}
        ORDER BY {AppliedSequenceColumn} DESC LIMIT 1
        """;

    /// <summary>已应用步骤标识(按应用顺序)。</summary>
    public static string AppliedIdsSql(string moduleKey) => $"""
        SELECT {MigrationIdColumn} FROM {TableName(moduleKey)}
        ORDER BY {AppliedSequenceColumn} ASC
        """;

    /// <summary>记账单条(应用顺序在事务内自增)。</summary>
    public static string RecordStepSql(string moduleKey) => $"""
        INSERT INTO {TableName(moduleKey)} ({AppliedSequenceColumn}, {MigrationIdColumn}, {VersionColumn}, {AppliedOnColumn})
        VALUES ((SELECT COALESCE(MAX({AppliedSequenceColumn}), 0) + 1 FROM {TableName(moduleKey)}), @migrationId, @version, @appliedOn)
        """;

    /// <summary>把模块标识转换为安全 SQL 标识(仅保留字母数字、其余转下划线,小写对齐 PostgreSQL 未加引号标识符折叠)。</summary>
    internal static string SanitizeModuleKey(string moduleKey)
    {
        ArgumentNullException.ThrowIfNull(moduleKey);
        var builder = new StringBuilder(moduleKey.Length);
        foreach (var ch in moduleKey)
        {
            builder.Append(char.IsLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : '_');
        }

        return builder.Length == 0 ? "module" : builder.ToString();
    }
}
