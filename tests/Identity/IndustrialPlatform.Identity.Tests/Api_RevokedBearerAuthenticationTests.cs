using System.Net;
using System.Net.Http.Headers;
using IndustrialPlatform.Identity.Application.Authentication;
using IndustrialPlatform.Identity.Contracts.Authentication;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace IndustrialPlatform.Identity.Api.Tests;

public sealed class RevokedBearerAuthenticationTests
{
    [Fact]
    public async Task RevokedSid_IsRejectedByAuthMeBeforeControllerExecution()
    {
        var revocation = new FakeRevocationStore();
        using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ISessionRevocationStore>();
                services.AddSingleton<ISessionRevocationStore>(revocation);
                services.RemoveAll<IAuthenticationService>();
                services.AddSingleton<IAuthenticationService>(new FakeAuthenticationService());
            }));
        using var client = factory.CreateClient();
        var tokenFactory = factory.Services.GetRequiredService<IAccessTokenFactory>();
        var token = tokenFactory.Create(new AccessTokenDescriptor(
            "user.alice",
            "alice",
            "development",
            [],
            "sid-target",
            0,
            DateTimeOffset.UtcNow)).Token;
        var siblingToken = tokenFactory.Create(new AccessTokenDescriptor(
            "user.alice",
            "alice",
            "development",
            [],
            "sid-sibling",
            0,
            DateTimeOffset.UtcNow)).Token;

        await revocation.RevokeAsync("sid-target", TimeSpan.FromMinutes(30), CancellationToken.None);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await client.SendAsync(request);
        using var siblingRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/me");
        siblingRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", siblingToken);
        using var siblingResponse = await client.SendAsync(siblingRequest);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, siblingResponse.StatusCode);
    }

    [Fact]
    public async Task RevocationStoreUnavailable_IsFailClosedWith503()
    {
        var revocation = new FakeRevocationStore { FailOnCheck = true };
        using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ISessionRevocationStore>();
                services.AddSingleton<ISessionRevocationStore>(revocation);
                services.RemoveAll<IAuthenticationService>();
                services.AddSingleton<IAuthenticationService>(new FakeAuthenticationService());
            }));
        using var client = factory.CreateClient();
        var token = factory.Services.GetRequiredService<IAccessTokenFactory>().Create(new AccessTokenDescriptor(
            "user.alice", "alice", "development", [], "sid-target", 0, DateTimeOffset.UtcNow)).Token;

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    private sealed class FakeRevocationStore : ISessionRevocationStore
    {
        private readonly HashSet<string> revoked = new(StringComparer.Ordinal);
        public bool FailOnCheck { get; init; }

        public Task RevokeAsync(string sessionNId, TimeSpan ttl, CancellationToken cancellationToken)
        {
            revoked.Add(sessionNId);
            return Task.CompletedTask;
        }

        public Task<bool> IsRevokedAsync(string sessionNId, CancellationToken cancellationToken)
        {
            if (FailOnCheck)
            {
                throw new SecurityStoreUnavailableException();
            }

            return Task.FromResult(revoked.Contains(sessionNId));
        }
    }

    private sealed class FakeAuthenticationService : IAuthenticationService
    {
        public Task<AuthUser> GetCurrentUserAsync(string userNId, CancellationToken cancellationToken)
            => Task.FromResult(new AuthUser(
                userNId,
                "alice",
                "Alice",
                "development",
                [],
                [],
                false));

        public Task<AuthSession> LoginAsync(
            LoginRequest request,
            string? clientIp,
            string? userAgent,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<AuthSession> RefreshAsync(
            RefreshRequest request,
            string? clientIp,
            string? userAgent,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task LogoutAsync(
            LogoutRequest request,
            string? sessionNId,
            TimeSpan sidRevocationTtl,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task LogoutAllAsync(string userNId, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task ChangePasswordAsync(
            ChangePasswordRequest request,
            string userNId,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();
    }
}
