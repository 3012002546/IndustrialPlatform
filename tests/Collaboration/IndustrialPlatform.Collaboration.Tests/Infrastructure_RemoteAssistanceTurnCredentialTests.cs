using IndustrialPlatform.Collaboration.Application.RemoteAssistance;
using IndustrialPlatform.Collaboration.Infrastructure;
using Microsoft.Extensions.Options;

namespace IndustrialPlatform.Collaboration.Tests;

public sealed class Infrastructure_RemoteAssistanceTurnCredentialTests
{
    [Fact]
    public void Defaults_to_all_ice_policy()
    {
        Assert.Equal("All", new RemoteAssistanceOptions().IcePolicy);
    }

    [Fact]
    public async Task Relay_only_without_turn_configuration_fails_closed()
    {
        var provider = new RemoteAssistanceTurnCredentialProvider(
            Options.Create(new RemoteAssistanceOptions { IcePolicy = "RelayOnly" }));

        var exception = await Assert.ThrowsAsync<RemoteAssistanceException>(() =>
            provider.GetAsync("tenant-1", "user-1", CancellationToken.None));

        Assert.Equal("MEDIA_ICE_CONFIGURATION", exception.Code);
        Assert.Equal("collaboration.media.iceConfiguration", exception.MessageKey);
    }

    [Fact]
    public async Task Relay_only_issues_short_lived_credentials_for_configured_turn_servers()
    {
        var now = DateTimeOffset.UtcNow;
        var provider = new RemoteAssistanceTurnCredentialProvider(
            Options.Create(new RemoteAssistanceOptions
            {
                IcePolicy = "RelayOnly",
                IceServerUrls = ["turns:turn.example.test:5349"],
                TurnCredentialSecret = "test-secret",
                TurnCredentialTtlSeconds = 120,
            }),
            () => now);

        var servers = await provider.GetAsync("tenant-1", "user-1", CancellationToken.None);

        var server = Assert.Single(servers);
        Assert.Equal(["turns:turn.example.test:5349"], server.Urls);
        Assert.False(string.IsNullOrWhiteSpace(server.Username));
        Assert.False(string.IsNullOrWhiteSpace(server.Credential));
        Assert.Contains(":user-1", server.Username, StringComparison.Ordinal);
    }

    [Fact]
    public async Task All_policy_issues_short_lived_credentials_for_configured_turn_servers()
    {
        var provider = new RemoteAssistanceTurnCredentialProvider(
            Options.Create(new RemoteAssistanceOptions
            {
                IcePolicy = "All",
                IceServerUrls = ["turns:turn.example.test:5349"],
                TurnCredentialSecret = "test-secret",
                TurnCredentialTtlSeconds = 120,
            }));

        var servers = await provider.GetAsync("tenant-1", "user-1", CancellationToken.None);

        var server = Assert.Single(servers);
        Assert.False(string.IsNullOrWhiteSpace(server.Username));
        Assert.False(string.IsNullOrWhiteSpace(server.Credential));
    }
}
