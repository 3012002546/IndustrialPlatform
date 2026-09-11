using IndustrialPlatform.Infrastructure.Database;
using Microsoft.Extensions.Logging;
using SqlSugar;
using System.Security.Cryptography;
using System.Text;

namespace IndustrialPlatform.SystemData.Infrastructure.Persistence.Migrations;

/// <summary>
/// 基于 SqlSugar 的 SystemData 库迁移运行器:确保迁移账本存在,按标识排序应用未记录步骤,
/// 每个步骤在独立事务内执行并记账,失败回滚且不记账。
/// </summary>
public sealed class SchemaMigrationRunner : ISchemaMigrationRunner
{
    private static readonly Action<ILogger, string, string, Exception?> MigrationApplied =
        LoggerMessage.Define<string, string>(
            LogLevel.Information,
            new EventId(1, nameof(MigrationApplied)),
            "SystemData 库迁移已应用 {MigrationId}:{Description}");

    private readonly SqlSugarDbContext _dbContext;
    private readonly SchemaMigrationStep[] _steps;
    private readonly ILogger<SchemaMigrationRunner> _logger;

    /// <summary>
    /// 初始化迁移运行器。
    /// </summary>
    /// <param name="dbContext">SqlSugar 数据库上下文。</param>
    /// <param name="steps">全部迁移步骤;TASK-SD-001 阶段为空,真实表步骤由 TASK-SD-002+ 注册。</param>
    /// <param name="logger">运行日志。</param>
    public SchemaMigrationRunner(SqlSugarDbContext dbContext, IEnumerable<SchemaMigrationStep> steps, ILogger<SchemaMigrationRunner> logger)
    {
        _dbContext = dbContext;
        _steps = steps.ToArray();
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task ApplyPendingAsync(CancellationToken cancellationToken = default)
    {
        var sugar = _dbContext.SqlSugar;
        sugar.CodeFirst.InitTables<SchemaMigrationRecord>();
        await EnsureChecksumColumnAsync(cancellationToken);

        var applied = (await sugar.Queryable<SchemaMigrationRecord>().ToListAsync(cancellationToken))
            .ToDictionary(record => record.MigrationId, StringComparer.Ordinal);

        foreach (var step in _steps.OrderBy(step => step.Id, StringComparer.Ordinal))
        {
            if (!applied.TryGetValue(step.Id, out var record))
            {
                await ApplyStepAsync(step, cancellationToken);
                continue;
            }
            var checksum = Checksum(step);
            if (record.Checksum is null)
            {
                if (!string.Equals(record.Description, step.Description, StringComparison.Ordinal))
                    throw new InvalidOperationException($"SystemData migration description drift detected for '{step.Id}'. Apply a new migration instead of rewriting the applied checksum.");
                await BackfillChecksumAsync(step.Id, checksum, cancellationToken);
                record.Checksum = checksum;
            }
            else if (!string.Equals(record.Checksum, checksum, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"SystemData migration checksum drift detected for '{step.Id}'. Apply a new migration instead of rewriting the applied checksum.");
            if (step.Validate is not null)
                await step.Validate(sugar, cancellationToken);
        }
    }

    private async Task ApplyStepAsync(SchemaMigrationStep step, CancellationToken cancellationToken)
    {
        var sugar = _dbContext.SqlSugar;
        sugar.Ado.BeginTran();
        try
        {
            await step.Apply(sugar, cancellationToken);
            await sugar.Insertable(new SchemaMigrationRecord
            {
                MigrationId = step.Id,
                Description = step.Description,
                Checksum = Checksum(step),
                AppliedOn = DateTimeOffset.UtcNow,
            }).ExecuteCommandAsync(cancellationToken);
            sugar.Ado.CommitTran();

            MigrationApplied(_logger, step.Id, step.Description, null);
        }
        catch
        {
            sugar.Ado.RollbackTran();
            throw;
        }
    }

    private async Task EnsureChecksumColumnAsync(CancellationToken cancellationToken)
    {
        var dbType = _dbContext.SqlSugar.CurrentConnectionConfig.DbType;
        var columns = dbType == DbType.Sqlite
            ? _dbContext.SqlSugar.Ado.GetDataTable("SELECT name FROM pragma_table_info('system_data_schema_migrations')").Rows.Cast<System.Data.DataRow>().Select(row => row["name"]?.ToString()).Where(name => name is not null).Select(name => name!).ToHashSet(StringComparer.OrdinalIgnoreCase)
            : _dbContext.SqlSugar.Ado.GetDataTable("SELECT column_name FROM information_schema.columns WHERE table_schema = current_schema() AND table_name = 'system_data_schema_migrations'").Rows.Cast<System.Data.DataRow>().Select(row => row["column_name"]?.ToString()).Where(name => name is not null).Select(name => name!).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!columns.Contains("checksum"))
            await _dbContext.SqlSugar.Ado.ExecuteCommandAsync("ALTER TABLE system_data_schema_migrations ADD COLUMN checksum TEXT NULL", parameters: null, cancellationToken: cancellationToken);
    }

    private Task<int> BackfillChecksumAsync(string migrationId, string checksum, CancellationToken cancellationToken) =>
        _dbContext.SqlSugar.Ado.ExecuteCommandAsync(
            "UPDATE system_data_schema_migrations SET checksum = @checksum WHERE migration_id = @migrationId AND checksum IS NULL",
            new SugarParameter[]
            {
                new("@checksum", checksum),
                new("@migrationId", migrationId),
            },
            cancellationToken: cancellationToken);

    private static string Checksum(SchemaMigrationStep step) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{step.Id}|{step.Description}"))).ToLowerInvariant();
}
