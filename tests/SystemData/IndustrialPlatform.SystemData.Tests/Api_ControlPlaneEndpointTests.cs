using System.Net;
using System.Net.Http.Json;
using IndustrialPlatform.SystemData.Application.ControlPlane;
using IndustrialPlatform.SystemData.Contracts.ControlPlane;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace IndustrialPlatform.SystemData.Api.Tests;

public sealed class ControlPlaneEndpointTests
{
    [Fact]
    public async Task Service_catalog_update_endpoint_binds_platform_safe_fields_and_owner_ids()
    {
        var service = new CapturingServiceCatalogControlService();
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IServiceCatalogControlService>();
            services.AddSingleton<IServiceCatalogControlService>(service);
            services.AddAuthentication(TestAuthDefaults.Scheme)
                .AddScheme<TestAuthHandlerOptions, TestAuthHandler>(TestAuthDefaults.Scheme, options => options.PrincipalFactory = _ => TestAuthHandler.BuildPrincipal(new TestUser
                {
                    UserNId = "user-actor",
                    UserName = "tester",
                    TenantNId = "tenant-a",
                    PermissionNIds = ["systemdata.service-catalog.manage"],
                }));
        }));
        using var client = factory.CreateClient();

        using var response = await client.PutAsJsonAsync("/api/v1/service-catalog/platform-a", new
        {
            name = "新名称",
            description = "新描述",
            ownerOrganizationNId = "org-a",
            technicalLeadUserNId = "user-a",
            status = "Inactive",
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("新名称", service.LastUpdate!.Name);
        Assert.Equal("org-a", service.LastUpdate.OwnerOrganizationNId);
        Assert.Equal("user-a", service.LastUpdate.TechnicalLeadUserNId);
        Assert.Equal("Inactive", service.LastUpdate.Status);
    }

    private sealed class CapturingServiceCatalogControlService : IServiceCatalogControlService
    {
        public UpdateServiceCatalogRequest? LastUpdate { get; private set; }

        public Task<IReadOnlyCollection<ServiceCatalogResponse>> ListAsync(string tenantNId, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<ServiceCatalogResponse>>([]);
        public Task<ServiceCatalogResponse> CreateExternalAsync(string tenantNId, CreateExternalServiceCatalogRequest request, CancellationToken cancellationToken) => Task.FromResult(new ServiceCatalogResponse());
        public Task<ServiceCatalogResponse> UpdateAsync(string tenantNId, string serviceNId, UpdateServiceCatalogRequest request, CancellationToken cancellationToken)
        {
            LastUpdate = request;
            return Task.FromResult(new ServiceCatalogResponse { ServiceNId = serviceNId, Name = request.Name ?? string.Empty, Status = request.Status ?? string.Empty });
        }
        public Task<ServiceCatalogResponse> SetStatusAsync(string tenantNId, string serviceNId, SetCatalogStatusRequest request, CancellationToken cancellationToken) => Task.FromResult(new ServiceCatalogResponse());
        public Task<ServiceCatalogRuntimeResponse> RuntimeAsync(string tenantNId, CancellationToken cancellationToken) => Task.FromResult(new ServiceCatalogRuntimeResponse());
    }
}
