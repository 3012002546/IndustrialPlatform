using System.Net;
using IndustrialPlatform.SystemData.Application.Authorization;
using IndustrialPlatform.SystemData.Api.Authorization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace IndustrialPlatform.SystemData.Api.Tests;

public sealed class Pf04AuthorizationTests
{
    [Fact]
    public async Task File_notification_and_audit_queries_are_denied_before_service_execution_without_permissions()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ISystemDataPermissionEvaluator>();
            services.AddSingleton<ISystemDataPermissionEvaluator, DenyPermissionEvaluator>();
            services.AddAuthentication(TestAuthDefaults.Scheme)
                .AddScheme<TestAuthHandlerOptions, TestAuthHandler>(TestAuthDefaults.Scheme, options => options.PrincipalFactory = _ => TestAuthHandler.BuildPrincipal(new TestUser
                {
                    UserNId = "viewer",
                    TenantNId = "tenant-1",
                    PermissionNIds = [],
                }));
        }));
        using var client = factory.CreateClient();

        using var file = await client.GetAsync("/api/v1/files?page=1&pageSize=25");
        using var announcements = await client.GetAsync("/api/v1/notifications/announcements?page=1&pageSize=25");
        using var audits = await client.GetAsync("/api/v1/audits/facts?page=1&pageSize=25");

        Assert.Equal(HttpStatusCode.Forbidden, file.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, announcements.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, audits.StatusCode);
    }

    private sealed class DenyPermissionEvaluator : ISystemDataPermissionEvaluator
    {
        public Task<SystemDataPermissionDecision> EvaluateAsync(SystemDataPermissionRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new SystemDataPermissionDecision(false, SystemDataPermissionDenialReason.MissingPermission));
    }
}
