using IndustrialPlatform.Infrastructure.Database;
using IndustrialPlatform.SystemData.Application.Auditing;

namespace IndustrialPlatform.SystemData.Infrastructure.Reliability;

/// <summary>组织/岗位写入、操作审计和 Audit Outbox 共享的 SqlSugar 事务。</summary>
public sealed class SqlSystemDataWriteTransaction : ISystemDataWriteTransaction
{
    private readonly SqlSugarDbContext _dbContext;

    public SqlSystemDataWriteTransaction(SqlSugarDbContext dbContext) => _dbContext = dbContext;

    public async Task ExecuteAsync(Func<Task> action, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(action);
        var sugar = _dbContext.SqlSugar;
        if (!sugar.Ado.IsNoTran())
        {
            await action();
            return;
        }
        sugar.Ado.BeginTran();
        try
        {
            await action();
            sugar.Ado.CommitTran();
        }
        catch
        {
            sugar.Ado.RollbackTran();
            throw;
        }
    }
}
