using IndustrialPlatform.Collaboration.Contracts;

namespace IndustrialPlatform.Collaboration.Application;

public sealed record PresenceLease(
    string ConnectionNId,
    string State,
    long Revision,
    DateTimeOffset ObservedOn,
    DateTimeOffset ExpiresOn);

public static class PresenceLeaseProjection
{
    public static PresenceDto Project(string tenantNId, string userNId, IEnumerable<PresenceLease> leases, DateTimeOffset now)
    {
        var live = leases.Where(lease => lease.ExpiresOn > now).ToArray();
        if (live.Length == 0)
            return new PresenceDto { UserNId = userNId, State = "Offline", Revision = 0, ObservedOn = now, ExpiresOn = now };

        var selected = live
            .OrderByDescending(lease => StatePriority(lease.State))
            .ThenByDescending(lease => lease.ObservedOn)
            .First();
        return new PresenceDto
        {
            UserNId = userNId,
            State = selected.State,
            Revision = live.Max(lease => lease.Revision),
            ObservedOn = live.Max(lease => lease.ObservedOn),
            ExpiresOn = live.Max(lease => lease.ExpiresOn),
        };
    }

    private static int StatePriority(string state) => state switch
    {
        "Online" => 4,
        "Busy" => 3,
        "Away" => 2,
        _ => 1,
    };
}
