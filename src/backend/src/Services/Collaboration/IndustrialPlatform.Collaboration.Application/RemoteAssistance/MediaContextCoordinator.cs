namespace IndustrialPlatform.Collaboration.Application.RemoteAssistance;

/// <summary>Bounds authenticated media-context coordination and reclaims idle keys.</summary>
public sealed class MediaContextCoordinator
{
    private const int MaxContexts = 256;
    private const int MaxWaitersPerContext = 32;
    private readonly object _gate = new();
    private readonly Dictionary<ContextKey, Entry> _entries = new();

    public async Task<Lease> AcquireAsync(string tenantNId, string conversationNId, CancellationToken cancellationToken)
    {
        var key = new ContextKey(tenantNId, conversationNId);
        Entry entry;
        lock (_gate)
        {
            if (!_entries.TryGetValue(key, out entry!))
            {
                if (_entries.Count >= MaxContexts)
                    throw new MediaContextException("MEDIA_BUSY");
                entry = new Entry();
                _entries.Add(key, entry);
            }
            if (entry.Waiters >= MaxWaitersPerContext)
                throw new MediaContextException("MEDIA_BUSY");
            entry.Waiters++;
        }

        try
        {
            if (!await entry.Semaphore.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken))
                throw new MediaContextException("MEDIA_BUSY");
            return new Lease(this, key, entry);
        }
        catch
        {
            ReleaseWaiter(key, entry);
            throw;
        }
    }

    private void Release(ContextKey key, Entry entry)
    {
        entry.Semaphore.Release();
        ReleaseWaiter(key, entry);
    }

    private void ReleaseWaiter(ContextKey key, Entry entry)
    {
        lock (_gate)
        {
            if (--entry.Waiters == 0 && _entries.TryGetValue(key, out var current) && ReferenceEquals(current, entry))
            {
                _entries.Remove(key);
                entry.Semaphore.Dispose();
            }
        }
    }

    internal sealed class Entry
    {
        public SemaphoreSlim Semaphore { get; } = new(1, 1);
        public int Waiters { get; set; }
    }

    public sealed class Lease : IDisposable
    {
        private readonly MediaContextCoordinator _owner;
        private readonly ContextKey _key;
        private readonly Entry _entry;
        private int _released;

        internal Lease(MediaContextCoordinator owner, ContextKey key, Entry entry)
        {
            _owner = owner;
            _key = key;
            _entry = entry;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
                _owner.Release(_key, _entry);
        }
    }

    internal readonly record struct ContextKey(string TenantNId, string ConversationNId);
}
