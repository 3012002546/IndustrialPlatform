namespace IndustrialPlatform.Infrastructure.Caching;

/// <summary>
/// 缓存服务接口。
/// </summary>
public interface ICacheService
{
    /// <summary>写入缓存。</summary>
    Task SetAsync<T>(string key, T value, TimeSpan? expiry = null, CancellationToken cancellationToken = default);

    /// <summary>读取缓存,未命中返回默认值。</summary>
    Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default);

    /// <summary>删除缓存。</summary>
    Task RemoveAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>读取缓存,未命中时通过工厂回填并写入。</summary>
    Task<T?> GetOrAddAsync<T>(string key, Func<Task<T>> factory, TimeSpan? expiry = null, CancellationToken cancellationToken = default);
}
