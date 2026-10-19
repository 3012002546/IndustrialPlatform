using IndustrialPlatform.Infrastructure.Database;
using IndustrialPlatform.SystemData.Infrastructure.Scheduler;
using SqlSugar;

namespace IndustrialPlatform.SystemData.Infrastructure.Persistence.Migrations;

/// <summary>PF07两张业务表增量迁移；原生存储另外受控初始化。</summary>
public static class SchedulerSchemaMigrations
{
    public static IReadOnlyList<SchemaMigrationStep> All { get; } =
    [
        new("SCHED-001-Job", "任务定义及唯一周期计划-v1", (db, token) => db.Ado.ExecuteCommandAsync(JobDdl(db.CurrentConnectionConfig.DbType), parameters: null, cancellationToken: token), (db, _) => ValidateJob(db)),
        new("SCHED-002-Run", "运行快照及唯一活动槽-v1", (db, token) => db.Ado.ExecuteCommandAsync(RunDdl(db.CurrentConnectionConfig.DbType), parameters: null, cancellationToken: token), (db, _) => ValidateRun(db)),
        new("SCHED-003-Hangfire", "受控Hangfire1.8.25-PG1.20.13-SQLite0.4.3原生存储-v1", SchedulerStorageSchema.InitializeAsync),
    ];

    public static string JobDdl(DbType provider)
    {
        var (g,t,b,big,f) = Words(provider);
        var text = provider == DbType.SqlServer ? "nvarchar" : provider == DbType.PostgreSQL ? "varchar" : "TEXT";
        string Key(int n) => provider == DbType.Sqlite ? "TEXT" : $"{text}({n})";
        var json = provider == DbType.SqlServer ? "nvarchar(max)" : "TEXT";
        var common = $"id {g} PRIMARY KEY NOT NULL, is_frozen {b} NOT NULL, is_locked {b} NOT NULL, is_deleted {b} NOT NULL, entity_type {Key(512)} NOT NULL, created_on {t} NOT NULL, last_updated_on {t} NOT NULL, optimistic_version {big} NOT NULL, concurrency_version {g} NOT NULL";
        return Create(provider, "scheduler_job", $"""
            {common}, tenant_n_id {Key(128)} NOT NULL, n_id {Key(128)} NOT NULL,
            name {Key(128)} NOT NULL, description {Key(1000)} NULL, handler_key {Key(128)} NOT NULL,
            parameters_json {json} NOT NULL, status {Key(32)} NOT NULL CHECK(status IN ('Disabled','Enabled')),
            cron_expression {Key(120)} NOT NULL, time_zone_id {Key(128)} NOT NULL,
            timeout_seconds INTEGER NOT NULL CHECK(timeout_seconds BETWEEN 5 AND 3600),
            max_attempts INTEGER NOT NULL CHECK(max_attempts BETWEEN 1 AND 5),
            retry_safety {Key(32)} NOT NULL CHECK(retry_safety IN ('Idempotent','NoAutomaticRecovery')),
            is_system_owned {b} NOT NULL, UNIQUE(tenant_n_id,n_id), UNIQUE(tenant_n_id,id)
            """) + Index(provider, "scheduler_job", "ix_scheduler_job_list", "tenant_n_id,status,is_deleted,name,n_id");
    }

    public static string RunDdl(DbType provider)
    {
        var (g,t,_,big,_) = Words(provider);
        string Key(int n) => provider == DbType.SqlServer ? $"nvarchar({n})" : provider == DbType.PostgreSQL ? $"varchar({n})" : "TEXT";
        var json = provider == DbType.SqlServer ? "nvarchar(max)" : "TEXT";
        return Create(provider, "scheduler_run", $"""
            id {g} PRIMARY KEY NOT NULL, n_id {Key(128)} NOT NULL, tenant_n_id {Key(128)} NOT NULL, created_on {t} NOT NULL,
            job_id {g} NOT NULL, source {Key(32)} NOT NULL CHECK(source IN ('Schedule','Manual','Retry')),
            invocation_key {Key(180)} NOT NULL, request_hash {Key(64)} NULL, original_run_n_id {Key(128)} NULL,
            operation_key {Key(128)} NOT NULL, requested_by_user_n_id {Key(128)} NULL, accepted_on {t} NOT NULL,
            status {Key(32)} NOT NULL CHECK(status IN ('Queued','Running','RetryWaiting','CancelRequested','Succeeded','Failed','Cancelled','TimedOut','Unknown')),
            job_name_snapshot {Key(128)} NOT NULL, handler_key {Key(128)} NOT NULL, parameters_json {json} NOT NULL,
            timeout_seconds INTEGER NOT NULL CHECK(timeout_seconds BETWEEN 5 AND 3600), max_attempts INTEGER NOT NULL CHECK(max_attempts BETWEEN 1 AND 5),
            retry_safety {Key(32)} NOT NULL, active_job_id {g} NULL, execution_token {g} NULL, worker_reference {Key(128)} NULL,
            attempts_used INTEGER NOT NULL, started_on {t} NULL, deadline_on {t} NULL,
            completed_on {t} NULL, cancel_requested_on {t} NULL, result_code {Key(64)} NULL, result_summary_json {json} NULL,
            engine_reference {Key(128)} NULL, trace_id {Key(64)} NOT NULL, state_version {big} NOT NULL,
            UNIQUE(tenant_n_id,n_id), UNIQUE(tenant_n_id,invocation_key),
            FOREIGN KEY(tenant_n_id,job_id) REFERENCES scheduler_job(tenant_n_id,id),
            CHECK(attempts_used BETWEEN 0 AND max_attempts),
            CHECK(active_job_id IS NULL OR (active_job_id=job_id AND execution_token IS NOT NULL AND worker_reference IS NOT NULL AND deadline_on IS NOT NULL))
            """) + Index(provider, "scheduler_run", "ux_scheduler_run_active_job", "tenant_n_id,active_job_id", unique: true, condition: "active_job_id IS NOT NULL")
            + Index(provider, "scheduler_run", "ix_scheduler_run_history", "tenant_n_id,job_id,accepted_on,n_id")
            + Index(provider, "scheduler_run", "ix_scheduler_run_state", "tenant_n_id,status,id");
    }

