using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using IndustrialPlatform.ReferenceData.Application.Authorization;
using IndustrialPlatform.ReferenceData.Contracts;
using IndustrialPlatform.ReferenceData.Contracts.Dictionary;
using IndustrialPlatform.Security;
using IndustrialPlatform.Web.Results;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IndustrialPlatform.ReferenceData.Tests;

public sealed class DictionaryHttpTests
{
    [Fact]
    public async Task Real_http_dictionary_flow_preserves_envelopes_tenant_authorization_and_concurrency()
    {
        var path = Path.Combine(Path.GetTempPath(), $"pf03-dictionary-http-{Guid.NewGuid():N}.db");
        try
        {
            await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["SqlSugar:ConnectionString"] = $"Data Source={path};Pooling=False",
                    ["DatabaseTopology:SharedSqliteFile"] = path,
                    ["ReferenceData:Initialization:AutoApply"] = "true",
                }));
                builder.ConfigureTestServices(services =>
                {
                    services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(path + ".keys")).UseEphemeralDataProtectionProvider();
                    services.AddAuthentication(options =>
                    {
                        options.DefaultAuthenticateScheme = "Test";
                        options.DefaultChallengeScheme = "Test";
                    }).AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>("Test", _ => { });
                    services.AddScoped<IReferenceDataPermissionEvaluator, TestPermissionEvaluator>();
                });
            });
            using var client = factory.CreateClient();
            const string root = "/api/v1/reference-data/admin/dictionaries";
            using var created = await client.PostAsJsonAsync(root, new CreateDictionaryRequest("Tenant", null, "STATUS", "Status", null, [new("OPEN", "Open", null, 0, true)]));
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            var draft = (await created.Content.ReadFromJsonAsync<ApiResult<DictionaryDetailDto>>())!.Data!;
            Assert.Equal("TENANT-A", draft.TenantNId);
            using var listed = await client.GetAsync(root);
            listed.EnsureSuccessStatusCode();
            var page = (await listed.Content.ReadFromJsonAsync<ApiResult<PageResult<DictionarySummaryDto>>>())!.Data!;
            Assert.Equal(1, Assert.Single(page.Items).EnabledItemCount);
            using var pageJson = JsonDocument.Parse(await listed.Content.ReadAsStringAsync());
            Assert.False(pageJson.RootElement.GetProperty("data").GetProperty("items")[0].TryGetProperty("items", out _));
            using var check = await client.GetAsync($"{root}/{draft.Id}/publication-check");
            Assert.Empty((await check.Content.ReadFromJsonAsync<ApiResult<DictionaryPublicationCheckDto>>())!.Data!.Errors);
            var version = new PublishOrDisableRequest(draft.OptimisticVersion, draft.ConcurrencyVersion, null);
            using var published = await client.PostAsJsonAsync($"{root}/{draft.Id}/publish", version);
            published.EnsureSuccessStatusCode();
            Assert.Equal("Published", (await published.Content.ReadFromJsonAsync<ApiResult<DictionaryDetailDto>>())!.Data!.Status);
            using var stale = await client.PutAsJsonAsync($"{root}/{draft.Id}", new UpdateDictionaryRequest("Stale", null, draft.Items, draft.OptimisticVersion, draft.ConcurrencyVersion));
            Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
            var conflict = (await stale.Content.ReadFromJsonAsync<ApiResult>())!;
            Assert.Equal("REF-CONCURRENCY-CONFLICT", conflict.Code);
            Assert.False(string.IsNullOrWhiteSpace(conflict.TraceId));
            using var effective = await client.GetAsync("/api/v1/reference-data/dictionaries/status");
            effective.EnsureSuccessStatusCode();
            Assert.Equal("Tenant", (await effective.Content.ReadFromJsonAsync<ApiResult<EffectiveDictionaryDto>>())!.Data!.SourceScope);

            client.DefaultRequestHeaders.Add("X-Test-Tenant", "TENANT-B");
            using var hidden = await client.GetAsync($"{root}/{draft.Id}");
            Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
            client.DefaultRequestHeaders.Remove("X-Test-Tenant");
            client.DefaultRequestHeaders.Add("X-Test-Denied", ReferenceDataPermissions.DictionaryView);
            using var denied = await client.GetAsync(root);
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
            client.DefaultRequestHeaders.Remove("X-Test-Denied");
            client.DefaultRequestHeaders.Add("X-Test-Denied", ReferenceDataPermissions.PlatformManage);
            using var platform = await client.PostAsJsonAsync(root, new CreateDictionaryRequest("Platform", null, "GLOBAL", "Global", null, []));
            Assert.Equal(HttpStatusCode.Forbidden, platform.StatusCode);
            client.DefaultRequestHeaders.Remove("X-Test-Denied");

            using var factoryScope = await client.PostAsJsonAsync(root, new CreateDictionaryRequest("Factory", "FACTORY-A", "FACTORY", "Factory", null, []));
            Assert.Equal(HttpStatusCode.Conflict, factoryScope.StatusCode);
            using var invalid = await client.PostAsJsonAsync(root, new { scopeType = "Tenant", nId = "INVALID", name = "Invalid", items = new object?[] { null } });
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
            using var invalidJson = JsonDocument.Parse(await invalid.Content.ReadAsStringAsync());
            Assert.Equal("items[0]", invalidJson.RootElement.GetProperty("parameters").GetProperty("field").GetString());
            using var invalidStatus = await client.GetAsync($"{root}?status=999");
            Assert.Equal(HttpStatusCode.BadRequest, invalidStatus.StatusCode);
            using var invalidPage = await client.GetAsync($"{root}?pageIndex=invalid");
            Assert.Equal(HttpStatusCode.BadRequest, invalidPage.StatusCode);
            Assert.Equal("REF-VALIDATION-FAILED", (await invalidPage.Content.ReadFromJsonAsync<ApiResult>())!.Code);
            using var invalidBody = new StringContent("{", System.Text.Encoding.UTF8, "application/json");
            using var malformed = await client.PostAsync(root, invalidBody);
            Assert.Equal(HttpStatusCode.BadRequest, malformed.StatusCode);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(path)) File.Delete(path);
            if (Directory.Exists(path + ".keys")) Directory.Delete(path + ".keys", true);
        }
    }

    private sealed class TestPermissionEvaluator(IHttpContextAccessor accessor) : IReferenceDataPermissionEvaluator
    {
        public Task<ReferenceDataPermissionDecision> EvaluateAsync(ReferenceDataPermissionRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(accessor.HttpContext!.Request.Headers["X-Test-Denied"] == request.PermissionNId
                ? new ReferenceDataPermissionDecision(false, ReferenceDataPermissionDenialReason.MissingPermission)
                : new ReferenceDataPermissionDecision(true, ReferenceDataPermissionDenialReason.None));
    }

    private sealed class TestAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var tenant = Request.Headers["X-Test-Tenant"].FirstOrDefault() ?? "TENANT-A";
            var identity = new ClaimsIdentity([
                new Claim(ClaimConstants.UserNId, "USER-A"), new Claim(ClaimConstants.TenantId, tenant),
                new Claim(ClaimConstants.SessionId, "SESSION-A"), new Claim(ClaimConstants.AuthVersion, "1"),
            ], "Test");
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), "Test")));
        }
    }
}
