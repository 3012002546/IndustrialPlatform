using System.Data.Common;
using IndustrialPlatform.SystemData.Domain.DatabaseOrchestration;

namespace IndustrialPlatform.SystemData.Infrastructure.DatabaseOrchestration.Runner;

/// <summary>
/// 目标库种子账本契约(TASK-SD-004,蓝图 §5.3):执行器首次应用时在目标库自建账本表,
/// 执行器与 Verify 共用表名、列名与 SQL 模板。账本只含种子键/版本/校验和/状态与时间,不含任何 Secret/SQL。
/// 表名按 ModuleKey 派生(<c>{moduleKey}_seed_ledger</c>);PK (module_key, seed_key, seed_version)
/// 唯一幂等,同版本不同校验和视为 drift 由执行器拒绝,版本升级追加新行。
/// 时间列类型按目标提供程序分支(PostgreSQL timestamptz / SQLite TEXT)。
/// </summary>
internal static class SeedLedgerContracts
{
    public const string AppliedSequenceColumn = "applied_sequence";

    public const string ModuleKeyColumn = "module_key";

    public const string SeedKeyColumn = "seed_key";

    public const string SeedVersionColumn = "seed_version";

    public const string ChecksumColumn = "checksum";

    public const string ScopeColumn = "scope";

    public const string StatusColumn = "status";

    public const string AppliedOnColumn = "applied_on";

    public const string OperationNIdColumn = "operation_n_id";

    public const string TraceIdColumn = "trace_id";

    /// <summary>按模块标识派生目标库种子账本表名(标识转安全 SQL 标识,不引号包裹)。</summary>
    public static string TableName(string moduleKey) => $"{MigrationLedgerContracts.SanitizeModuleKey(moduleKey)}_seed_ledger";

    /// <summary>建账本表(幂等);<paramref name="timestampType"/> 为目标库时间列类型词。</summary>
    public static string EnsureTableSql(string moduleKey, string timestampType) => $"""
        CREATE TABLE IF NOT EXISTS {TableName(moduleKey)} (
          {AppliedSequenceColumn} INTEGER NOT NULL,
          {ModuleKeyColumn} TEXT NOT NULL,
          {SeedKeyColumn} TEXT NOT NULL,
          {SeedVersionColumn} TEXT NOT NULL,
          {ChecksumColumn} TEXT NOT NULL,
          {ScopeColumn} INTEGER NOT NULL,
          {StatusColumn} INTEGER NOT NULL,
          {AppliedOnColumn} {timestampType} NULL,
          {OperationNIdColumn} TEXT NULL,
          {TraceIdColumn} TEXT NULL,
          PRIMARY KEY ({ModuleKeyColumn}, {SeedKeyColumn}, {SeedVersionColumn})
        );
        """;

    /// <summary>按 (seed_key, seed_version) 精确读取单条账本条目(PK 唯一,至多一条)。</summary>
    public static string FindSql(string moduleKey) => $"""
        SELECT {SeedKeyColumn}, {SeedVersionColumn}, {ChecksumColumn}, {ScopeColumn}, {StatusColumn}, {AppliedOnColumn}, {OperationNIdColumn}
        FROM {TableName(moduleKey)}
        WHERE {SeedKeyColumn} = @seedKey AND {SeedVersionColumn} = @seedVersion
        LIMIT 1
        """;

    /// <summary>记账单条(应用顺序在事务内自增;PK 冲突由执行器按幂等/drift 语义裁决)。</summary>
    public static string RecordSql(string moduleKey) => $"""
        INSERT INTO {TableName(moduleKey)} ({AppliedSequenceColumn}, {ModuleKeyColumn}, {SeedKeyColumn}, {SeedVersionColumn}, {ChecksumColumn}, {ScopeColumn}, {StatusColumn}, {AppliedOnColumn}, {OperationNIdColumn}, {TraceIdColumn})
        VALUES ((SELECT COALESCE(MAX({AppliedSequenceColumn}), 0) + 1 FROM {TableName(moduleKey)}), @moduleKey, @seedKey, @seedVersion, @checksum, @scope, @status, @appliedOn, @operationNId, @traceId)
        """;

    /// <summary>把 <see cref="RecordSql"/> 的参数绑定到目标库命令(Sqlite/PostgreSQL 通用,经 <see cref="DbCommand.CreateParameter"/>)。</summary>
    internal static void BindRecordParameters(
        DbCommand command,
        string moduleKey,
        string seedKey,
        string seedVersion,
        string checksum,
        SeedScope scope,
        SeedStatus status,
        DateTimeOffset? appliedOn,
        string? operationNId,
        string traceId)
    {
        command.CommandText = RecordSql(moduleKey);
        Bind(command, "moduleKey", moduleKey);
        Bind(command, "seedKey", seedKey);
        Bind(command, "seedVersion", seedVersion);
        Bind(command, "checksum", checksum);
        Bind(command, "scope", (int)scope);
        Bind(command, "status", (int)status);
        Bind(command, "appliedOn", appliedOn);
        Bind(command, "operationNId", operationNId);
        Bind(command, "traceId", traceId);
    }

    private static void Bind(DbCommand command, string name, object? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }
}
