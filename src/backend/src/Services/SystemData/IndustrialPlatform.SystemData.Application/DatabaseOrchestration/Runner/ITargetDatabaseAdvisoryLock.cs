namespace IndustrialPlatform.SystemData.Application.DatabaseOrchestration.Runner;

/// <summary>
/// 同物理目标 advisory lock 端口(05 方案 §7.1.5)。provision/migrate/verify 关键区获取,
/// 多副本并发时仅一个同物理目标 Operation 可进入;锁等待受 Operation 超时控制。
/// 释放句柄 Disposed 时解锁;获取超时/不可用返回 <c>null</c>。
/// </summary>
public interface ITargetDatabaseAdvisoryLock
{
    /// <summary>获取目标 advisory lock(可等待)。</summary>
    /// <param name="key">同物理目标锁键。</param>
    /// <param name="connection">用于持锁的目标连接(migrator 优先,provision 阶段用 admin)。</param>
    /// <param name="timeout">锁等待超时。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>成功返回释放句柄(Dispose 解锁),超时返回 <c>null</c>。</returns>
    Task<IDisposable?> AcquireAsync(
        DatabaseTargetLockKey key,
        TargetDatabaseConnection connection,
        TimeSpan timeout,
        CancellationToken cancellationToken);
}
