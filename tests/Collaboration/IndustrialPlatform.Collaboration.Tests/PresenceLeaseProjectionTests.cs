using IndustrialPlatform.Collaboration.Application;
using Xunit;

namespace IndustrialPlatform.Collaboration.Tests;

public sealed class PresenceLeaseProjectionTests
{
    [Fact]
    public void Project_UsesAnyLiveConnectionAndDropsExpiredLeases()
    {
        var now = DateTimeOffset.UtcNow;
        var leases = new[]
        {
            new PresenceLease("a", "Offline", 1, now.AddSeconds(-5), now.AddSeconds(-1)),
            new PresenceLease("b", "Online", 2, now.AddSeconds(-2), now.AddSeconds(20)),
        };

        var result = PresenceLeaseProjection.Project("tenant-1", "user-1", leases, now);

        Assert.Equal("Online", result.State);
        Assert.Equal(2, result.Revision);
        Assert.Equal(now.AddSeconds(20), result.ExpiresOn);
    }

    [Fact]
    public void Project_ReturnsOfflineWhenNoLiveConnectionRemains()
    {
        var now = DateTimeOffset.UtcNow;
        var result = PresenceLeaseProjection.Project("tenant-1", "user-1", [
            new PresenceLease("a", "Away", 4, now.AddSeconds(-70), now.AddSeconds(-10)),
        ], now);

        Assert.Equal("Offline", result.State);
        Assert.Equal(0, result.Revision);
    }
}
