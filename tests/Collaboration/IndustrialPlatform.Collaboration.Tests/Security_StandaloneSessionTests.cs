using System.Net;
using System.Net.NetworkInformation;
using System.Text.Json;
using IndustrialPlatform.Collaboration.EmbeddedHost.Abstractions;
using IndustrialPlatform.Collaboration.EmbeddedHost.Adapters;
using IndustrialPlatform.Collaboration.EmbeddedHost.Models;
using IndustrialPlatform.Collaboration.EmbeddedHost.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace IndustrialPlatform.Collaboration.Tests;

public sealed class Security_StandaloneSessionTests
{
    [Fact]
    public void Development_origin_accepts_current_host_ip_but_not_unrelated_hosts()
    {
        var options = new EmbeddedHostHandshakeOptions(["https://localhost:5173"])
        {
            AllowLocalDevelopmentOrigins = true,
        };
        var address = NetworkInterface.GetAllNetworkInterfaces()
            .Where(adapter => adapter.OperationalStatus == OperationalStatus.Up)
            .SelectMany(adapter => adapter.GetIPProperties().UnicastAddresses)
            .Select(item => item.Address)
            .Where(address => address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
            .First();
        var host = address.ToString();

        Assert.True(options.IsAllowedOrigin($"https://{host}:5173"));
        Assert.False(options.IsAllowedOrigin("https://203.0.113.10:5173"));
        Assert.False(options.IsAllowedOrigin($"http://{host}:5173"));
        Assert.False(options.IsAllowedOrigin($"https://{host}:5174"));
        Assert.False(new EmbeddedHostHandshakeOptions(["https://localhost:5173"])
            .IsAllowedOrigin($"https://{host}:5173"));
    }

    [Fact]
    public async Task Standalone_entry_checks_account_against_user_list_and_creates_host_session()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["EmbeddedCollaboration:PlatformTenantNId"] = "standalone",
        });
        builder.Services.AddSingleton<IEmbeddedCollaborationAccessAdapter, MesUserDirectoryAdapter>();
        builder.Services.AddSingleton<IEmbeddedSourcePrincipalResolver, StandaloneAccountSourcePrincipalResolver>();
        builder.Services.AddSingleton<RecordingStore>();
        builder.Services.AddSingleton<IEmbeddedHandshakeStore>(sp => sp.GetRequiredService<RecordingStore>());
        builder.Services.AddSingleton(new EmbeddedHostHandshakeOptions(["https://parent.example"]));
        await using var app = builder.Build();
        app.MapStandaloneSession();
        await app.StartAsync();
        var client = app.GetTestClient();
        var users = (MesUserDirectoryAdapter)app.Services.GetRequiredService<IEmbeddedCollaborationAccessAdapter>();
        var selected = (await users.LoadMesUsersAsync(CancellationToken.None))[0];

        using var missing = await SendAsync(client, "missing-user");
        Assert.Equal(HttpStatusCode.Forbidden, missing.StatusCode);

        using var accepted = await SendAsync(client, selected.UserId);
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        using var body = JsonDocument.Parse(await accepted.Content.ReadAsStringAsync());
        Assert.Equal(selected.UserId, body.RootElement.GetProperty("identity").GetProperty("userNId").GetString());
        Assert.Equal(selected.UserName, body.RootElement.GetProperty("identity").GetProperty("displayName").GetString());
        Assert.False(string.IsNullOrWhiteSpace(body.RootElement.GetProperty("pageSession").GetProperty("token").GetString()));
        Assert.NotNull(app.Services.GetRequiredService<RecordingStore>().Created);
    }

    private static Task<HttpResponseMessage> SendAsync(HttpClient client, string account)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/embedded/standalone/session?account={Uri.EscapeDataString(account)}");
        request.Headers.Add("Origin", "https://parent.example");
        return client.SendAsync(request);
    }

    private sealed class RecordingStore : IEmbeddedHandshakeStore
    {
        public EmbeddedIdentity? Created { get; private set; }

        public Task<EmbeddedStoredSession> CreateSessionAsync(
            EmbeddedIdentity identity, string browserBindingHash, string sessionTokenHash,
            DateTimeOffset expiresOn, CancellationToken cancellationToken)
        {
            Created = identity;
            return Task.FromResult(new EmbeddedStoredSession(
                sessionTokenHash, browserBindingHash, identity, 1, expiresOn, null));
        }

        public Task<EmbeddedStoredChallenge?> GetChallengeAsync(string nonce, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task SaveChallengeAsync(EmbeddedStoredChallenge challenge, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<EmbeddedChallengeConsumeResult> ConsumeChallengeAsync(
            string nonce, string browserBindingHash, string jti, DateTimeOffset now,
            DateTimeOffset assertionExpiresOn, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<EmbeddedStoredSession?> GetSessionAsync(
            string sessionTokenHash, string browserBindingHash, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<EmbeddedStoredSession?> TouchSessionAsync(
            string sessionTokenHash, string browserBindingHash, DateTimeOffset now,
            DateTimeOffset expiresOn, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task RevokeSessionAsync(
            string sessionTokenHash, string browserBindingHash, DateTimeOffset revokedOn,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
