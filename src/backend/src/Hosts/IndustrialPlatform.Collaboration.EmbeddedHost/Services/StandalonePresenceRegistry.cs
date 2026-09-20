using IndustrialPlatform.Collaboration.Application;
using IndustrialPlatform.Collaboration.Contracts;

namespace IndustrialPlatform.Collaboration.EmbeddedHost.Services;

/// <summary>Single-instance Standalone presence leases, scoped to this host process.</summary>
public sealed class StandalonePresenceRegistry : ICollaborationPresence
{
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromSeconds(60);
    private readonly Lock _gate = new();
    private readonly Dictionary<(string Tenant, string User, string Connection), PresenceLease> _leases = [];

    public PresenceDto SetPresence(string tenantNId, string userNId, string state, string? connectionNId = null)
    {
        var now = DateTimeOffset.UtcNow;
        lock (_gate)
        {
            _leases[(tenantNId, userNId, connectionNId ?? "http")] =
                new PresenceLease(connectionNId ?? "http", state, now.ToUnixTimeMilliseconds(), now, now.Add(LeaseDuration));
            return Project(tenantNId, userNId, now);
        }
    }

    public PresenceDto GetPresence(string tenantNId, string userNId)
    {
        lock (_gate)
            return Project(tenantNId, userNId, DateTimeOffset.UtcNow);
    }

    public void RemovePresence(string tenantNId, string userNId, string connectionNId)
    {
        lock (_gate)
            _leases.Remove((tenantNId, userNId, connectionNId));
    }

    private PresenceDto Project(string tenantNId, string userNId, DateTimeOffset now)
    {
        foreach (var key in _leases.Where(item => item.Value.ExpiresOn <= now).Select(item => item.Key).ToArray())
            _leases.Remove(key);
        return PresenceLeaseProjection.Project(tenantNId, userNId,
            _leases.Where(item => item.Key.Tenant == tenantNId && item.Key.User == userNId).Select(item => item.Value), now);
    }
}
