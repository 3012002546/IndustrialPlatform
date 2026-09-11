using System.Security.Claims;
using IndustrialPlatform.Collaboration.Api.Security;
using IndustrialPlatform.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;

namespace IndustrialPlatform.Collaboration.Tests;

public sealed class Security_ExecutionTimePermissionTests
{
    [Fact]
    public async Task Revoking_original_permission_is_observed_on_the_next_execution_check()
    {
        var policy = new ToggleAuthorizationService { Allowed = true };
        var context = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimConstants.TenantId, "T-1"),
            new Claim(ClaimConstants.UserNId, "U-1"),
            new Claim(ClaimConstants.SessionId, "SID-1"),
            new Claim(ClaimConstants.AuthVersion, "7"),
        }, "test")) };
        var evaluator = new HttpCollaborationPermissionEvaluator(policy, new HttpContextAccessor { HttpContext = context });

        Assert.True(await evaluator.HasPermissionAsync("collaboration.compliance.read-original", "T-1", "U-1", "SID-1", "7", CancellationToken.None));
        policy.Allowed = false;

        Assert.False(await evaluator.HasPermissionAsync("collaboration.compliance.read-original", "T-1", "U-1", "SID-1", "7", CancellationToken.None));
    }

    [Fact]
    public async Task Administrator_export_waiver_requires_the_bound_authenticated_role_and_live_permission()
    {
        var policy = new ToggleAuthorizationService { Allowed = true };
        var identity = new ClaimsIdentity(new[] {
            new Claim(ClaimConstants.TenantId, "T-1"), new Claim(ClaimConstants.UserNId, "U-1"),
            new Claim(ClaimConstants.SessionId, "SID-1"), new Claim(ClaimConstants.AuthVersion, "7"),
        }, "test");
        var evaluator = new HttpCollaborationPermissionEvaluator(policy, new HttpContextAccessor {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) },
        });
        Assert.False(await evaluator.IsSystemAdministratorAsync("T-1", "U-1", "SID-1", "7", CancellationToken.None));
        identity.AddClaim(new Claim(ClaimConstants.Role, "SYSTEM_ADMIN"));
        Assert.True(await evaluator.IsSystemAdministratorAsync("T-1", "U-1", "SID-1", "7", CancellationToken.None));
        Assert.False(await evaluator.IsSystemAdministratorAsync("T-2", "U-1", "SID-1", "7", CancellationToken.None));
        Assert.False(await evaluator.IsSystemAdministratorAsync("T-1", "U-2", "SID-1", "7", CancellationToken.None));
        Assert.False(await evaluator.IsSystemAdministratorAsync("T-1", "U-1", "SID-old", "7", CancellationToken.None));
        Assert.False(await evaluator.IsSystemAdministratorAsync("T-1", "U-1", "SID-1", "6", CancellationToken.None));
        policy.Allowed = false;
        Assert.False(await evaluator.IsSystemAdministratorAsync("T-1", "U-1", "SID-1", "7", CancellationToken.None));
    }

    private sealed class ToggleAuthorizationService : IAuthorizationService
    {
        public bool Allowed { get; set; }

        public Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, object? resource, IEnumerable<IAuthorizationRequirement> requirements) =>
            Task.FromResult(Allowed ? AuthorizationResult.Success() : AuthorizationResult.Failed());

        public Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, string policyName) =>
            Task.FromResult(Allowed ? AuthorizationResult.Success() : AuthorizationResult.Failed());

        public Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, object? resource, string policyName) =>
            Task.FromResult(Allowed ? AuthorizationResult.Success() : AuthorizationResult.Failed());
    }
}
