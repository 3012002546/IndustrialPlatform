using IndustrialPlatform.Infrastructure.Database;
using IndustrialPlatform.SystemData.Application.Assignments;
using SqlSugar;
using System.Security.Cryptography;
using System.Text;

namespace IndustrialPlatform.SystemData.Infrastructure.Persistence.SystemData;

/// <summary>
/// 按用户 advisory lock 实现(TASK-SD-005,05 方案 §7.4 并发控制)。
/// PostgreSQL:事务级 <c>pg_advisory_xact_lock</c>,锁随事务提交/回滚自动释放,
/// 关键区内的仓储写操作与锁同一事务(共享 SqlSugarScope 连接)。
/// SQL Server:事务级 sp_getapplock,资源为稳定的 (租户,用户) SHA-256 键;
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
        return sugar.CurrentConnectionConfig.DbType switch
        {
            DbType.PostgreSQL => await AcquirePostgreSqlAsync(sugar, tenantNId, userNId, cancellationToken),
            DbType.SqlServer => await AcquireSqlServerAsync(sugar, tenantNId, userNId, cancellationToken),
            DbType.Sqlite => await AcquireSqliteAsync(sugar, tenantNId, userNId, cancellationToken),
            var dbType => throw new NotSupportedException($"用户分配锁不支持的数据库类型:{dbType}。"),
        };
    }

    private static async Task<IUserAssignmentLockHandle> AcquireSqlServerAsync(
        ISqlSugarClient sugar,
        string tenantNId,
        string userNId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var input = $"{tenantNId.Length}:{tenantNId}{userNId.Length}:{userNId}";
        var resource = "industrial-platform:user-assignment:"
            + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(input)));
        sugar.Ado.BeginTran();
        try
        {
            var result = await sugar.Ado.GetIntAsync("""
                DECLARE @result int;
                EXEC @result = sys.sp_getapplock
                    @Resource = @lockKey,
                    @LockMode = 'Exclusive',
                    @LockOwner = 'Transaction',
                    @LockTimeout = 10000;
                SELECT @result;
                """, new SugarParameter("@lockKey", resource));
            if (result < 0)
                throw new InvalidOperationException($"SQL Server 用户分配锁获取失败，返回码 {result}。");
            return new TransactionLockHandle(sugar);
        }
        catch
        {
            sugar.Ado.RollbackTran();
            throw;
        }
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
            return new TransactionLockHandle(sugar);
        }
        catch
        {
            sugar.Ado.RollbackTran();
            throw;
        }
    }

    /// <summary>SQLite 替身:进程内按用户信号量等待(串行化同一进程内的同用户关键区)。</summary>
    private static async Task<IUserAssignmentLockHandle> AcquireSqliteAsync(
        ISqlSugarClient sugar,
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

    /// <summary>事务级锁句柄:CommitAsync 提交事务并释放锁,DisposeAsync 回滚;均幂等。</summary>
    private sealed class TransactionLockHandle : IUserAssignmentLockHandle
    {
        private readonly ISqlSugarClient _sugar;
        private int _disposed;

        public TransactionLockHandle(ISqlSugarClient sugar) => _sugar = sugar;

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
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                _semaphore.Release();
            }
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
