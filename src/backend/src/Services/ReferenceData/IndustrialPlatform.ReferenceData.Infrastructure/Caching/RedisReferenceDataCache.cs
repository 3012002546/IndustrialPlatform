using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IndustrialPlatform.Infrastructure.Database;
using IndustrialPlatform.ReferenceData.Application.Caching;
using IndustrialPlatform.ReferenceData.Infrastructure.Outbox;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace IndustrialPlatform.ReferenceData.Infrastructure.Caching;

/// <summary>Shared Redis cache-aside adapter for every ReferenceData logical module.</summary>
public sealed partial class RedisReferenceDataCache : IReferenceDataCache
{
    private const string ConditionalSetScript = """
        local current = redis.call('GET', KEYS[1])
        if not current then
            redis.call('SET', KEYS[1], ARGV[1])
            current = ARGV[1]
        end
        if current ~= ARGV[1] then return 0 end
        redis.call('SET', KEYS[2], ARGV[2], 'PX', ARGV[3])
        return 1
        """;
    private const string AuthoritativeSetScript = """
        redis.call('SET', KEYS[1], ARGV[1])
        redis.call('SET', KEYS[2], ARGV[2], 'PX', ARGV[3])
        return 1
        """;
    private readonly IConnectionMultiplexer connection;
    private readonly IDatabase database;
    private readonly ILogger<RedisReferenceDataCache> logger;
    private readonly SqlSugarDbContext? authoritativeDatabase;
    private readonly ConcurrentDictionary<string, byte> initializedModules = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> moduleGates = new(StringComparer.Ordinal);

    internal RedisReferenceDataCache(IConnectionMultiplexer connection,
        IOptions<IndustrialPlatform.Infrastructure.Caching.RedisOptions> options,
        ILogger<RedisReferenceDataCache> logger) : this(connection, options, logger, null, true)
    {
    }

    public RedisReferenceDataCache(IConnectionMultiplexer connection,
        IOptions<IndustrialPlatform.Infrastructure.Caching.RedisOptions> options,
        ILogger<RedisReferenceDataCache> logger,
        SqlSugarDbContext authoritativeDatabase) : this(connection, options, logger,
            authoritativeDatabase ?? throw new ArgumentNullException(nameof(authoritativeDatabase)), false)
    {
    }

    private RedisReferenceDataCache(IConnectionMultiplexer connection,
        IOptions<IndustrialPlatform.Infrastructure.Caching.RedisOptions> options,
        ILogger<RedisReferenceDataCache> logger,
        SqlSugarDbContext? authoritativeDatabase,
        bool redisOnly)
    {
        if (!redisOnly && authoritativeDatabase is null)
            throw new ArgumentNullException(nameof(authoritativeDatabase));
        this.connection = connection;
        database = connection.GetDatabase(options.Value.DefaultDatabase);
        this.logger = logger;
        this.authoritativeDatabase = authoritativeDatabase;
        connection.ConnectionFailed += ClearInitializedModules;
        connection.ConnectionRestored += ClearInitializedModules;
    }

