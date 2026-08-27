using System.Security.Claims;
using IndustrialPlatform.Security;
using IndustrialPlatform.SystemData.Api.Authorization;
using IndustrialPlatform.SystemData.Application.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;

namespace IndustrialPlatform.SystemData.Api.Tests;

public sealed class SystemDataPermissionAuthorizationHandlerTests
{
    [Fact]
    public async Task Handle_AllowsIdentityEvaluationWithoutPermissionClaims()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers.Authorization = "Bearer access-token";
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimConstants.UserNId, "admin"),
            new Claim(ClaimConstants.TenantId, "development"),
            new Claim(ClaimConstants.SessionId, "SES-1"),
            new Claim(ClaimConstants.AuthVersion, "3"),
        ], "Test"));
        var requirement = new SystemDataPermissionRequirement("systemdata.organization.view");
        var context = new AuthorizationHandlerContext([requirement], principal, null);
        var evaluator = new RecordingSystemDataPermissionEvaluator(
            new SystemDataPermissionDecision(true, SystemDataPermissionDenialReason.None));
        var handler = new SystemDataPermissionAuthorizationHandler(
            evaluator,
            new HttpContextAccessor { HttpContext = httpContext });

        await handler.HandleAsync(context);

        Assert.True(context.HasSucceeded);
        Assert.Equal("access-token", evaluator.Request?.AccessToken);
        Assert.Equal("systemdata.organization.view", evaluator.Request?.PermissionNId);
    }

    [Theory]
    [InlineData(SystemDataPermissionDenialReason.SessionInvalid, StatusCodes.Status401Unauthorized)]
    [InlineData(SystemDataPermissionDenialReason.MissingPermission, StatusCodes.Status403Forbidden)]
    [InlineData(SystemDataPermissionDenialReason.SecurityStoreUnavailable, StatusCodes.Status503ServiceUnavailable)]
    public async Task Handle_MapsIdentityDenialToStableHttpStatus(
        SystemDataPermissionDenialReason reason,
        int expectedStatus)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Response.Body = new MemoryStream();
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimConstants.UserNId, "admin"),
            new Claim(ClaimConstants.TenantId, "development"),
            new Claim(ClaimConstants.SessionId, "SES-1"),
            new Claim(ClaimConstants.AuthVersion, "3"),
        ], "Test"));
        var requirement = new SystemDataPermissionRequirement("systemdata.organization.view");
        var authorizationContext = new AuthorizationHandlerContext([requirement], principal, null);
        var handler = new SystemDataPermissionAuthorizationHandler(
            new RecordingSystemDataPermissionEvaluator(new SystemDataPermissionDecision(false, reason)),
            new HttpContextAccessor { HttpContext = httpContext });
        await handler.HandleAsync(authorizationContext);

        var resultHandler = new SystemDataAuthorizationMiddlewareResultHandler();
        await resultHandler.HandleAsync(
            _ => Task.CompletedTask,
            httpContext,
            new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build(),
            PolicyAuthorizationResult.Forbid());

        Assert.Equal(expectedStatus, httpContext.Response.StatusCode);
    }

    private sealed class RecordingSystemDataPermissionEvaluator(SystemDataPermissionDecision decision)
        : ISystemDataPermissionEvaluator
    {
        public SystemDataPermissionRequest? Request { get; private set; }

        public Task<SystemDataPermissionDecision> EvaluateAsync(
            SystemDataPermissionRequest request,
            CancellationToken cancellationToken)
        {
            Request = request;
            return Task.FromResult(decision);
        }
    }
}
