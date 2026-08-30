using IndustrialPlatform.Identity.Application.Authentication;
using Microsoft.Extensions.Options;

namespace IndustrialPlatform.Identity.Tests;

public sealed class IdentitySessionManagementTests
{
    [Fact]
    public async Task ListActive_ProjectsSafeSummaryAndMarksCurrentSession()
    {
        var store = new FakeStore
        {
            Active =
            [
                new ActiveRefreshSession(
                    "sid-1", "user-1", "operator", "操作员",
                    DateTimeOffset.UtcNow.AddMinutes(-10), DateTimeOffset.UtcNow.AddMinutes(-1),
                    DateTimeOffset.UtcNow.AddHours(1), Guid.NewGuid()),
            ],
        };
        var service = new IdentitySessionManagementService(
            store,
            new FakeRevocationStore(),
            Options.Create(new AuthenticationOptions()));

        var result = await service.ListActiveAsync("tenant-a", "sid-1", CancellationToken.None);

        var session = Assert.Single(result);
        Assert.Equal("sid-1", session.SessionNId);
        Assert.True(session.IsCurrent);
        Assert.Equal("操作员", session.Name);
    }

    [Fact]
    public async Task Revoke_IsTenantScopedAndIdempotent()
    {
        var store = new FakeStore { RevokeResult = true };
        var revocation = new FakeRevocationStore();
        var service = new IdentitySessionManagementService(
            store,
            revocation,
            Options.Create(new AuthenticationOptions { AccessTokenRevocationMinutes = 30 }));

        var result = await service.RevokeAsync("tenant-a", "sid-1", "sid-1", CancellationToken.None);

        Assert.True(result.Found);
        Assert.True(result.IsCurrent);
        Assert.Equal(("tenant-a", "sid-1", "admin_revoke"), store.LastRevoke);
        var revocationCall = Assert.Single(revocation.Calls);
        Assert.Equal("sid-1", revocationCall.SessionNId);
        Assert.Equal(TimeSpan.FromMinutes(30), revocationCall.Ttl);
    }

    private sealed class FakeStore : IRefreshSessionStore
    {
        public IReadOnlyList<ActiveRefreshSession> Active { get; init; } = [];
        public bool RevokeResult { get; init; }
        public (string Tenant, string Session, string Reason) LastRevoke { get; private set; }

        public Task<IReadOnlyList<ActiveRefreshSession>> ListActiveForTenantAsync(string tenantNId, DateTimeOffset now, CancellationToken cancellationToken)
            => Task.FromResult(Active);

        public Task<bool> RevokeByNIdAsync(string tenantNId, string sessionNId, string reason, CancellationToken cancellationToken)
        {
            LastRevoke = (tenantNId, sessionNId, reason);
            return Task.FromResult(RevokeResult);
        }

        public Task<StoredRefreshSession?> FindByRawTokenAsync(string rawToken, CancellationToken cancellationToken) => Task.FromResult<StoredRefreshSession?>(null);
        public Task AddAsync(NewRefreshSession session, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<RefreshRotationStatus> RotateAsync(Guid currentSessionId, NewRefreshSession replacement, CancellationToken cancellationToken) => Task.FromResult(RefreshRotationStatus.Invalid);
        public Task RevokeFamilyAsync(string familyNId, string reason, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task RevokeAllForUserAsync(Guid userId, string reason, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FakeRevocationStore : ISessionRevocationStore
    {
        public List<(string SessionNId, TimeSpan Ttl)> Calls { get; } = [];

        public Task RevokeAsync(string sessionNId, TimeSpan ttl, CancellationToken cancellationToken)
        {
            Calls.Add((sessionNId, ttl));
            return Task.CompletedTask;
        }

        public Task<bool> IsRevokedAsync(string sessionNId, CancellationToken cancellationToken)
            => Task.FromResult(false);
    }
}