    public async Task<T?> GetAsync<T>(ReferenceDataCacheKey key,
        CancellationToken cancellationToken = default) where T : class
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!connection.IsConnected)
        {
            FallbackDisconnected(key.Module, "read", key.Value);
            return null;
        }
        try
        {
            if (authoritativeDatabase is null)
                await EnsureModuleGenerationAsync(key.GenerationKey, cancellationToken);
            var value = await database.StringGetAsync(PhysicalValueKey(key));
            if (value.IsNullOrEmpty)
            {
                ReferenceDataMetrics.CacheMisses.Add(1,
                    new KeyValuePair<string, object?>("module", key.Module));
                return null;
            }

            var envelope = JsonSerializer.Deserialize<CacheEnvelope<T>>(value.ToString());
            var generation = authoritativeDatabase is null
                ? await GetGenerationCoreAsync(key.GenerationKey)
                : await ReferenceDataCacheGenerationStore.ReadAsync(
                    authoritativeDatabase, key.GenerationKey, cancellationToken);
            if (envelope?.Value is null || envelope.Generation != generation)
            {
                ReferenceDataMetrics.CacheMisses.Add(1,
                    new KeyValuePair<string, object?>("module", key.Module));
                return null;
            }

            ReferenceDataMetrics.CacheHits.Add(1,
                new KeyValuePair<string, object?>("module", key.Module));
            return envelope.Value;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            initializedModules.TryRemove(key.GenerationKey, out _);
            Fallback(key.Module, "read", key.Value, exception);
            return null;
        }
    }

    public async Task SetAsync<T>(ReferenceDataCacheKey key, T value, TimeSpan ttl,
        CancellationToken cancellationToken = default) where T : class
    {
        var generation = await GetGenerationAsync(key.GenerationKey, cancellationToken);
        await SetIfGenerationAsync(key, value, ttl, generation, cancellationToken);
    }

    public async Task<string?> GetGenerationAsync(string moduleKey,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            if (authoritativeDatabase is not null)
            {
                return await ReferenceDataCacheGenerationStore.ReadAsync(
                    authoritativeDatabase, moduleKey, cancellationToken);
            }
            if (!connection.IsConnected)
            {
                FallbackDisconnected(ModuleFromGenerationKey(moduleKey), "generation-read",
                    PhysicalGenerationKey(moduleKey));
                return null;
            }
            await EnsureModuleGenerationAsync(moduleKey, cancellationToken);
            return await GetGenerationCoreAsync(moduleKey);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            initializedModules.TryRemove(moduleKey, out _);
            Fallback(ModuleFromGenerationKey(moduleKey), "generation-read",
                PhysicalGenerationKey(moduleKey), exception);
            return null;
        }
    }

    public async Task<bool> SetIfGenerationAsync<T>(ReferenceDataCacheKey key, T value, TimeSpan ttl,
        string? expectedGeneration, CancellationToken cancellationToken = default) where T : class
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrEmpty(expectedGeneration)) return false;
        if (!connection.IsConnected)
        {
            FallbackDisconnected(key.Module, "conditional-write", key.Value);
            return false;
        }
        try
        {
            if (authoritativeDatabase is null)
                await EnsureModuleGenerationAsync(key.GenerationKey, cancellationToken);
            else
            {
                var authoritativeGeneration = await ReferenceDataCacheGenerationStore.ReadAsync(
                    authoritativeDatabase, key.GenerationKey, cancellationToken);
                if (authoritativeGeneration != expectedGeneration) return false;
            }
            var envelope = JsonSerializer.Serialize(new CacheEnvelope<T>(expectedGeneration, value));
            var script = authoritativeDatabase is null ? ConditionalSetScript : AuthoritativeSetScript;
            var result = await database.ScriptEvaluateAsync(script,
                [PhysicalGenerationKey(key.GenerationKey), PhysicalValueKey(key)],
                [expectedGeneration, envelope, Math.Max(1L, checked((long)ttl.TotalMilliseconds))]);
            return (long)result == 1;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            initializedModules.TryRemove(key.GenerationKey, out _);
            Fallback(key.Module, "conditional-write", key.Value, exception);
            return false;
        }
    }

    public async Task InvalidateAsync(ReferenceDataCachePattern pattern,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (authoritativeDatabase is not null)
            return;
        if (!connection.IsConnected)
        {
            FallbackDisconnected(pattern.Module, "invalidate", pattern.Value);
            return;
        }

        var gate = moduleGates.GetOrAdd(pattern.GenerationKey, static _ => new SemaphoreSlim(1, 1));
        var acquired = false;
        try
        {
            await gate.WaitAsync(cancellationToken);
            acquired = true;
            await database.StringIncrementAsync(PhysicalGenerationKey(pattern.GenerationKey));
            initializedModules.TryAdd(pattern.GenerationKey, 0);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            initializedModules.TryRemove(pattern.GenerationKey, out _);
            Fallback(pattern.Module, "invalidate", pattern.Value, exception);
        }
        finally
        {
            if (acquired) gate.Release();
        }
    }

    private void Fallback(string module, string operation, string key, Exception exception)
    {
        ReferenceDataMetrics.CacheFallbacks.Add(1,
            new KeyValuePair<string, object?>("module", module),
            new KeyValuePair<string, object?>("operation", operation));
        CacheUnavailable(logger, module, operation, key, exception);
    }

    private void FallbackDisconnected(string module, string operation, string key)
    {
        ReferenceDataMetrics.CacheFallbacks.Add(1,
            new KeyValuePair<string, object?>("module", module),
            new KeyValuePair<string, object?>("operation", operation));
        CacheDisconnected(logger, module, operation, key);
    }

    private async Task<string> GetGenerationCoreAsync(string moduleKey)
    {
        var value = await database.StringGetAsync(PhysicalGenerationKey(moduleKey));
        return value.IsNullOrEmpty ? "0" : value.ToString();
    }

    private async Task EnsureModuleGenerationAsync(string moduleKey, CancellationToken cancellationToken)
    {
        if (initializedModules.ContainsKey(moduleKey)) return;
        var gate = moduleGates.GetOrAdd(moduleKey, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (initializedModules.ContainsKey(moduleKey)) return;
            await database.StringIncrementAsync(PhysicalGenerationKey(moduleKey));
            initializedModules.TryAdd(moduleKey, 0);
        }
        finally
        {
            gate.Release();
        }
    }

    internal static string PhysicalGenerationKey(string moduleKey) =>
        $"referencedata:generation:v3:{{{SlotTag(moduleKey)}}}";

    internal static string PhysicalValueKey(ReferenceDataCacheKey key) =>
        $"referencedata:cache:v3:{{{SlotTag(key.GenerationKey)}}}:{key.Value}";

    private static string SlotTag(string moduleKey) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(moduleKey)))[..16];

    private static string ModuleFromGenerationKey(string generationKey)
    {
        var separator = generationKey.IndexOf('\u001f');
        return separator < 0 ? generationKey : generationKey[..separator];
    }

    private void ClearInitializedModules(object? sender, ConnectionFailedEventArgs args) =>
        initializedModules.Clear();

    private sealed record CacheEnvelope<T>(string Generation, T Value);

    [LoggerMessage(3301, LogLevel.Warning,
        "ReferenceData Redis cache unavailable; database remains authoritative. Module={Module} Operation={Operation} Key={Key}")]
    private static partial void CacheUnavailable(ILogger logger, string module, string operation, string key,
        Exception exception);

    [LoggerMessage(3302, LogLevel.Warning,
        "ReferenceData Redis cache disconnected; database remains authoritative. Module={Module} Operation={Operation} Key={Key}")]
    private static partial void CacheDisconnected(ILogger logger, string module, string operation, string key);
}
