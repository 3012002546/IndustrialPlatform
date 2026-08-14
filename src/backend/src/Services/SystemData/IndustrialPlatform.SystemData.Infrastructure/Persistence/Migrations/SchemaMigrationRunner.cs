using IndustrialPlatform.Infrastructure.Database;
using Microsoft.Extensions.Logging;
using SqlSugar;

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

        var appliedIds = await sugar
            .Queryable<SchemaMigrationRecord>()
            .Select(record => record.MigrationId)
            .ToListAsync(cancellationToken);
        var appliedSet = appliedIds.ToHashSet(StringComparer.Ordinal);

        foreach (var step in _steps.OrderBy(step => step.Id, StringComparer.Ordinal))
        {
            if (!appliedSet.Contains(step.Id))
            {
                await ApplyStepAsync(step, cancellationToken);
            }
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
}
