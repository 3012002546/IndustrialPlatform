using IndustrialPlatform.Infrastructure.Database;
using IndustrialPlatform.SharedKernel.Interfaces;

namespace IndustrialPlatform.Infrastructure.Transaction;

/// <summary>
/// 基于 SqlSugar 事务的工作单元,负责提交事务。
/// </summary>
public sealed class SqlSugarUnitOfWork : IUnitOfWork
{
    private readonly SqlSugarDbContext _dbContext;

    /// <summary>
    /// 创建工作单元。
    /// </summary>
    /// <param name="dbContext">数据库上下文。</param>
    public SqlSugarUnitOfWork(SqlSugarDbContext dbContext)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        _dbContext = dbContext;
    }

    /// <inheritdoc/>
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var ado = _dbContext.SqlSugar.Ado;
        if (!ado.IsNoTran())
        {
            ado.CommitTran();
            return Task.FromResult(1);
        }

        return Task.FromResult(0);
    }
}
