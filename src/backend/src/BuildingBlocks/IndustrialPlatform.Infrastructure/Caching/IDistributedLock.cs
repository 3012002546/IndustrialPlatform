namespace IndustrialPlatform.Infrastructure.Caching;

/// <summary>
/// 分布式锁接口。
/// </summary>
public interface IDistributedLock
{
    /// <summary>尝试获取锁,成功返回 true。</summary>
    Task<bool> TryAcquireAsync(string key, string token, TimeSpan expiry, CancellationToken cancellationToken = default);

    /// <summary>释放锁,仅当 token 与持有者匹配时删除。</summary>
    Task ReleaseAsync(string key, string token, CancellationToken cancellationToken = default);
}
