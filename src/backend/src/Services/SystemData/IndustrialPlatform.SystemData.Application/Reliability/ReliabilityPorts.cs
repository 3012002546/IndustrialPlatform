using System.Collections.Concurrent;
using IndustrialPlatform.SystemData.Application.ControlPlane;

namespace IndustrialPlatform.SystemData.Application.Reliability;

public sealed record ControlPlaneEvent(Guid EventId, string EventType, string EventVersion, string TenantNId, string Payload, DateTimeOffset CreatedOn);
public sealed record OutboxEventStatus(Guid EventId, int RetryCount, DateTimeOffset? PublishedOn, string? LastError, DateTimeOffset? NextAttemptOn = null, bool DeadLetter = false);

public interface IControlPlaneOutbox
{
    Task EnqueueAsync(ControlPlaneEvent item, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<ControlPlaneEvent>> GetPendingAsync(int limit, CancellationToken cancellationToken);
    Task MarkPublishedAsync(Guid eventId, CancellationToken cancellationToken);
    Task<bool> RecordFailureAsync(Guid eventId, string failureMessage, CancellationToken cancellationToken);
    Task<OutboxEventStatus?> GetStatusAsync(Guid eventId, CancellationToken cancellationToken);
}

public sealed class InMemoryControlPlaneOutbox : IControlPlaneOutbox
{
    private readonly ConcurrentDictionary<Guid, ControlPlaneEvent> _events = new();
    private readonly ConcurrentDictionary<Guid, OutboxEventStatus> _status = new();
    public Task EnqueueAsync(ControlPlaneEvent item, CancellationToken cancellationToken)
    {
        _events.TryAdd(item.EventId, item);
        _status.TryAdd(item.EventId, new OutboxEventStatus(item.EventId, 0, null, null));
        return Task.CompletedTask;
    }
    public Task<IReadOnlyCollection<ControlPlaneEvent>> GetPendingAsync(int limit, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var pending = _events.Values.Where(e => _status[e.EventId].PublishedOn is null && _status[e.EventId].RetryCount < 10 && (_status[e.EventId].NextAttemptOn is null || _status[e.EventId].NextAttemptOn <= now)).OrderBy(e => e.CreatedOn).Take(Math.Clamp(limit, 1, 100)).ToArray();
        return Task.FromResult<IReadOnlyCollection<ControlPlaneEvent>>(pending);
    }
    public Task MarkPublishedAsync(Guid eventId, CancellationToken cancellationToken) { if (_status.TryGetValue(eventId, out var status)) _status[eventId] = status with { PublishedOn = DateTimeOffset.UtcNow }; return Task.CompletedTask; }
    public Task<bool> RecordFailureAsync(Guid eventId, string failureMessage, CancellationToken cancellationToken) { if (_status.TryGetValue(eventId, out var status)) { var retry = status.RetryCount + 1; var dead = retry >= 10; var delaySeconds = Math.Min(Math.Pow(2, retry - 1), 900); _status[eventId] = status with { RetryCount = retry, LastError = failureMessage[..Math.Min(failureMessage.Length, 500)], NextAttemptOn = dead ? null : DateTimeOffset.UtcNow.AddSeconds(delaySeconds), DeadLetter = dead }; return Task.FromResult(dead); } return Task.FromResult(false); }
    public Task<OutboxEventStatus?> GetStatusAsync(Guid eventId, CancellationToken cancellationToken) => Task.FromResult(_status.TryGetValue(eventId, out var status) ? status : null);
}

public interface IIdentityPermissionRegistry
{
    Task<PermissionRegistrationReceipt?> VerifyAsync(PermissionManifestV1 manifest, CancellationToken cancellationToken);
}

public sealed record PermissionRegistrationReceipt(string ModuleNId, string ManifestVersion, string Checksum, bool Verified, DateTimeOffset VerifiedOn);

public sealed class UnavailableIdentityPermissionRegistry : IIdentityPermissionRegistry
{
    public Task<PermissionRegistrationReceipt?> VerifyAsync(PermissionManifestV1 manifest, CancellationToken cancellationToken) => Task.FromResult<PermissionRegistrationReceipt?>(null);
}

public interface IControlPlaneSnapshotCache
{
    string BuildKey(string tenantNId, string area, long revision);
    string BuildLatestKey(string tenantNId, string area);
    string BuildRevisionKey(string tenantNId, string area);
    Task SetAsync<T>(string tenantNId, string area, long revision, T value, CancellationToken cancellationToken);
    Task<T?> GetAsync<T>(string tenantNId, string area, long revision, CancellationToken cancellationToken);
    Task SetRevisionAsync(string tenantNId, string area, long revision, CancellationToken cancellationToken);
    Task RemoveRevisionAsync(string tenantNId, string area, CancellationToken cancellationToken);
    Task<long?> GetRevisionAsync(string tenantNId, string area, CancellationToken cancellationToken);
    Task SetLatestAsync<T>(string tenantNId, string area, long revision, T value, CancellationToken cancellationToken);
    Task<CachedControlPlaneSnapshot<T>?> GetLatestAsync<T>(string tenantNId, string area, CancellationToken cancellationToken);
}

public sealed record CachedControlPlaneSnapshot<T>(long Revision, DateTimeOffset GeneratedOn, T Value);

public static class SystemDataMetrics
{
    public static readonly System.Diagnostics.Metrics.Meter Meter = new("IndustrialPlatform.SystemData", "v1");
    public static readonly System.Diagnostics.Metrics.Counter<long> RuntimeCacheHits = Meter.CreateCounter<long>("systemdata_runtime_snapshot_cache_hit_total");
    public static readonly System.Diagnostics.Metrics.Counter<long> RuntimeDegraded = Meter.CreateCounter<long>("systemdata_runtime_snapshot_degraded_total");
    public static readonly System.Diagnostics.Metrics.Counter<long> IdentityFailures = Meter.CreateCounter<long>("systemdata_identity_directory_failures_total");
    public static readonly System.Diagnostics.Metrics.Counter<long> PermissionRegistryFailures = Meter.CreateCounter<long>("systemdata_permission_registry_failures_total");
    public static readonly System.Diagnostics.Metrics.Counter<long> OutboxRetries = Meter.CreateCounter<long>("systemdata_outbox_retry_total");
    public static readonly System.Diagnostics.Metrics.Counter<long> OutboxDeadLetters = Meter.CreateCounter<long>("systemdata_outbox_dead_total");
}

public interface IControlPlaneCacheBackend
{
    Task SetAsync<T>(string key, T value, TimeSpan expiry, CancellationToken cancellationToken);
    Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken);
    Task RemoveAsync(string key, CancellationToken cancellationToken);
}

