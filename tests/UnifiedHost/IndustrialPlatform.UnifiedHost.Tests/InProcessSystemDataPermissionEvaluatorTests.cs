using IndustrialPlatform.Identity.Application.Authorization;
using IndustrialPlatform.SystemData.Application.Authorization;

namespace IndustrialPlatform.UnifiedHost.Tests;

public sealed class InProcessSystemDataPermissionEvaluatorTests
{
    [Fact]
    public async Task Evaluate_DelegatesAuthenticatedContextToIdentityEvaluator()
    {
        var identity = new RecordingIdentityPermissionEvaluator();
        var adapter = new InProcessSystemDataPermissionEvaluator(identity);

        var result = await adapter.EvaluateAsync(
            new SystemDataPermissionRequest(
                "development", "admin", "SES-1", 3,
                "systemdata.organization.view", "unused-in-process"),
            CancellationToken.None);

        Assert.True(result.Allowed);
        Assert.Equal("development", identity.TenantNId);
        Assert.Equal("admin", identity.UserNId);
        Assert.Equal("systemdata.organization.view", identity.PermissionNId);
    }

    private sealed class RecordingIdentityPermissionEvaluator : IPermissionEvaluator
    {
        public string? TenantNId { get; private set; }
        public string? UserNId { get; private set; }
        public string? PermissionNId { get; private set; }

        public Task<PermissionEvaluation> EvaluateAsync(
            string tenantNId,
            string userNId,
            string? sessionNId,
            int authVersion,
            string requiredPermissionNId,
            CancellationToken cancellationToken)
        {
            TenantNId = tenantNId;
            UserNId = userNId;
            PermissionNId = requiredPermissionNId;
            return Task.FromResult(new PermissionEvaluation(true, AuthorizationDenialReason.None));
        }
    }
}
