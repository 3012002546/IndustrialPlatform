namespace IndustrialPlatform.SystemData.Application.Assignments;

/// <summary>
/// 按用户 advisory lock 端口(TASK-SD-005,05 方案 §7.4 并发控制)。
/// 同一 (租户, 用户) 的任职编排关键区串行执行,防并发创建/主任职切换竞争。
/// PostgreSQL 实现使用事务级 <c>pg_advisory_xact_lock</c>;
/// SQLite 替身使用进程内按用户信号量(本地开发)。
/// </summary>
public interface IUserAssignmentAdvisoryLock
{
    /// <summary>获取用户级 advisory lock(可等待直到可用)。</summary>
    /// <param name="tenantNId">租户业务标识。</param>
    /// <param name="userNId">用户业务标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>释放句柄;DisposeAsync 未提交时回滚,<see cref="IUserAssignmentLockHandle.CommitAsync"/> 提交并释放。</returns>
    Task<IUserAssignmentLockHandle> AcquireAsync(string tenantNId, string userNId, CancellationToken cancellationToken);
}

/// <summary>
/// 用户级锁句柄:提交释放锁且持久化生效;未提交的 DisposeAsync 回滚(事务级锁自动失效)。
/// 实现必须保证幂等——提交/释放后再次调用无害。
/// </summary>
public interface IUserAssignmentLockHandle : IAsyncDisposable
{
    /// <summary>提交关键区(事务提交/信号量释放)。</summary>
    Task CommitAsync(CancellationToken cancellationToken);
}