public interface IControlPlaneDistributedLock
{
    Task<bool> TryAcquireAsync(string key, string token, TimeSpan expiry, CancellationToken cancellationToken);
    Task ReleaseAsync(string key, string token, CancellationToken cancellationToken);
}

public sealed class ControlPlaneSnapshotCache : IControlPlaneSnapshotCache
{
    private static readonly TimeSpan RevisionPointerTtl = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan SnapshotTtl = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan LatestSnapshotTtl = TimeSpan.FromMinutes(5);
    private readonly IControlPlaneCacheBackend _cache;
    public ControlPlaneSnapshotCache(IControlPlaneCacheBackend cache) => _cache = cache;
    public string BuildKey(string tenantNId, string area, long revision)
    {
        if (area.StartsWith("navigation:", StringComparison.OrdinalIgnoreCase))
            return $"systemdata:navigation:{area["navigation:".Length..]}:{tenantNId}:v{revision}";
        return $"systemdata:{area}:{tenantNId}:v{revision}";
    }
    public string BuildLatestKey(string tenantNId, string area) => $"systemdata:{area}:{tenantNId}:latest";
    public string BuildRevisionKey(string tenantNId, string area) => $"systemdata:revision:{tenantNId}:{area}";
    public Task SetAsync<T>(string tenantNId, string area, long revision, T value, CancellationToken cancellationToken) => _cache.SetAsync(BuildKey(tenantNId, area, revision), value, SnapshotTtl, cancellationToken);
    public Task<T?> GetAsync<T>(string tenantNId, string area, long revision, CancellationToken cancellationToken) => _cache.GetAsync<T>(BuildKey(tenantNId, area, revision), cancellationToken);
    public Task SetRevisionAsync(string tenantNId, string area, long revision, CancellationToken cancellationToken) => _cache.SetAsync(BuildRevisionKey(tenantNId, area), revision, RevisionPointerTtl, cancellationToken);
    public Task RemoveRevisionAsync(string tenantNId, string area, CancellationToken cancellationToken) => _cache.RemoveAsync(BuildRevisionKey(tenantNId, area), cancellationToken);
    public Task<long?> GetRevisionAsync(string tenantNId, string area, CancellationToken cancellationToken) => _cache.GetAsync<long?>(BuildRevisionKey(tenantNId, area), cancellationToken);
    public Task SetLatestAsync<T>(string tenantNId, string area, long revision, T value, CancellationToken cancellationToken) => _cache.SetAsync(BuildLatestKey(tenantNId, area), new CachedControlPlaneSnapshot<T>(revision, DateTimeOffset.UtcNow, value), LatestSnapshotTtl, cancellationToken);
    public Task<CachedControlPlaneSnapshot<T>?> GetLatestAsync<T>(string tenantNId, string area, CancellationToken cancellationToken) => _cache.GetAsync<CachedControlPlaneSnapshot<T>>(BuildLatestKey(tenantNId, area), cancellationToken);
}
