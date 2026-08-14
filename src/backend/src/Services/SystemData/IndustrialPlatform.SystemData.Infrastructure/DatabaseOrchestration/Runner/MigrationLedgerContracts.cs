namespace IndustrialPlatform.SystemData.Infrastructure.DatabaseOrchestration.Runner;

/// <summary>
/// 目标库迁移账本契约(05 方案 §7.1.4):Executor 首次应用时在目标库自建账本表,
/// Inspector/Executor 共用表名、列名与 SQL 模板。账本只含迁移步骤标识、版本与时间,不含任何 Secret/SQL。
/// 时间列类型按目标提供程序分支(PostgreSQL timestamptz / SQLite TEXT),与 SD-001 迁移框架一致。
/// </summary>
internal static class MigrationLedgerContracts
{
    public const string TableName = "system_data_database_migration_ledger";

    public const string AppliedSequenceColumn = "applied_sequence";

    public const string MigrationIdColumn = "migration_id";

    public const string VersionColumn = "version";

    public const string AppliedOnColumn = "applied_on";

    /// <summary>建账本表(幂等);<paramref name="timestampType"/> 为目标库时间列类型词。</summary>
    public static string EnsureTableSql(string timestampType) => $"""
        CREATE TABLE IF NOT EXISTS {TableName} (
          {AppliedSequenceColumn} INTEGER NOT NULL,
          {MigrationIdColumn} TEXT PRIMARY KEY,
          {VersionColumn} TEXT NOT NULL,
          {AppliedOnColumn} {timestampType} NOT NULL
        );
        """;

    /// <summary>目标库当前迁移版本(最后应用记录)。</summary>
    public const string LatestVersionSql = $"""
        SELECT {VersionColumn} FROM {TableName}
        ORDER BY {AppliedSequenceColumn} DESC LIMIT 1
        """;

    /// <summary>已应用步骤标识(按应用顺序)。</summary>
    public const string AppliedIdsSql = $"""
        SELECT {MigrationIdColumn} FROM {TableName}
        ORDER BY {AppliedSequenceColumn} ASC
        """;

    /// <summary>记账单条(应用顺序在事务内自增)。</summary>
    public const string RecordStepSql = $"""
        INSERT INTO {TableName} ({AppliedSequenceColumn}, {MigrationIdColumn}, {VersionColumn}, {AppliedOnColumn})
        VALUES ((SELECT COALESCE(MAX({AppliedSequenceColumn}), 0) + 1 FROM {TableName}), @migrationId, @version, @appliedOn)
        """;
}
