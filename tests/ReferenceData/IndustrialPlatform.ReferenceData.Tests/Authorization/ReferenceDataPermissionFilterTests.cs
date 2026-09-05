using System.Security.Claims;
using IndustrialPlatform.ReferenceData.Api.Authorization;
using IndustrialPlatform.ReferenceData.Application.Authorization;
using IndustrialPlatform.ReferenceData.Contracts;
using IndustrialPlatform.Security;
using IndustrialPlatform.Web.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace IndustrialPlatform.ReferenceData.Tests.Authorization;

public sealed class ReferenceDataPermissionFilterTests
{
    [Theory]
    [InlineData(ReferenceDataPermissionDenialReason.SessionInvalid, 401)]
    [InlineData(ReferenceDataPermissionDenialReason.MissingPermission, 403)]
    [InlineData(ReferenceDataPermissionDenialReason.SecurityStoreUnavailable, 503)]
    public async Task Embedded_permission_claim_cannot_bypass_dynamic_denial(ReferenceDataPermissionDenialReason reason, int status)
    {
        var evaluator = new DecisionEvaluator(new(false, reason));
        var http = Context();
        var called = false;
        var result = await new ReferenceDataPermissionFilter(evaluator).InvokeAsync(
            new DefaultEndpointFilterInvocationContext(http, []), _ => { called = true; return ValueTask.FromResult<object?>(null); });
        var response = Assert.IsType<JsonHttpResult<ApiResult>>(result);
        Assert.Equal(status, response.StatusCode);
        Assert.False(called);
        Assert.False(string.IsNullOrWhiteSpace(response.Value!.TraceId));
        Assert.Equal(ReferenceDataPermissions.DictionaryView, Assert.Single(evaluator.Requests).PermissionNId);
    }

    [Fact]
    public async Task Missing_tenant_fails_before_authority_call()
    {
        var evaluator = new DecisionEvaluator(new(true, ReferenceDataPermissionDenialReason.None));
        var http = Context();
        ((ClaimsIdentity)http.User.Identity!).RemoveClaim(http.User.FindFirst(ClaimConstants.TenantId)!);
        var result = await new ReferenceDataPermissionFilter(evaluator).InvokeAsync(new DefaultEndpointFilterInvocationContext(http, []), _ => ValueTask.FromResult<object?>(null));
        Assert.Equal(401, Assert.IsType<JsonHttpResult<ApiResult>>(result).StatusCode);
        Assert.Empty(evaluator.Requests);
    }

    [Fact]
    public async Task Allowed_read_receives_verified_actor_without_platform_write_grant()
    {
        var evaluator = new DecisionEvaluator(new(true, ReferenceDataPermissionDenialReason.None));
        var http = Context();
        var result = await new ReferenceDataPermissionFilter(evaluator).InvokeAsync(new DefaultEndpointFilterInvocationContext(http, []), _ => ValueTask.FromResult<object?>(http.ReferenceDataActor()));
        var actor = Assert.IsType<ReferenceDataActor>(result);
        Assert.Equal("TENANT-A", actor.TenantNId);
        Assert.Equal("USER-A", actor.UserNId);
        Assert.False(actor.CanManagePlatform);
        Assert.Single(evaluator.Requests);
    }

    private static DefaultHttpContext Context()
    {
        var http = new DefaultHttpContext();
        http.Request.Method = "GET";
        http.Request.Headers.Authorization = "Bearer test-only";
        http.User = new ClaimsPrincipal(new ClaimsIdentity([
            new(ClaimConstants.UserNId, "USER-A"), new(ClaimConstants.TenantId, "TENANT-A"),
            new(ClaimConstants.SessionId, "SESSION-A"), new(ClaimConstants.AuthVersion, "1"),
            new("permission_nid", ReferenceDataPermissions.DictionaryView),
        ], "test"));
        http.SetEndpoint(new Endpoint(null, new EndpointMetadataCollection(new ReferenceDataPermissionMetadata(ReferenceDataPermissions.DictionaryView)), "reference-test"));
        return http;
    }

    private sealed class DecisionEvaluator(ReferenceDataPermissionDecision decision) : IReferenceDataPermissionEvaluator
    {
        public List<ReferenceDataPermissionRequest> Requests { get; } = [];
        public Task<ReferenceDataPermissionDecision> EvaluateAsync(ReferenceDataPermissionRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(decision);
        }
    }
}
