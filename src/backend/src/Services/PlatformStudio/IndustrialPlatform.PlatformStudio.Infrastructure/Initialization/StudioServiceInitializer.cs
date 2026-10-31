using System.Globalization;
using IndustrialPlatform.Application.Abstractions.Initialization;
using IndustrialPlatform.Infrastructure.Database;
using IndustrialPlatform.PlatformStudio.Domain;
using IndustrialPlatform.PlatformStudio.Infrastructure.Persistence;
using IndustrialPlatform.RuntimeSupport.Migrations;
using SqlSugar;

namespace IndustrialPlatform.PlatformStudio.Infrastructure.Initialization;

/// <summary>服务自有六步迁移、统一账本与只读Inspect；不调用SystemData整套初始化器。</summary>
public sealed class StudioServiceInitializer(StudioDatabase database, StudioDeploymentProfile profile, TimeProvider clock) : IServiceInitializer
{
    public const string CurrentVersion = "studio-core-schema-v1";
    public string ServiceKey => "platform-studio";
    public string ModuleKey => "studio-core";
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static readonly string[] FileSqlServerIds = ["PF04-001-01", "PF04-001-02", "PF04-001-03", "PF04-001-04", "PF05-003"];
    private static readonly string[] AuditSqlServerIds = ["PF04-001-08", "PF04-001-09", "PF04-001-10", "PF04-001-11", "PF04-001-12"];
    private ISqlSugarClient Db => database.Client;
    private bool _hasJobsHistory;
    public sealed record Migration(string Id, string Description, IReadOnlyList<string> Statements)
    {
        public string Checksum => StudioJson.Hash(string.Join("\n", Statements));
    }
    public sealed class MigrationRow
    {
        public string MigrationId { get; set; } = "";
        public string Description { get; set; } = "";
        public string? Checksum { get; set; }
        public DateTimeOffset AppliedOn { get; set; }
    }

