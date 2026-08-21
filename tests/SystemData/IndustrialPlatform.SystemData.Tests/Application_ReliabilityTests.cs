using IndustrialPlatform.SystemData.Application.Reliability;
using IndustrialPlatform.SystemData.Application.ControlPlane;
using IndustrialPlatform.SharedKernel.Exceptions;

namespace IndustrialPlatform.SystemData.Tests;

public sealed class ReliabilityTests
{
    private static readonly string[] NodeNames = ["node-a"];
    [Fact]
    public async Task Outbox_enqueue_is_idempotent_and_failed_event_is_retried()
    {
        var outbox = new InMemoryControlPlaneOutbox();
        var @event = new ControlPlaneEvent(Guid.NewGuid(), "SystemData.Test.v1", "v1", "tenant-a", "{}", DateTimeOffset.UtcNow);
        await outbox.EnqueueAsync(@event, CancellationToken.None);
        await outbox.EnqueueAsync(@event, CancellationToken.None);
        await outbox.RecordFailureAsync(@event.EventId, "temporary failure", CancellationToken.None);

        await Task.Delay(TimeSpan.FromSeconds(1.1));
        Assert.Single(await outbox.GetPendingAsync(10, CancellationToken.None));
        await outbox.MarkPublishedAsync(@event.EventId, CancellationToken.None);
        Assert.Empty(await outbox.GetPendingAsync(10, CancellationToken.None));
    }

    [Fact]
    public async Task Outbox_failure_uses_backoff_and_becomes_dead_letter_after_the_limit()
    {
        var outbox = new InMemoryControlPlaneOutbox();
        var @event = new ControlPlaneEvent(Guid.NewGuid(), "SystemData.Test.v1", "v1", "tenant-a", "{}", DateTimeOffset.UtcNow);
        await outbox.EnqueueAsync(@event, CancellationToken.None);

        Assert.False(await outbox.RecordFailureAsync(@event.EventId, "temporary", CancellationToken.None));
        Assert.Empty(await outbox.GetPendingAsync(10, CancellationToken.None));
        var dead = false;
        for (var attempt = 1; attempt < 10; attempt++)
        {
            dead = await outbox.RecordFailureAsync(@event.EventId, "temporary", CancellationToken.None);
        }

        Assert.True(dead);
        Assert.Empty(await outbox.GetPendingAsync(10, CancellationToken.None));
    }

    [Fact]
    public async Task Snapshot_cache_uses_versioned_tenant_key()
    {
        var backend = new TestCacheBackend();
        var cache = new ControlPlaneSnapshotCache(backend);
        await cache.SetAsync("tenant-a", "navigation", 4, NodeNames, CancellationToken.None);

        Assert.Equal(NodeNames, await cache.GetAsync<string[]>("tenant-a", "navigation", 4, CancellationToken.None));
        Assert.Null(await cache.GetAsync<string[]>("tenant-a", "navigation", 3, CancellationToken.None));
        Assert.Equal("systemdata:navigation:tenant-a:v4", cache.BuildKey("tenant-a", "navigation", 4));
    }

    [Fact]
    public async Task Snapshot_cache_uses_a_thirty_second_revision_pointer()
    {
        var backend = new TestCacheBackend();
        var cache = new ControlPlaneSnapshotCache(backend);

        await cache.SetRevisionAsync("tenant-a", "navigation", 4, CancellationToken.None);

        Assert.Equal(4, await cache.GetRevisionAsync("tenant-a", "navigation", CancellationToken.None));
        Assert.Equal("systemdata:revision:tenant-a:navigation", cache.BuildRevisionKey("tenant-a", "navigation"));
        Assert.Equal(TimeSpan.FromSeconds(30), backend.Expiry[cache.BuildRevisionKey("tenant-a", "navigation")]);
    }

    [Fact]
    public async Task Navigation_cache_key_contains_terminal_and_degraded_snapshot_is_five_minutes()
    {
        var backend = new TestCacheBackend();
        var cache = new ControlPlaneSnapshotCache(backend);
        Assert.Equal("systemdata:navigation:pc:tenant-a:v4", cache.BuildKey("tenant-a", "navigation:pc", 4));
        await cache.SetLatestAsync("tenant-a", "navigation:pc", 4, "snapshot", CancellationToken.None);
        Assert.Equal(TimeSpan.FromMinutes(5), backend.Expiry[cache.BuildLatestKey("tenant-a", "navigation:pc")]);
    }

