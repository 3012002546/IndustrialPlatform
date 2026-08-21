using IndustrialPlatform.SharedKernel.Exceptions;
using IndustrialPlatform.SystemData.Application.Reliability;
using System.Collections.Concurrent;

namespace IndustrialPlatform.SystemData.Application.ControlPlane;

public sealed record RuntimeSnapshotResult<T>(T Value, bool Degraded);

/// <summary>
/// 运行时只读快照读取器：数据库优先，版本化 Redis 命中优先于重新生成；
/// 数据库故障时只接受五分钟内的 latest 快照，Redis/数据库同时不可用则 fail-closed。
/// </summary>
public sealed class RuntimeSnapshotLoader
{
    private static readonly TimeSpan DegradedMaximumAge = TimeSpan.FromMinutes(5);
    private readonly IControlPlaneStore _store;
    private readonly IControlPlaneSnapshotCache? _cache;
    private readonly IControlPlaneDistributedLock? _distributedLock;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _missLocks = new(StringComparer.Ordinal);

    public RuntimeSnapshotLoader(IControlPlaneStore store, IControlPlaneSnapshotCache? cache = null, IControlPlaneDistributedLock? distributedLock = null)
    {
        _store = store;
        _cache = cache;
        _distributedLock = distributedLock;
    }

    public async Task<RuntimeSnapshotResult<T>> LoadAsync<T>(string tenantNId, string area, Func<ControlPlaneSnapshot, T> factory, CancellationToken cancellationToken)
    {
        if (_cache is not null)
        {
            try
            {
                var revision = await _cache.GetRevisionAsync(tenantNId, area, cancellationToken);
                if (revision is > 0)
                {
                    var cached = await _cache.GetAsync<T>(tenantNId, area, revision.Value, cancellationToken);
                    if (cached is not null)
                    {
                        SystemDataMetrics.RuntimeCacheHits.Add(1);
                        return new RuntimeSnapshotResult<T>(cached, false);
                    }
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // Redis is an optimization; database remains the authority.
            }
        }

        ControlPlaneSnapshot state;
        try
        {
            state = await _store.LoadAsync(tenantNId, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return await LoadDegradedAsync<T>(tenantNId, area, exception, cancellationToken);
        }

        if (_cache is not null)
        {
            try
            {
                var cached = await _cache.GetAsync<T>(tenantNId, area, state.Revision, cancellationToken);
                if (cached is not null) { SystemDataMetrics.RuntimeCacheHits.Add(1); return new RuntimeSnapshotResult<T>(cached, false); }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // Redis is an optimization; successful database reads remain available.
            }
        }

        var cacheKey = $"{tenantNId}:{area}:v{state.Revision}";
        var gate = _missLocks.GetOrAdd(cacheKey, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        var distributedKey = $"systemdata:snapshot:{tenantNId}:{area}:v{state.Revision}";
        var distributedToken = Guid.NewGuid().ToString("N");
        var distributedAcquired = false;
        try
        {
            if (_distributedLock is not null)
            {
                try
                {
                    distributedAcquired = await _distributedLock.TryAcquireAsync(
                        distributedKey,
                        distributedToken,
                        TimeSpan.FromSeconds(15),
                        cancellationToken);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    // A lock outage permits bounded local concurrency; it must not block a database-backed read.
                }
            }

            if (_cache is not null)
            {
                try
                {
                    var secondRead = await _cache.GetAsync<T>(tenantNId, area, state.Revision, cancellationToken);
                    if (secondRead is not null) { SystemDataMetrics.RuntimeCacheHits.Add(1); return new RuntimeSnapshotResult<T>(secondRead, false); }
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                }
            }

            var value = factory(state);
            if (_cache is not null)
            {
                try
                {
                    await _cache.SetAsync(tenantNId, area, state.Revision, value, cancellationToken);
                    await _cache.SetLatestAsync(tenantNId, area, state.Revision, value, cancellationToken);
                    await _cache.SetRevisionAsync(tenantNId, area, state.Revision, cancellationToken);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    // Do not fail a database-backed runtime read because Redis is unavailable.
                }
            }
            return new RuntimeSnapshotResult<T>(value, false);
        }
        finally
        {
            if (distributedAcquired && _distributedLock is not null)
            {
                try
                {
                    await _distributedLock.ReleaseAsync(distributedKey, distributedToken, CancellationToken.None);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    // Expiry remains the safety net when release cannot be confirmed.
                }
            }
            gate.Release();
        }
    }

    private async Task<RuntimeSnapshotResult<T>> LoadDegradedAsync<T>(string tenantNId, string area, Exception databaseException, CancellationToken cancellationToken)
    {
        if (_cache is not null)
        {
            try
            {
                var cached = await _cache.GetLatestAsync<T>(tenantNId, area, cancellationToken);
                if (cached is not null && DateTimeOffset.UtcNow - cached.GeneratedOn <= DegradedMaximumAge)
                {
                    SystemDataMetrics.RuntimeDegraded.Add(1);
                    return new RuntimeSnapshotResult<T>(cached.Value, true);
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // fall through to the stable 503 error
            }
        }
        throw new ValidationException("Runtime control-plane snapshot is unavailable.");
    }

    public async Task InvalidateAsync(string tenantNId, string area, CancellationToken cancellationToken)
    {
        if (_cache is null) return;
        try { await _cache.RemoveRevisionAsync(tenantNId, area, cancellationToken); }
        catch (Exception exception) when (exception is not OperationCanceledException) { }
    }
}
