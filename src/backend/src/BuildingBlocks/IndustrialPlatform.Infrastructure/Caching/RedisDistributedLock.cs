using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace IndustrialPlatform.Infrastructure.Caching;

/// <summary>
/// 基于 Redis SET NX EX 的分布式锁。
/// </summary>
public sealed class RedisDistributedLock : IDistributedLock
{
    private const string ReleaseScript =
        "if redis.call('get', KEYS[1]) == ARGV[1] then return redis.call('del', KEYS[1]) else return 0 end";

    private readonly IDatabase _database;

    /// <summary>
    /// 创建分布式锁。
    /// </summary>
    /// <param name="connection">Redis 连接。</param>
    /// <param name="options">Redis 配置。</param>
    public RedisDistributedLock(IConnectionMultiplexer connection, IOptions<RedisOptions> options)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(options);
        _database = connection.GetDatabase(options.Value.DefaultDatabase);
    }

    /// <inheritdoc/>
    public async Task<bool> TryAcquireAsync(string key, string token, TimeSpan expiry, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        return await _database.StringSetAsync(FullKey(key), token, expiry, When.NotExists);
    }

    /// <inheritdoc/>
    public async Task ReleaseAsync(string key, string token, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        await _database.ScriptEvaluateAsync(ReleaseScript, [FullKey(key)], [new RedisValue(token)]);
    }

    private static string FullKey(string key) => $"lock:{key}";
}