    public IReadOnlyList<Migration> Migrations()
    {
        var provider = database.Provider;
        var schema = database.Schema;
        var steps = StudioSchema.Core.GroupBy(table => table.Step).Select(group =>
        {
            var sql = group.Select(table => StudioSchema.CreateTable(table, provider, schema)).ToList();
            sql.AddRange(Indexes(group.Key));
            if (provider == DbType.SqlServer)
            {
                foreach (var table in group) sql.AddRange(StudioSqlServerDescriptions.Statements(table, schema));
                if (group.Key == "STU-001-Resources") sql.AddRange(StudioSqlServerDescriptions.Statements(StudioSqlServerDescriptions.MigrationLedger, schema));
            }
            if (group.Key == "STU-001-Resources") sql.AddRange(StudioSchemaIntegrity.ImmutableRevisionSql(database));
            if (group.Key == "STU-001-Resources" && provider != DbType.Sqlite)
            {
                sql.Add($"ALTER TABLE {database.Table("studio_resource")} ADD CONSTRAINT fk_st_resource_draft FOREIGN KEY (draft_base_revision_id) REFERENCES {database.Table("studio_revision")}(id)");
                sql.Add($"ALTER TABLE {database.Table("studio_resource")} ADD CONSTRAINT fk_st_resource_published FOREIGN KEY (published_revision_id) REFERENCES {database.Table("studio_revision")}(id)");
            }
            if (group.Key == "STU-005-Workbook" && provider != DbType.Sqlite) sql.Add($"ALTER TABLE {database.Table("studio_workbook")} ADD CONSTRAINT fk_st_workbook_current FOREIGN KEY (current_version_id) REFERENCES {database.Table("studio_workbook_version")}(id)");
            return new Migration(group.Key, "Studio固定持久结构：" + string.Join(',', group.Select(table => table.Name)), sql);
        }).ToList();
        if (profile.Files.Provider == "Embedded")
        {
            var ddl = provider == DbType.SqlServer
                ? FileSqlServerIds.Select(id => SqlServerMigrationArtifacts.Read(typeof(RuntimeFileAuditSchema).Assembly, id)).ToList()
                : new List<string> { RuntimeFileAuditSchema.FileUploadSessionDdl(provider), RuntimeFileAuditSchema.FileObjectDdl(provider), RuntimeFileAuditSchema.FileScanAttemptDdl(provider), RuntimeFileAuditSchema.FileReferenceGrantDdl(provider), RuntimeFileAuditSchema.FileStatusOutboxDdl(provider) };
            ddl.Add(RuntimeFileOwnershipSchema.UploadContextColumn(provider));
            steps.Add(new("STF-001-FileCore", "Embedded文件传输、扫描与持久上传归属", ddl));
            steps.Add(new("STF-002-OwnedReference", "业务文件引用与保全", [RuntimeFileOwnershipSchema.OwnedTable(provider)]));
        }
        if (profile.Audit.Provider == "Embedded")
        {
            var ddl = provider == DbType.SqlServer
                ? AuditSqlServerIds.Select(id => SqlServerMigrationArtifacts.Read(typeof(RuntimeFileAuditSchema).Assembly, id)).ToList()
                : new List<string> { RuntimeFileAuditSchema.AuditFactDdl(provider), RuntimeFileAuditSchema.AuditLifecycleDdl(provider), RuntimeFileAuditSchema.AuditIngressFailureDdl(provider), RuntimeFileAuditSchema.AuditOutboxDdl(provider), RuntimeFileAuditSchema.AuditLifecycleLegalHoldAlterDdl(provider) };
            steps.Add(new("STA-001-AuditCore", "Embedded持久审计及保全", ddl));
        }
        if (profile.Jobs.Provider == "Embedded" || _hasJobsHistory)
        {
            var jobs = new List<string> { IndustrialPlatform.SystemData.Infrastructure.Persistence.Migrations.SchedulerSchemaMigrations.JobDdl(provider), IndustrialPlatform.SystemData.Infrastructure.Persistence.Migrations.SchedulerSchemaMigrations.RunDdl(provider) };
            jobs.AddRange(IndustrialPlatform.RuntimeSupport.Jobs.SchedulerOnceSchema.Statements(provider));
            steps.Add(new("STJ-001-JobRun", "Embedded Jobs任务运行与一次性业务归属", jobs));
            steps.Add(new("STJ-002-EngineStorage", "Hangfire固定Provider原生存储1.8.25", ["SELECT 1"]));
        }
        // 只追加列，既有链接默认仍为有限期、固定修订；不改STU-007或其他已应用迁移的摘要。
        var screenPolicy = StudioSchema.ScreenPolicy;
        var policySql = screenPolicy.Columns.Split(',').Select(column =>
            $"ALTER TABLE {database.Table(screenPolicy.Name)} ADD {StudioSchema.Column(column, provider)} DEFAULT {(provider == DbType.PostgreSQL ? "false" : "0")}").ToList();
        if (provider == DbType.SqlServer)
            policySql.AddRange(screenPolicy.Columns.Split(',').Select(column => StudioSqlServerDescriptions.AddProperty(screenPolicy.Name, column.Split(':')[0], StudioSqlServerDescriptions.ColumnDescription(column.Split(':')[0]), schema)));
        steps.Add(new(screenPolicy.Step, "看板大屏无限期与跟随最新发布策略；保留既有链接规则", policySql));
        steps.Add(StudioWorkbookSchema.Migration(database));
        return steps;
    }

