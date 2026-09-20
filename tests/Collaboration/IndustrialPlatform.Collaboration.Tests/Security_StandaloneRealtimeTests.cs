using IndustrialPlatform.Collaboration.EmbeddedHost.Services;

namespace IndustrialPlatform.Collaboration.Tests;

public sealed class Security_StandaloneRealtimeTests
{
    [Fact]
    public void Presence_is_online_while_any_connection_is_live_and_isolated_by_tenant()
    {
        var presence = new StandalonePresenceRegistry();

        Assert.Equal("Offline", presence.GetPresence("standalone", "operator-2").State);
        Assert.Equal("Online", presence.SetPresence("standalone", "operator-2", "Online", "page-a").State);
        presence.SetPresence("standalone", "operator-2", "Online", "page-b");
        presence.RemovePresence("standalone", "operator-2", "page-a");

        Assert.Equal("Online", presence.GetPresence("standalone", "operator-2").State);
        Assert.Equal("Offline", presence.GetPresence("another-tenant", "operator-2").State);

        presence.RemovePresence("standalone", "operator-2", "page-b");
        Assert.Equal("Offline", presence.GetPresence("standalone", "operator-2").State);
    }
}
