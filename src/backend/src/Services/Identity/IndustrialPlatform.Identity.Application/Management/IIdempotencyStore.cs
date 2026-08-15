namespace IndustrialPlatform.Identity.Application.Management;

/// <summary>幂等键决策:Proceed 继续执行;Replay 为已成功请求的同内容重放,返回原语义(不重复执行)。</summary>
public enum IdempotencyDecision
{
    /// <summary>新键或未完成请求的重试,应继续执行。</summary>
    Proceed = 0,

    /// <summary>已完成请求的同内容重放,应返回幂等确认(不再执行)。</summary>
    Replay = 1,
}

/// <summary>
/// 写请求幂等存储端口(§29A.5):同一 (租户, 执行者, Idempotency-Key) 记录请求内容哈希。
/// 同键不同哈希抛 <see cref="IdempotencyConflictException"/>(409 ID_IDEMPOTENCY_CONFLICT);
/// 同键同哈希且已完成返回 <see cref="IdempotencyDecision.Replay"/>。实现由基础设施层提供。
/// </summary>
public interface IIdempotencyStore
{
    /// <summary>预留幂等键并返回决策;冲突抛 <see cref="IdempotencyConflictException"/>。</summary>
    Task<IdempotencyDecision> TryReserveAsync(
        string tenantNId,
        string actorUserNId,
        string idempotencyKey,
        string requestHash,
        CancellationToken cancellationToken = default);

    /// <summary>写操作成功后标记幂等键已完成(同内容重放返回 Replay)。</summary>
    Task MarkCompletedAsync(
        string tenantNId,
        string actorUserNId,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    /// <summary>写操作失败后释放预留(同键同哈希重试可再次执行)。</summary>
    Task ReleaseAsync(
        string tenantNId,
        string actorUserNId,
        string idempotencyKey,
        CancellationToken cancellationToken = default);
}
