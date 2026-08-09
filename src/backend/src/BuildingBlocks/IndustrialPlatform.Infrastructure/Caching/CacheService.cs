using System.Text.Json;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace IndustrialPlatform.Infrastructure.Caching;

/// <summary>
/// 基于 Redis 的缓存实现,使用 System.Text.Json 序列化。
/// </summary>
public sealed class CacheService : ICacheService
{
    private readonly IDatabase _database;
    private readonly RedisOptions _options;

    /// <summary>
    /// 创建缓存服务。
    /// </summary>
    /// <param name="connection">Redis 连接。</param>
    /// <param name="options">Redis 配置。</param>
    public CacheService(IConnectionMultiplexer connection, IOptions<RedisOptions> options)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(options);
        _database = connection.GetDatabase(options.Value.DefaultDatabase);
        _options = options.Value;
    }

    /// <inheritdoc/>
    public async Task SetAsync<T>(string key, T value, TimeSpan? expiry = null, CancellationToken cancellationToken = default)
        => await _database.StringSetAsync(key, JsonSerializer.Serialize(value), expiry ?? _options.DefaultExpiry);

    /// <inheritdoc/>
    public async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        var value = await _database.StringGetAsync(key);
        return value.IsNullOrEmpty ? default : JsonSerializer.Deserialize<T>(value.ToString());
    }

    /// <inheritdoc/>
    public async Task RemoveAsync(string key, CancellationToken cancellationToken = default)
        => await _database.KeyDeleteAsync(key);

    /// <inheritdoc/>
    public async Task<T?> GetOrAddAsync<T>(string key, Func<Task<T>> factory, TimeSpan? expiry = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(factory);
        var cached = await _database.StringGetAsync(key);
        if (!cached.IsNullOrEmpty)
        {
            return JsonSerializer.Deserialize<T>(cached.ToString());
        }

        var value = await factory();
        await _database.StringSetAsync(key, JsonSerializer.Serialize(value), expiry ?? _options.DefaultExpiry);
        return value;
    }
}
