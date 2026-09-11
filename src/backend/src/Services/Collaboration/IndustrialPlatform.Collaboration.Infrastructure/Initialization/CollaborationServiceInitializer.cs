using IndustrialPlatform.Application.Abstractions.Initialization;
using IndustrialPlatform.Infrastructure.Database;
using IndustrialPlatform.Collaboration.Infrastructure.Persistence;
using System.Security.Cryptography;
using System.Text;
using SqlSugar;

namespace IndustrialPlatform.Collaboration.Infrastructure;

/// <summary>Collaboration owns its schema ledger and performs migrations after Identity/SystemData.</summary>
public sealed class CollaborationServiceInitializer : IServiceInitializer
{
    private readonly SqlSugarDbContext _dbContext;

    public CollaborationServiceInitializer(SqlSugarDbContext dbContext) => _dbContext = dbContext;

    public string ServiceKey => "collaboration";
    public string ModuleKey => "collaboration";

    public async Task<ServiceInitializationState> InspectAsync(ServiceInitializationContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var dbType = _dbContext.SqlSugar.CurrentConnectionConfig.DbType;
            var ledgerColumns = dbType == SqlSugar.DbType.Sqlite
                ? _dbContext.SqlSugar.Ado.GetDataTable("SELECT name FROM pragma_table_info('collaboration_schema_migrations')").Rows.Cast<System.Data.DataRow>().Select(row => row["name"]?.ToString()).Where(name => name is not null).Select(name => name!).ToHashSet(StringComparer.OrdinalIgnoreCase)
                : _dbContext.SqlSugar.Ado.GetDataTable("SELECT column_name FROM information_schema.columns WHERE table_schema = current_schema() AND table_name = 'collaboration_schema_migrations'").Rows.Cast<System.Data.DataRow>().Select(row => row["column_name"]?.ToString()).Where(name => name is not null).Select(name => name!).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var requiredLedgerColumns = new[] { "migration_id", "description", "checksum", "applied_on" };
            var missingLedgerColumns = requiredLedgerColumns.Where(column => !ledgerColumns.Contains(column)).ToArray();
            if (missingLedgerColumns.Length > 0)
            {
                var reason = ledgerColumns.Count == 0
                    ? "Collaboration 本地迁移账本尚未创建。"
                    : $"Collaboration 本地迁移账本缺少列:{string.Join(',', missingLedgerColumns)}，需要执行 Apply 升级。";
                return new ServiceInitializationState(ServiceKey, ModuleKey, null, false, false, false, false, reason, []);
            }

            var appliedRows = await _dbContext.SqlSugar.Queryable<CollaborationSchemaMigrationRecord>().ToListAsync(cancellationToken);
            var applied = appliedRows.ToDictionary(item => item.MigrationId, StringComparer.Ordinal);
            var expected = CollaborationSchemaMigrations.All.Select(item => item.Id).ToArray();
            var missing = expected.Where(id => !applied.ContainsKey(id)).ToArray();
            var invalidChecksums = CollaborationSchemaMigrations.All
                .Where(step => applied.TryGetValue(step.Id, out var row) && !string.Equals(row.Checksum, Checksum(step), StringComparison.OrdinalIgnoreCase))
                .Select(step => step.Id)
                .ToArray();
            var ready = missing.Length == 0 && invalidChecksums.Length == 0;
            var version = expected.LastOrDefault(id => applied.ContainsKey(id) && !invalidChecksums.Contains(id, StringComparer.Ordinal));
            var facts = new List<string>();
            if (missing.Length > 0) facts.Add($"缺少迁移:{string.Join(',', missing)}");
            if (invalidChecksums.Length > 0) facts.Add($"迁移校验失败:{string.Join(',', invalidChecksums)}");
            if (!ready)
                return new ServiceInitializationState(ServiceKey, ModuleKey, version, false, true, true, false, $"Collaboration 本地架构尚未完成验证({string.Join(';', facts)})。", []);

            await VerifyCriticalTablesAsync(cancellationToken);
            return new ServiceInitializationState(ServiceKey, ModuleKey, version, true, true, true, true, null, []);
        }
        catch (Exception exception) when (IsMissingLocalColumn(exception))
        {
            return new ServiceInitializationState(ServiceKey, ModuleKey, null, false, false, false, false, "Collaboration 本地架构尚未完成升级，需要执行 Apply。", []);
        }
        catch (Exception exception) when (IsMissingTable(exception))
        {
            return new ServiceInitializationState(ServiceKey, ModuleKey, null, false, false, false, false, "Collaboration 本地迁移账本尚未创建。", []);
        }
        catch (InvalidOperationException exception) when (exception.Message.Contains("physical schema drift", StringComparison.OrdinalIgnoreCase))
        {
            return new ServiceInitializationState(ServiceKey, ModuleKey, null, false, false, false, false, $"Collaboration 本地物理架构校验失败:{exception.Message}", []);
        }
    }

    public Task<ServiceInitializationPlan> PlanAsync(ServiceInitializationContext context, ServiceInitializationState inspection, CancellationToken cancellationToken)
    {
        var desired = CollaborationSchemaMigrations.All[^1].Id;
        var steps = inspection.Ready ? Array.Empty<string>() : new[] { "collaboration-schema-migration", "collaboration-verify" };
        return Task.FromResult(new ServiceInitializationPlan(ServiceKey, ModuleKey, inspection.ObservedVersion, desired, !inspection.Ready || inspection.ObservedVersion != desired, steps));
    }

    public async Task<ServiceInitializationState> ApplyAsync(ServiceInitializationContext context, ServiceInitializationPlan plan, CancellationToken cancellationToken)
    {
        _dbContext.SqlSugar.CodeFirst.InitTables<CollaborationSchemaMigrationRecord>();
        await EnsureMigrationChecksumColumnAsync(cancellationToken);
        var applied = (await _dbContext.SqlSugar.Queryable<CollaborationSchemaMigrationRecord>().ToListAsync(cancellationToken)).ToDictionary(item => item.MigrationId, StringComparer.Ordinal);
        foreach (var step in CollaborationSchemaMigrations.All.OrderBy(item => item.Id, StringComparer.Ordinal))
        {
            if (applied.TryGetValue(step.Id, out var existing))
            {
                var checksum = Checksum(step);
                if (existing.Checksum is null)
                {
                    if (!string.Equals(existing.Description, step.Description, StringComparison.Ordinal))
                        throw new InvalidOperationException($"Collaboration migration description drift detected for '{step.Id}'. Apply a new migration instead of rewriting the applied checksum.");
                    await BackfillMigrationChecksumAsync(step.Id, checksum, cancellationToken);
                    existing.Checksum = checksum;
                }
                if (!string.Equals(existing.Checksum, checksum, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"Collaboration migration checksum drift detected for '{step.Id}'. Apply a new migration instead of rewriting the applied checksum.");
                if (step.Validate is not null)
                    await step.Validate(_dbContext.SqlSugar, cancellationToken);
                continue;
            }
            var sugar = _dbContext.SqlSugar;
            sugar.Ado.BeginTran();
            try
            {
                await step.Apply(sugar, cancellationToken);
                if (step.Validate is not null)
                    await step.Validate(sugar, cancellationToken);
                await sugar.Insertable(new CollaborationSchemaMigrationRecord { MigrationId = step.Id, Description = step.Description, Checksum = Checksum(step), AppliedOn = DateTimeOffset.UtcNow }).ExecuteCommandAsync(cancellationToken);
                sugar.Ado.CommitTran();
            }
            catch
            {
                sugar.Ado.RollbackTran();
                throw;
            }
        }
        return await InspectAsync(context, cancellationToken);
    }

    public Task<ServiceInitializationState> VerifyAsync(ServiceInitializationContext context, CancellationToken cancellationToken) => InspectAsync(context, cancellationToken);

    private async Task VerifyCriticalTablesAsync(CancellationToken cancellationToken)
    {
        await _dbContext.SqlSugar.Queryable<ConversationTable>().Take(1).ToListAsync(cancellationToken);
        await _dbContext.SqlSugar.Queryable<ConversationMemberTable>().Take(1).ToListAsync(cancellationToken);
        await _dbContext.SqlSugar.Queryable<MessageTable>().Take(1).ToListAsync(cancellationToken);
        await _dbContext.SqlSugar.Queryable<AttachmentTable>().Take(1).ToListAsync(cancellationToken);
        await _dbContext.SqlSugar.Queryable<DispositionTable>().Take(1).ToListAsync(cancellationToken);
        await _dbContext.SqlSugar.Queryable<LegalHoldTable>().Take(1).ToListAsync(cancellationToken);
        await _dbContext.SqlSugar.Queryable<ExportTable>().Take(1).ToListAsync(cancellationToken);
        await _dbContext.SqlSugar.Queryable<RetentionPolicyTable>().Take(1).ToListAsync(cancellationToken);
        await _dbContext.SqlSugar.Queryable<ComplianceCommandTable>().Take(1).ToListAsync(cancellationToken);
        await _dbContext.SqlSugar.Queryable<CompliancePreparationTable>().Take(1).ToListAsync(cancellationToken);
        await _dbContext.SqlSugar.Queryable<ComplianceViewBudgetTable>().Take(1).ToListAsync(cancellationToken);
        await _dbContext.SqlSugar.Queryable<CollaborationOutboxTable>().Take(1).ToListAsync(cancellationToken);
        await _dbContext.SqlSugar.Queryable<RetentionCheckpointTable>().Take(1).ToListAsync(cancellationToken);
        SchemaPhysicalDriftGuard.Validate(
            _dbContext.SqlSugar,
            "collaboration_conversation_member",
            ["id", "tenant_n_id", "conversation_n_id", "user_n_id", "visibility_state", "projection_version"],
            ["ix_collaboration_conversation_member_user"]);
    }

    private async Task EnsureMigrationChecksumColumnAsync(CancellationToken cancellationToken)
    {
        var dbType = _dbContext.SqlSugar.CurrentConnectionConfig.DbType;
        var columns = dbType == SqlSugar.DbType.Sqlite
            ? _dbContext.SqlSugar.Ado.GetDataTable("SELECT name FROM pragma_table_info('collaboration_schema_migrations')").Rows.Cast<System.Data.DataRow>().Select(row => row["name"]?.ToString()).Where(name => name is not null).Select(name => name!).ToHashSet(StringComparer.OrdinalIgnoreCase)
            : _dbContext.SqlSugar.Ado.GetDataTable("SELECT column_name FROM information_schema.columns WHERE table_schema = current_schema() AND table_name = 'collaboration_schema_migrations'").Rows.Cast<System.Data.DataRow>().Select(row => row["column_name"]?.ToString()).Where(name => name is not null).Select(name => name!).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!columns.Contains("checksum"))
            await _dbContext.SqlSugar.Ado.ExecuteCommandAsync("ALTER TABLE collaboration_schema_migrations ADD COLUMN checksum TEXT NULL", parameters: null, cancellationToken: cancellationToken);
    }

    private Task<int> BackfillMigrationChecksumAsync(string migrationId, string checksum, CancellationToken cancellationToken) =>
        _dbContext.SqlSugar.Ado.ExecuteCommandAsync(
            "UPDATE collaboration_schema_migrations SET checksum = @checksum WHERE migration_id = @migrationId AND checksum IS NULL",
            new SugarParameter[]
            {
                new("@checksum", checksum),
                new("@migrationId", migrationId),
            },
            cancellationToken: cancellationToken);

    private static string Checksum(CollaborationMigrationStep step) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{step.Id}|{step.Description}"))).ToLowerInvariant();

    private static bool IsMissingTable(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current.Message.Contains("no such table", StringComparison.OrdinalIgnoreCase)
                || (current.Message.Contains("relation", StringComparison.OrdinalIgnoreCase) && current.Message.Contains("does not exist", StringComparison.OrdinalIgnoreCase)))
                return true;
        }
        return false;
    }

    private static bool IsMissingLocalColumn(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            var message = current.Message;
            if (message.Contains("no such column", StringComparison.OrdinalIgnoreCase)
                || (message.Contains("column", StringComparison.OrdinalIgnoreCase)
                    && message.Contains("does not exist", StringComparison.OrdinalIgnoreCase)))
                return true;
        }
        return false;
    }
}