    private IEnumerable<string> Indexes(string step)
    {
        string Index(string table, string suffix, string columns, bool unique = false, string? filter = null) => $"CREATE {(unique ? "UNIQUE " : "")}INDEX {StudioSchema.Quote((unique ? "uq_st_" : "ix_st_") + suffix, database.Provider)} ON {(database.Provider == DbType.Sqlite ? StudioSchema.Quote(table, database.Provider) : database.Table(table))} ({columns})" + (filter is null ? "" : " WHERE " + filter);
        switch (step)
        {
            case "STU-007-DashboardScreen": yield return Index("studio_dashboard_screen", "screen_resource", "tenant_n_id,resource_n_id,created_on"); break;
            case "STU-001-Resources":
                yield return Index("studio_source", "source_provider", "tenant_n_id,provider,is_enabled");
                yield return Index("studio_resource", "resource_list", "tenant_n_id,kind,is_deleted,last_updated_on,id");
                yield return Index("studio_grant", "grant_subject", "tenant_n_id,subject_kind,subject_n_id");
                break;
            case "STU-002-Ledger":
                yield return Index("studio_ledger_record", "ledger_list", "tenant_n_id,model_resource_id,is_deleted,created_on,id");
                foreach (var (column, suffix) in new[] { ("text_value", "text"), ("number_scaled", "number"), ("bool_value", "boolean"), ("time_value", "time") }) yield return Index("studio_ledger_value", "ledger_" + suffix, "tenant_n_id,model_resource_id,field_id," + column + ",record_id");
                yield return Index("studio_ledger_value", "ledger_unique", "tenant_n_id,model_resource_id,field_id,unique_key", true, "unique_key IS NOT NULL");
                break;
            case "STU-003-AuditOutbox": yield return Index("studio_audit_outbox", "audit_pending", "status,next_attempt_on,id"); break;
            case "STU-004-Report":
                yield return Index("studio_report_run", "report_status", "tenant_n_id,status,created_on,id");
                yield return Index("studio_report_run", "report_scheduler", "scheduler_run_n_id");
                yield return Index("studio_report_run", "preview_expiry", "execution_kind,preview_expires_on,status");
                break;
            case "STU-005-Workbook":
                yield return Index("studio_workbook", "workbook_owner", "tenant_n_id,owner_user_n_id,is_deleted,last_updated_on");
                yield return Index("studio_workbook_session", "workbook_session", "workbook_id,status,expires_on");
                yield return Index("studio_workbook_session", "workbook_lock", "workbook_id", true, "lock_hash IS NOT NULL");
                break;
            case "STU-006-ExternalIdentity":
                yield return Index("studio_auth_request", "auth_request_expiry", "expires_on");
                yield return Index("studio_auth_session", "auth_subject", "tenant_n_id,subject_n_id,revoked_on");
                yield return Index("studio_auth_session", "auth_session_expiry", "expires_on");
                break;
        }
    }