    [Fact]
    public async Task Runtime_loader_uses_versioned_hit_then_recent_degraded_cache_and_rejects_expired_cache()
    {
        var cache = new SnapshotCacheFake();
        var store = new RuntimeStore(7);
        var loader = new RuntimeSnapshotLoader(store, cache);
        await cache.SetAsync("tenant-a", "features", 7, "cached", CancellationToken.None);
        var hit = await loader.LoadAsync("tenant-a", "features", _ => "database", CancellationToken.None);
        Assert.Equal("cached", hit.Value);
        Assert.False(hit.Degraded);

        var failingLoader = new RuntimeSnapshotLoader(new RuntimeStoreFailure(), cache);
        await cache.SetLatestAsync("tenant-a", "features", 7, "recent", CancellationToken.None);
        var degraded = await failingLoader.LoadAsync("tenant-a", "features", _ => "unreachable", CancellationToken.None);
        Assert.Equal("recent", degraded.Value);
        Assert.True(degraded.Degraded);

        cache.Latest = new CachedControlPlaneSnapshot<string>(7, DateTimeOffset.UtcNow.AddMinutes(-6), "expired");
        await Assert.ThrowsAsync<ValidationException>(() => failingLoader.LoadAsync("tenant-a", "features", _ => "unreachable", CancellationToken.None));
    }

    private sealed class RuntimeStore(long revision) : IControlPlaneStore
    {
        public Task<ControlPlaneSnapshot> LoadAsync(string tenantNId, CancellationToken cancellationToken) => Task.FromResult(ControlPlaneSnapshot.Empty(tenantNId) with { Revision = revision });
        public Task<long> CommitAsync(ControlPlaneSnapshot snapshot, long expectedRevision, ControlPlaneCommit commit, CancellationToken cancellationToken) => Task.FromResult(expectedRevision + 1);
        public Task<bool> SeedAppliedAsync(string tenantNId, string seedKey, string seedVersion, string checksum, CancellationToken cancellationToken) => Task.FromResult(false);
    }

    private sealed class RuntimeStoreFailure : IControlPlaneStore
    {
        public Task<ControlPlaneSnapshot> LoadAsync(string tenantNId, CancellationToken cancellationToken) => Task.FromException<ControlPlaneSnapshot>(new InvalidOperationException("database down"));
        public Task<long> CommitAsync(ControlPlaneSnapshot snapshot, long expectedRevision, ControlPlaneCommit commit, CancellationToken cancellationToken) => Task.FromResult(expectedRevision + 1);
        public Task<bool> SeedAppliedAsync(string tenantNId, string seedKey, string seedVersion, string checksum, CancellationToken cancellationToken) => Task.FromResult(false);
    }

    private sealed class SnapshotCacheFake : IControlPlaneSnapshotCache
    {
        private readonly Dictionary<string, object> _values = new(StringComparer.Ordinal);
        public CachedControlPlaneSnapshot<string>? Latest { get; set; }
        public string BuildKey(string tenantNId, string area, long revision) => $"systemdata:{area}:{tenantNId}:v{revision}";
        public string BuildLatestKey(string tenantNId, string area) => $"systemdata:{area}:{tenantNId}:latest";
        public string BuildRevisionKey(string tenantNId, string area) => $"systemdata:revision:{tenantNId}:{area}";
        public Task SetAsync<T>(string tenantNId, string area, long revision, T value, CancellationToken cancellationToken) { _values[BuildKey(tenantNId, area, revision)] = value!; return Task.CompletedTask; }
        public Task<T?> GetAsync<T>(string tenantNId, string area, long revision, CancellationToken cancellationToken) => Task.FromResult(_values.TryGetValue(BuildKey(tenantNId, area, revision), out var value) ? (T)value : default);
        public Task SetRevisionAsync(string tenantNId, string area, long revision, CancellationToken cancellationToken) { _values[BuildRevisionKey(tenantNId, area)] = revision; return Task.CompletedTask; }
        public Task RemoveRevisionAsync(string tenantNId, string area, CancellationToken cancellationToken) { _values.Remove(BuildRevisionKey(tenantNId, area)); return Task.CompletedTask; }
        public Task<long?> GetRevisionAsync(string tenantNId, string area, CancellationToken cancellationToken) => Task.FromResult(_values.TryGetValue(BuildRevisionKey(tenantNId, area), out var value) ? (long?)value : null);
        public Task SetLatestAsync<T>(string tenantNId, string area, long revision, T value, CancellationToken cancellationToken) { Latest = new CachedControlPlaneSnapshot<string>(revision, DateTimeOffset.UtcNow, (string)(object)value!); return Task.CompletedTask; }
        public Task<CachedControlPlaneSnapshot<T>?> GetLatestAsync<T>(string tenantNId, string area, CancellationToken cancellationToken) => Task.FromResult(Latest is null ? null : (CachedControlPlaneSnapshot<T>?)(object)Latest);
    }

    private sealed class TestCacheBackend : IControlPlaneCacheBackend
    {
        private readonly Dictionary<string, object> _values = new(StringComparer.Ordinal);
        public Dictionary<string, TimeSpan> Expiry { get; } = new(StringComparer.Ordinal);
        public Task SetAsync<T>(string key, T value, TimeSpan expiry, CancellationToken cancellationToken) { _values[key] = value!; Expiry[key] = expiry; return Task.CompletedTask; }
        public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken) => Task.FromResult(_values.TryGetValue(key, out var value) ? (T)value : default);
        public Task RemoveAsync(string key, CancellationToken cancellationToken) { _values.Remove(key); return Task.CompletedTask; }
    }
}
