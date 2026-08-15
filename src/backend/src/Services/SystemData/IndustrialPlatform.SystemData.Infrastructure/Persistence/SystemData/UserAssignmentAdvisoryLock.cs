using IndustrialPlatform.Infrastructure.Database;
using IndustrialPlatform.SystemData.Application.Assignments;
using SqlSugar;

namespace IndustrialPlatform.SystemData.Infrastructure.Persistence.SystemData;

/// <summary>
/// 按用户 advisory lock 实现(TASK-SD-005,05 方案 §7.4 并发控制)。
/// PostgreSQL:事务级 <c>pg_advisory_xact_lock</c>,锁随事务提交/回滚自动释放,
/// 关键区内的仓储写操作与锁同一事务(共享 SqlSugarScope 连接)。
/// SQLite 替身(本地开发无进程间并发):进程内按 (租户, 用户) 信号量串行化;
/// 提交/释放幂等,DisposeAsync 未提交时语义等价回滚。
/// </summary>
public sealed class UserAssignmentAdvisoryLock : IUserAssignmentAdvisoryLock
{
    private readonly SqlSugarDbContext _dbContext;

    /// <summary>初始化用户级 advisory lock。</summary>
    public UserAssignmentAdvisoryLock(SqlSugarDbContext dbContext)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        _dbContext = dbContext;
    }

    /// <inheritdoc />
    public async Task<IUserAssignmentLockHandle> AcquireAsync(
        string tenantNId,
        string userNId,
        CancellationToken cancellationToken)
    {
        var sugar = _dbContext.SqlSugar;
        if (sugar.CurrentConnectionConfig.DbType == DbType.PostgreSQL)
        {
            return await AcquirePostgreSqlAsync(sugar, tenantNId, userNId, cancellationToken);
        }

        return await AcquireSqliteAsync(tenantNId, userNId, cancellationToken);
    }

    /// <summary>PostgreSQL:开启事务并执行事务级 advisory lock,锁键为 (tenant, user) 哈希。</summary>
    private static async Task<IUserAssignmentLockHandle> AcquirePostgreSqlAsync(
        ISqlSugarClient sugar,
        string tenantNId,
        string userNId,
        CancellationToken cancellationToken)
    {
        sugar.Ado.BeginTran();
        try
        {
            await sugar.Ado.ExecuteCommandAsync(
                "SELECT pg_advisory_xact_lock(hashtextextended(@lockKey, 0))",
                new SugarParameter("@lockKey", $"{tenantNId}|{userNId}"));
            return new PostgreSqlLockHandle(sugar);
        }
        catch
        {
            sugar.Ado.RollbackTran();
            throw;
        }
    }

    /// <summary>SQLite 替身:进程内按用户信号量等待(串行化同一进程内的同用户关键区)。</summary>
    private static async Task<IUserAssignmentLockHandle> AcquireSqliteAsync(
        string tenantNId,
        string userNId,
        CancellationToken cancellationToken)
    {
        var semaphore = GetOrCreateSemaphore($"{tenantNId}|{userNId}");
        await semaphore.WaitAsync(cancellationToken);
        return new SqliteLockHandle(semaphore);
    }

    private static readonly object SemaphoreGate = new();
    private static readonly Dictionary<string, SemaphoreSlim> Semaphores = new();

    private static SemaphoreSlim GetOrCreateSemaphore(string key)
    {
        lock (SemaphoreGate)
        {
            if (!Semaphores.TryGetValue(key, out var semaphore))
            {
                semaphore = new SemaphoreSlim(1, 1);
                Semaphores[key] = semaphore;
            }

            return semaphore;
        }
    }

    /// <summary>PostgreSQL 句柄:CommitAsync 提交事务(释放 xact 锁),DisposeAsync 回滚;均幂等。</summary>
    private sealed class PostgreSqlLockHandle : IUserAssignmentLockHandle
    {
        private readonly ISqlSugarClient _sugar;
        private int _disposed;

        public PostgreSqlLockHandle(ISqlSugarClient sugar) => _sugar = sugar;

        public Task CommitAsync(CancellationToken cancellationToken)
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                _sugar.Ado.CommitTran();
            }

            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                _sugar.Ado.RollbackTran();
            }

            return ValueTask.CompletedTask;
        }
    }

    /// <summary>SQLite 替身句柄:提交/释放均释放信号量(幂等)。</summary>
    private sealed class SqliteLockHandle : IUserAssignmentLockHandle
    {
        private readonly SemaphoreSlim _semaphore;
        private int _released;

        public SqliteLockHandle(SemaphoreSlim semaphore) => _semaphore = semaphore;

        public Task CommitAsync(CancellationToken cancellationToken)
        {
            Release();
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            Release();
            return ValueTask.CompletedTask;
        }

        private void Release()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                _semaphore.Release();
            }
        }
    }
}
