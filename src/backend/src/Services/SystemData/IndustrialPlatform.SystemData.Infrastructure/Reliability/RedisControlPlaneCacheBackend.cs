using IndustrialPlatform.Infrastructure.Caching;
using IndustrialPlatform.SystemData.Application.Reliability;

namespace IndustrialPlatform.SystemData.Infrastructure.Reliability;

public sealed class RedisControlPlaneCacheBackend : IControlPlaneCacheBackend
{
    private readonly ICacheService _cache;
    public RedisControlPlaneCacheBackend(ICacheService cache) => _cache = cache;
    public Task SetAsync<T>(string key, T value, TimeSpan expiry, CancellationToken cancellationToken) => _cache.SetAsync(key, value, expiry, cancellationToken);
    public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken) => _cache.GetAsync<T>(key, cancellationToken);
    public Task RemoveAsync(string key, CancellationToken cancellationToken) => _cache.RemoveAsync(key, cancellationToken);
}
