using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using IndustrialPlatform.Identity.Application.Authentication;
using IndustrialPlatform.Identity.Application.Authorization;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace IndustrialPlatform.Identity.Api.Tests;

public sealed class AuthorizationEvaluationEndpointTests
{
    [Fact]
    public async Task Evaluate_UsesAuthenticatedSubjectAndRequestedPermission()
    {
        var evaluator = new RecordingPermissionEvaluator
        {
            Result = new PermissionEvaluation(true, AuthorizationDenialReason.None),
        };
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IPermissionEvaluator>();
                services.AddSingleton<IPermissionEvaluator>(evaluator);
            }));
        using var client = factory.CreateClient();
        var token = factory.Services.GetRequiredService<IAccessTokenFactory>().Create(new AccessTokenDescriptor(
            "admin", "admin", "development", ["SYSTEM_ADMIN"], "SES-evaluate", 3, DateTimeOffset.UtcNow)).Token;
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/authorization/evaluate")
        {
            Content = JsonContent.Create(new { permissionNId = "systemdata.organization.view" }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
        Assert.True(payload.RootElement.GetProperty("data").GetProperty("allowed").GetBoolean());
        Assert.Equal("development", evaluator.TenantNId);
        Assert.Equal("admin", evaluator.UserNId);
        Assert.Equal("SES-evaluate", evaluator.SessionNId);
        Assert.Equal(3, evaluator.AuthVersion);
        Assert.Equal("systemdata.organization.view", evaluator.PermissionNId);
    }

    private sealed class RecordingPermissionEvaluator : IPermissionEvaluator
    {
        public required PermissionEvaluation Result { get; init; }
        public string? TenantNId { get; private set; }
        public string? UserNId { get; private set; }
        public string? SessionNId { get; private set; }
        public int AuthVersion { get; private set; }
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
            SessionNId = sessionNId;
            AuthVersion = authVersion;
            PermissionNId = requiredPermissionNId;
            return Task.FromResult(Result);
        }
    }
}