    public async Task<ServiceInitializationState> InspectLocalAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (database.Provider == DbType.Sqlite && !File.Exists(new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder(database.ConnectionString).DataSource)) return State(false, "Studio数据库文件不存在，须受控Apply初始化。");
            await database.EnsureTargetAsync(cancellationToken);
            if (!await database.TableExistsAsync("studio_schema_migrations", cancellationToken)) return State(false, "Studio初始化账本不存在。");
            var columns = await ColumnsAsync("studio_schema_migrations", cancellationToken);
            if (!columns.Contains("checksum")) return State(false, "Studio旧账本缺少checksum列，需要受控Apply升级。");
            var rows = await ReadLedgerAsync(cancellationToken);
            if (rows.Any(row => !Migrations().Any(migration => migration.Id == row.MigrationId))) return State(false, "STUDIO_SCHEMA_DRIFT:UNKNOWN_MIGRATION");
            foreach (var migration in Migrations())
            {
                var row = rows.SingleOrDefault(row => row.MigrationId == migration.Id);
                if (row is null) return State(false, "Studio尚有未应用迁移。" + migration.Id);
                if (row.Description != migration.Description || row.Checksum != migration.Checksum) return State(false, "STUDIO_SCHEMA_DRIFT:" + migration.Id);
            }
            foreach (var table in StudioSchema.Core)
            {
                if (!await database.TableExistsAsync(table.Name, cancellationToken)) return State(false, "STUDIO_SCHEMA_DRIFT:" + table.Name);
                var actual = await ColumnsAsync(table.Name, cancellationToken);
                var quotedTarget = database.Provider == DbType.Sqlite ? StudioSchema.Quote(table.Name, database.Provider) : database.Table(table.Name);
                var expectedIndexes = Indexes(table.Step).Where(sql => sql.Contains(" ON " + quotedTarget + " (", StringComparison.Ordinal)).Select(sql => System.Text.RegularExpressions.Regex.Match(sql, "INDEX [\"\\[]([a-z0-9_]+)", System.Text.RegularExpressions.RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)).Groups[1].Value).ToArray();
                SchemaPhysicalDriftGuard.Validate(Db, table.Name, table.Columns.Split(',').Select(column => column.Split(':')[0]).ToArray(), expectedIndexes);
                if (table.Columns.Split(',').Select(column => column.Split(':')[0]).Any(column => !actual.Contains(column))) return State(false, "STUDIO_SCHEMA_DRIFT:" + table.Name);
            }
            await StudioSchemaIntegrity.ValidatePhysicalAsync(database, Migrations(), cancellationToken);
            if (!await database.TableExistsAsync("studio_seed_ledger", cancellationToken)) return State(false, "Studio种子账本不存在。");
            if (database.Provider == DbType.SqlServer)
            {
                await StudioSqlServerDescriptions.VerifyAsync(database, StudioSqlServerDescriptions.MigrationLedger, cancellationToken);
                await StudioSqlServerDescriptions.VerifyAsync(database, StudioSqlServerDescriptions.SeedLedger, cancellationToken);
            }
            var baseline = await Db.Ado.GetIntAsync($"SELECT COUNT(*) FROM {database.Table("studio_seed_ledger")} WHERE seed_n_id=@seed AND seed_version='1' AND checksum=@checksum AND status='Applied'", new { seed = "studio.core.baseline", checksum = BaselineChecksum }, cancellationToken);
            return State(baseline == 1, baseline == 1 ? null : "Studio共同注册种子尚未完成。");
        }
        catch (StudioException exception) { return State(false, exception.Code); }
        catch (InvalidOperationException exception) when (exception.Message.StartsWith("physical schema drift:", StringComparison.Ordinal)) { return State(false, "STUDIO_SCHEMA_DRIFT"); }
        catch (Exception exception) when (exception is not OperationCanceledException) { return State(false, "STUDIO_DATABASE_UNAVAILABLE:" + exception.GetType().Name); }
    }

    private ServiceInitializationState State(bool ready, string? reason) => new(ServiceKey, ModuleKey, ready ? CurrentVersion : null, ready, ready, true, ready, reason);
    private static string BaselineChecksum => StudioJson.Hash(string.Join('|', StudioKinds.Permissions) + "|" + string.Join('|', StudioDocuments.ComponentTypes));

    public async Task InitializeLocalAsync(CancellationToken cancellationToken)
    {
        await Gate.WaitAsync(cancellationToken);
        try
        {
            await database.EnsureTargetAsync(cancellationToken);
            await using var targetLock = await StudioInitializationLock.AcquireAsync(database, cancellationToken);
            if (database.Provider == DbType.PostgreSQL && profile.DeploymentMode == "ExternalMes") await Db.Ado.ExecuteCommandAsync("CREATE SCHEMA IF NOT EXISTS " + StudioSchema.Quote(database.Schema, database.Provider));
            var ledgerExists = await database.TableExistsAsync("studio_schema_migrations", cancellationToken);
            if (!ledgerExists && await database.TableExistsAsync("studio_resource", cancellationToken)) throw new StudioException("STUDIO_SCHEMA_DRIFT", "目标已存在非本服务账本管理的同名对象，拒绝接管。", 409);
            if (!ledgerExists)
            {
                var ledger = database.Table("studio_schema_migrations");
                await Db.Ado.ExecuteCommandAsync($"CREATE TABLE {ledger} ({StudioSchema.Column("migration_id:key128", database.Provider)} PRIMARY KEY, {StudioSchema.Column("description:json64", database.Provider)}, {StudioSchema.Column("checksum:sha?", database.Provider)}, {StudioSchema.Column("applied_on:time", database.Provider)})");
            }
            else if (!(await ColumnsAsync("studio_schema_migrations", cancellationToken)).Contains("checksum"))
                await Db.Ado.ExecuteCommandAsync($"ALTER TABLE {database.Table("studio_schema_migrations")} ADD {StudioSchema.Column("checksum:sha?", database.Provider)}");
            var rows = await ReadLedgerAsync(cancellationToken);
            var migrations = Migrations();
            if (rows.Any(row => !migrations.Any(migration => migration.Id == row.MigrationId))) throw new StudioException("STUDIO_SCHEMA_DRIFT", "账本含未知或未来迁移，不允许降级接管。", 409);
            // 按已应用步骤的最终形状核验；后续ALTER已改变旧表，不能再拿单个旧CREATE与现表比较。
            var applied = migrations.Where(migration => rows.Any(row => row.MigrationId == migration.Id)).ToArray();
            if (applied.Length > 0) await StudioSchemaIntegrity.ValidatePhysicalAsync(database, applied, cancellationToken);
            foreach (var migration in migrations)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var old = rows.SingleOrDefault(row => row.MigrationId == migration.Id);
                if (old is not null)
                {
                    if (old.Description != migration.Description || old.Checksum is not null && old.Checksum != migration.Checksum) throw new StudioException("STUDIO_SCHEMA_DRIFT", "迁移说明或校验值发生漂移：" + migration.Id, 409);
                    foreach (var table in StudioSchema.Core.Where(table => table.Step == migration.Id))
                    {
                        var actual = await ColumnsAsync(table.Name, cancellationToken);
                        if (table.Columns.Split(',').Select(column => column.Split(':')[0]).Any(column => !actual.Contains(column))) throw new StudioException("STUDIO_SCHEMA_DRIFT", "旧账本对应物理结构缺失，拒绝回填校验值。", 409);
                    }
                    if (old.Checksum is null) await Db.Ado.ExecuteCommandAsync($"UPDATE {database.Table("studio_schema_migrations")} SET checksum=@checksum WHERE migration_id=@id AND checksum IS NULL", new { checksum = migration.Checksum, id = migration.Id }, cancellationToken);
                    continue;
                }
                await Db.Ado.BeginTranAsync();
                try
                {
                    if (migration.Id == "STJ-002-EngineStorage") await IndustrialPlatform.SystemData.Infrastructure.Scheduler.SchedulerStorageSchema.InitializeAsync(Db, cancellationToken);
                    foreach (var sql in migration.Statements)
                        foreach (var batch in System.Text.RegularExpressions.Regex.Split(sql, @"(?im)^\s*GO\s*$", System.Text.RegularExpressions.RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)).Where(batch => !string.IsNullOrWhiteSpace(batch))) await Db.Ado.ExecuteCommandAsync(batch, parameters: null, cancellationToken: cancellationToken);
                    await Db.Ado.ExecuteCommandAsync($"INSERT INTO {database.Table("studio_schema_migrations")} (migration_id,description,checksum,applied_on) VALUES (@id,@description,@checksum,@applied)", new { id = migration.Id, description = migration.Description, checksum = migration.Checksum, applied = clock.GetUtcNow() }, cancellationToken);
                    await Db.Ado.CommitTranAsync();
                }
                catch { await Db.Ado.RollbackTranAsync(); throw; }
            }
            await SeedAsync(cancellationToken);
            var state = await InspectLocalAsync(cancellationToken);
            if (!state.Ready) throw new StudioException("STUDIO_NOT_READY", state.Reason ?? "Studio初始化验证失败。", 503);
        }
        finally { Gate.Release(); }
    }

    private async Task SeedAsync(CancellationToken cancellationToken)
    {
        if (!await database.TableExistsAsync("studio_seed_ledger", cancellationToken))
        {
            await Db.Ado.ExecuteCommandAsync(StudioSchema.CreateTable(StudioSqlServerDescriptions.SeedLedger, database.Provider, database.Schema));
            if (database.Provider == DbType.SqlServer)
                foreach (var statement in StudioSqlServerDescriptions.Statements(StudioSqlServerDescriptions.SeedLedger, database.Schema)) await Db.Ado.ExecuteCommandAsync(statement, parameters: null, cancellationToken: cancellationToken);
        }
        var count = await Db.Ado.GetIntAsync($"SELECT COUNT(*) FROM {database.Table("studio_seed_ledger")} WHERE seed_n_id=@seed AND seed_version='1'", new { seed = "studio.core.baseline" }, cancellationToken);
        if (count > 0) return;
        var now = clock.GetUtcNow();
        await Db.Ado.ExecuteCommandAsync($"INSERT INTO {database.Table("studio_seed_ledger")} (id,is_frozen,is_locked,is_deleted,entity_type,created_on,last_updated_on,optimistic_version,concurrency_version,tenant_n_id,seed_n_id,seed_version,seed_class,scope,checksum,status,applied_on,system_data_operation_n_id,trace_id) VALUES (@id,@frozen,@locked,@deleted,@type,@now,@now,0,@version,@tenant,@seed,'1','SystemBaseline','system',@checksum,'Applied',@now,NULL,NULL)", new { id = Guid.NewGuid(), frozen = true, locked = false, deleted = false, type = "Studio.SeedLedger", now, version = Guid.NewGuid(), tenant = "system", seed = "studio.core.baseline", checksum = BaselineChecksum }, cancellationToken);
    }

    private async Task<List<MigrationRow>> ReadLedgerAsync(CancellationToken cancellationToken)
    {
        var rows = await Db.Ado.SqlQueryAsync<MigrationRow>($"SELECT migration_id AS MigrationId,description AS Description,checksum AS Checksum,applied_on AS AppliedOn FROM {database.Table("studio_schema_migrations")}", new { }, cancellationToken);
        _hasJobsHistory |= rows.Any(row => row.MigrationId.StartsWith("STJ-", StringComparison.Ordinal));
        return rows;
    }
    private async Task<HashSet<string>> ColumnsAsync(string table, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var sql = database.Provider switch
        {
            DbType.Sqlite => "SELECT name FROM pragma_table_info(@table)",
            _ => "SELECT column_name AS name FROM information_schema.columns WHERE table_schema=@schema AND table_name=@table",
        };
        var names = await Db.Ado.SqlQueryAsync<ColumnName>(sql, new { table, schema = database.Schema }, cancellationToken);
        return names.Select(item => item.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
    }
    public sealed class ColumnName { public string Name { get; set; } = ""; }

    public async Task<ServiceInitializationState> InspectAsync(ServiceInitializationContext context, CancellationToken cancellationToken) => Matches(context) ? await InspectLocalAsync(cancellationToken) : State(false, "STUDIO_INITIALIZATION_TARGET_MISMATCH");
    public Task<ServiceInitializationPlan> PlanAsync(ServiceInitializationContext context, ServiceInitializationState inspection, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!Matches(context)) throw new StudioException("STUDIO_INITIALIZATION_TARGET_MISMATCH", "初始化目标不一致。", 409);
        return Task.FromResult(new ServiceInitializationPlan(ServiceKey, ModuleKey, inspection.ObservedVersion, CurrentVersion, !inspection.Ready, Migrations().Select(step => step.Id).ToArray()));
    }
    public async Task<ServiceInitializationState> ApplyAsync(ServiceInitializationContext context, ServiceInitializationPlan plan, CancellationToken cancellationToken)
    {
        if (!Matches(context) || plan.ServiceKey != ServiceKey || plan.ModuleKey != ModuleKey || plan.DesiredVersion != CurrentVersion) throw new StudioException("STUDIO_INITIALIZATION_TARGET_MISMATCH", "初始化计划目标不一致。", 409);
        await InitializeLocalAsync(cancellationToken);
        return await InspectLocalAsync(cancellationToken);
    }
    public Task<ServiceInitializationState> VerifyAsync(ServiceInitializationContext context, CancellationToken cancellationToken) => InspectAsync(context, cancellationToken);
    private bool Matches(ServiceInitializationContext context) => context.ServiceKey == ServiceKey && context.ModuleKey == ModuleKey && context.DatabaseTarget.ServiceKey == ServiceKey && context.DatabaseTarget.LogicalDatabaseName == "platform-studio_db" && (string.IsNullOrEmpty(context.DesiredVersion) || context.DesiredVersion == CurrentVersion) && context.DatabaseTarget.EnvironmentName == context.EnvironmentName && database.MatchesTarget(context.DatabaseTarget);
}
