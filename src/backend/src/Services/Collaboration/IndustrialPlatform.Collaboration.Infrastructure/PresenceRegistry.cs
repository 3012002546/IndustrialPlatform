using System.Text.Json;
using IndustrialPlatform.Collaboration.Application;
using IndustrialPlatform.Collaboration.Contracts;
using StackExchange.Redis;

namespace IndustrialPlatform.Collaboration.Infrastructure;

/// <summary>
/// Redis-backed connection leases. Each connection has its own TTL; renewing one
/// connection therefore cannot extend stale leases belonging to other replicas.
/// </summary>
public sealed class PresenceRegistry : ICollaborationPresence
{
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromSeconds(60);
    private readonly IConnectionMultiplexer _connection;

    public PresenceRegistry(IConnectionMultiplexer connection) => _connection = connection;

    public PresenceDto SetPresence(string tenantNId, string userNId, string state, string? connectionNId = null)
    {
        var now = DateTimeOffset.UtcNow;
        var lease = new PresenceLease(connectionNId ?? "http", state, now.ToUnixTimeMilliseconds(), now, now.Add(LeaseDuration));
        try
        {
            var database = _connection.GetDatabase();
            database.StringSetAsync(LeaseKey(tenantNId, userNId, lease.ConnectionNId), JsonSerializer.Serialize(lease), LeaseDuration).GetAwaiter().GetResult();
            var indexKey = ConnectionIndexKey(tenantNId, userNId);
            database.SetAddAsync(indexKey, lease.ConnectionNId).GetAwaiter().GetResult();
            database.KeyExpireAsync(indexKey, LeaseDuration + LeaseDuration).GetAwaiter().GetResult();
            return ReadProjection(tenantNId, userNId, now);
        }
        catch (RedisException)
        {
            return Unknown(userNId, now);
        }
    }

    public PresenceDto GetPresence(string tenantNId, string userNId) => ReadProjection(tenantNId, userNId, DateTimeOffset.UtcNow);

    public void RemovePresence(string tenantNId, string userNId, string connectionNId)
    {
        if (string.IsNullOrWhiteSpace(connectionNId))
            return;
        try
        {
            var database = _connection.GetDatabase();
            database.KeyDeleteAsync(LeaseKey(tenantNId, userNId, connectionNId)).GetAwaiter().GetResult();
            database.SetRemoveAsync(ConnectionIndexKey(tenantNId, userNId), connectionNId).GetAwaiter().GetResult();
        }
        catch (RedisException)
        {
            // A disconnect is best effort; the per-connection lease will expire naturally.
        }
    }

    private PresenceDto ReadProjection(string tenantNId, string userNId, DateTimeOffset now)
    {
        try
        {
            var database = _connection.GetDatabase();
            var indexKey = ConnectionIndexKey(tenantNId, userNId);
            var connections = database.SetMembersAsync(indexKey).GetAwaiter().GetResult();
            var leases = new List<PresenceLease>(connections.Length);
            foreach (var connection in connections)
            {
                try
                {
                    var connectionNId = connection.ToString();
                    var value = database.StringGetAsync(LeaseKey(tenantNId, userNId, connectionNId)).GetAwaiter().GetResult();
                    if (value.IsNullOrEmpty)
                    {
                        database.SetRemoveAsync(indexKey, connection).GetAwaiter().GetResult();
                        continue;
                    }
                    var lease = JsonSerializer.Deserialize<PresenceLease>(value.ToString());
                    if (lease is null)
                        continue;
                    if (lease.ExpiresOn <= now)
                    {
                        database.KeyDeleteAsync(LeaseKey(tenantNId, userNId, connectionNId)).GetAwaiter().GetResult();
                        database.SetRemoveAsync(indexKey, connection).GetAwaiter().GetResult();
                    }
                    else
                        leases.Add(lease);
                }
                catch (JsonException)
                {
                    database.KeyDeleteAsync(LeaseKey(tenantNId, userNId, connection.ToString())).GetAwaiter().GetResult();
                    database.SetRemoveAsync(indexKey, connection).GetAwaiter().GetResult();
                }
            }
            return PresenceLeaseProjection.Project(tenantNId, userNId, leases, now);
        }
        catch (RedisException)
        {
            return Unknown(userNId, now);
        }
    }

    private static PresenceDto Unknown(string userNId, DateTimeOffset now) =>
        new() { UserNId = userNId, State = "Unknown", Revision = 0, ObservedOn = now, ExpiresOn = now.AddSeconds(15) };

    private static RedisKey ConnectionIndexKey(string tenantNId, string userNId) => $"collaboration:presence:{tenantNId}:{userNId}:connections";
    private static RedisKey LeaseKey(string tenantNId, string userNId, string connectionNId) => $"collaboration:presence:{tenantNId}:{userNId}:lease:{connectionNId}";
}