    private static Task ValidateJob(ISqlSugarClient db)
    {
        SchemaPhysicalDriftGuard.Validate(db, "scheduler_job", ["id", "tenant_n_id", "n_id", "is_frozen", "is_locked", "is_deleted", "entity_type", "created_on", "last_updated_on", "optimistic_version", "concurrency_version", "name", "description", "handler_key", "parameters_json", "status", "cron_expression", "time_zone_id", "timeout_seconds", "max_attempts", "retry_safety", "is_system_owned"], ["ix_scheduler_job_list"]);
        return Task.CompletedTask;
    }
    private static Task ValidateRun(ISqlSugarClient db)
    {
        SchemaPhysicalDriftGuard.Validate(db, "scheduler_run", ["id", "n_id", "tenant_n_id", "created_on", "job_id", "source", "invocation_key", "request_hash", "original_run_n_id", "operation_key", "requested_by_user_n_id", "accepted_on", "status", "job_name_snapshot", "handler_key", "parameters_json", "timeout_seconds", "max_attempts", "retry_safety", "active_job_id", "execution_token", "worker_reference", "attempts_used", "started_on", "deadline_on", "completed_on", "cancel_requested_on", "result_code", "result_summary_json", "engine_reference", "trace_id", "state_version"], ["ux_scheduler_run_active_job", "ix_scheduler_run_history", "ix_scheduler_run_state"]);
        return Task.CompletedTask;
    }

    private static string Create(DbType provider, string table, string columns)
    {
        if (provider == DbType.Sqlite)
        {
            var lengths = table == "scheduler_job" ? new Dictionary<string, int>
            { ["n_id"] = 128, ["tenant_n_id"] = 128, ["name"] = 128, ["description"] = 1000, ["entity_type"] = 512, ["handler_key"] = 128, ["status"] = 32, ["cron_expression"] = 120, ["time_zone_id"] = 128, ["retry_safety"] = 32 }
                : new Dictionary<string, int>
            { ["n_id"] = 128, ["tenant_n_id"] = 128, ["source"] = 32, ["invocation_key"] = 180, ["request_hash"] = 64, ["original_run_n_id"] = 128, ["operation_key"] = 128, ["requested_by_user_n_id"] = 128, ["status"] = 32, ["job_name_snapshot"] = 128, ["handler_key"] = 128, ["retry_safety"] = 32, ["worker_reference"] = 128, ["result_code"] = 64, ["engine_reference"] = 128, ["trace_id"] = 64 };
            foreach (var field in lengths) columns += $", CHECK({field.Key} IS NULL OR length({field.Key})<={field.Value})";
            columns += ", CHECK(length(CAST(parameters_json AS BLOB))<=16384)";
            columns += table == "scheduler_job" ? ", CHECK(is_frozen IN(0,1) AND is_locked IN(0,1) AND is_deleted IN(0,1) AND is_system_owned IN(0,1))"
                : ", CHECK(result_summary_json IS NULL OR length(CAST(result_summary_json AS BLOB))<=4096)";
        }
        if (provider == DbType.SqlServer)
            foreach (var field in new[] { "tenant_n_id", "n_id", "handler_key", "invocation_key", "operation_key" })
                columns = System.Text.RegularExpressions.Regex.Replace(columns, $"\\b{field} nvarchar\\((\\d+)\\)", $"{field} nvarchar($1) COLLATE Latin1_General_100_BIN2");
        var ddl = provider == DbType.SqlServer ? $"IF OBJECT_ID(N'dbo.{table}',N'U') IS NULL BEGIN CREATE TABLE dbo.{table} ({columns}); END;\n" : $"CREATE TABLE IF NOT EXISTS {table} ({columns});\n";
        return ddl + SchedulerSchemaDescriptions.Sql(provider, table, columns);
    }
    private static string Index(DbType provider, string table, string name, string columns, bool unique=false, string? condition=null)
    {
        var where = condition is null ? string.Empty : " WHERE " + condition;
        var uq = unique ? "UNIQUE " : string.Empty;
        return provider == DbType.SqlServer ? $"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.{table}') AND name=N'{name}') CREATE {uq}INDEX {name} ON dbo.{table} ({columns}){where};\n"
            : $"CREATE {uq}INDEX IF NOT EXISTS {name} ON {table} ({columns}){where};\n";
    }
    private static (string G,string T,string B,string Big,string F) Words(DbType provider) => provider == DbType.SqlServer
        ? ("uniqueidentifier","datetimeoffset(7)","bit","bigint","0") : SystemDataMigrationHelpers.TypeWords(provider);
}
