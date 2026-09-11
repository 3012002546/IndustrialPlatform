using System.Security.Claims;
using IndustrialPlatform.Collaboration.Api.Hubs;
using IndustrialPlatform.Collaboration.Application;
using IndustrialPlatform.Collaboration.Contracts;
using IndustrialPlatform.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.SignalR;

namespace IndustrialPlatform.Collaboration.Tests;

public sealed class Api_CollaborationHubAuthorizationTests
{
    [Fact]
    public void Hub_requires_messaging_read_permission_to_connect()
    {
        var authorize = typeof(CollaborationHub).GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .Cast<AuthorizeAttribute>()
            .Single();

        Assert.Equal("permission:collaboration.messaging.read", authorize.Policy);
    }

    [Fact]
    public async Task SendMessage_checks_write_permission_at_invocation_time()
    {
        var authorization = new FixedAuthorizationService { Allowed = false };
        var hub = new CollaborationHub(null!, new TestCurrentUser("T-1", "U-1"), authorization)
        {
            Context = new TestHubCallerContext(),
        };

        var exception = await Assert.ThrowsAsync<HubException>(() => hub.SendMessage(
            "CV-1",
            new SendMessageRequest { ClientMessageNId = "CLIENT-1", TextContent = "hello" }));

        Assert.Equal("forbidden", exception.Message);
    }

    [Fact]
    public async Task JoinConversation_rejects_a_missing_tenant_or_actor_before_group_join()
    {
        var hub = new CollaborationHub(null!, new TestCurrentUser(null, null), new FixedAuthorizationService { Allowed = true })
        {
            Context = new TestHubCallerContext(),
        };

        var exception = await Assert.ThrowsAsync<HubException>(() => hub.JoinConversation("CV-1"));

        Assert.Equal("unauthorized", exception.Message);
    }

    private sealed class TestCurrentUser(string? tenantId, string? userNId) : ICurrentUser
    {
        public bool IsAuthenticated => TenantId is not null && UserNId is not null;
        public string? UserNId { get; } = userNId;
        public string? UserName => UserNId;
        public string? TenantId { get; } = tenantId;
        public IReadOnlyCollection<string> Roles => [];
    }

    private sealed class FixedAuthorizationService : IAuthorizationService
    {
        public bool Allowed { get; init; }

        public Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, object? resource, IEnumerable<IAuthorizationRequirement> requirements) => Result();
        public Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, string policyName) => Result();
        public Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, object? resource, string policyName) => Result();

        private Task<AuthorizationResult> Result() => Task.FromResult(Allowed ? AuthorizationResult.Success() : AuthorizationResult.Failed());
    }

    private sealed class TestHubCallerContext : HubCallerContext
    {
        public override string ConnectionId => "CONN-1";
        public override string? UserIdentifier => "U-1";
        public override ClaimsPrincipal? User => new(new ClaimsIdentity("test"));
        public override IDictionary<object, object?> Items { get; } = new Dictionary<object, object?>();
        public override IFeatureCollection Features { get; } = new FeatureCollection();
        public override CancellationToken ConnectionAborted => CancellationToken.None;
        public override void Abort() { }
    }
}
