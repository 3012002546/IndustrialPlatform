using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using IndustrialPlatform.ReferenceData.Application.Authorization;
using IndustrialPlatform.ReferenceData.Contracts;
using IndustrialPlatform.ReferenceData.Contracts.DynamicProperty;
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

public sealed class DynamicConfigurationHttpTests
{
    private const string Root = "/api/v1/reference-data/admin/dynamic-properties/configurations";
    private const string Runtime = "/api/v1/reference-data/dynamic-properties/configurations";
    private static readonly string[] InvalidBooleans = ["null", "\"false\"", "0"];

    [Fact]
    public Task Records_require_root_versions_and_runtime_requires_explicit_snapshot() => WithServerAsync(async client =>
    {
        var definition = await CreateAsync(client, "Routes");
        using var added = await client.PostAsJsonAsync($"{Root}/{definition.Id}/records", Record(definition, "First", "false"));
        var mutation = await ReadAsync<DynamicRecordMutationDto>(added, HttpStatusCode.Created);
        Assert.Equal(definition.OptimisticVersion + 1, mutation.OptimisticVersion);
        Assert.NotEqual(definition.ConcurrencyVersion, mutation.ConcurrencyVersion);
        Assert.False(mutation.Record.Values["FLAG"].GetBoolean());
        using (var stale = await client.PostAsJsonAsync($"{Root}/{definition.Id}/records", Record(definition, "Second", "true")))
            await ErrorAsync(stale, HttpStatusCode.Conflict, "REF-CONCURRENCY-CONFLICT");
        using (var fetched = await client.GetAsync($"{Root}/{definition.Id}"))
        {
            definition = await ReadAsync<DynamicConfigurationDetailDto>(fetched);
            Assert.Equal(1, definition.RecordCount);
            using var json = JsonDocument.Parse(await fetched.Content.ReadAsStringAsync());
            Assert.False(json.RootElement.GetProperty("data").TryGetProperty("records", out _));
        }
        using (var published = await client.PostAsJsonAsync($"{Root}/{definition.Id}/publish", Version(definition)))
            definition = await ReadAsync<DynamicConfigurationDetailDto>(published);
        using (var schema = await client.GetAsync($"{Runtime}/routes/schema"))
        {
            var snapshot = await ReadAsync<DynamicSchemaDto>(schema);
            Assert.Equal(definition.Id, snapshot.DefinitionId);
            Assert.Equal("Tenant", snapshot.SourceScope);
        }
        using (var missing = await client.GetAsync($"{Runtime}/routes/records"))
            await ErrorAsync(missing, HttpStatusCode.BadRequest, "REF-DYNAMIC-CONFIG-REVISION-REQUIRED");
        using (var missing = await client.GetAsync($"{Runtime}/routes/records?revision=1"))
            await ErrorAsync(missing, HttpStatusCode.BadRequest, "REF-SCOPE-INVALID");
        using (var wrongTenant = await client.GetAsync($"{Runtime}/routes/records?revision=1&sourceScope=Tenant&sourceTenantNId=TENANT-B"))
            await ErrorAsync(wrongTenant, HttpStatusCode.NotFound, "REF-DYNAMIC-CONFIG-NOT-FOUND");
        using (var records = await client.GetAsync($"{Runtime}/routes/records?revision=1&sourceScope=Tenant&sourceTenantNId=TENANT-A"))
        {
            var page = await ReadAsync<PageResult<DynamicRecordDto>>(records);
            Assert.Equal("FIRST", Assert.Single(page.Items).NId);
            Assert.Equal(1, page.Total);
        }
        using (var record = await client.GetAsync($"{Runtime}/routes/records/first?revision=1&sourceScope=Tenant&sourceTenantNId=TENANT-A"))
            Assert.False((await ReadAsync<DynamicRecordDto>(record)).Values["FLAG"].GetBoolean());
        using var immutable = await client.PutAsJsonAsync($"{Root}/{definition.Id}/records/{mutation.Record.Id}", Record(definition, "First", "true"));
        await ErrorAsync(immutable, HttpStatusCode.Conflict, "REF-INVALID-STATE");
    });

    [Fact]
    public Task Record_creation_uses_update_permission_and_platform_and_tenant_guards_apply() => WithServerAsync(async client =>
    {
        var definition = await CreateAsync(client, "Permissions");
        var platform = await CreateAsync(client, "Shared", "Platform");
        client.DefaultRequestHeaders.Add("X-Test-Denied", ReferenceDataPermissions.DynamicPropertyUpdate);
        using (var denied = await client.PostAsJsonAsync($"{Root}/{definition.Id}/records", Record(definition, "First", "true")))
            await ErrorAsync(denied, HttpStatusCode.Forbidden, "ID_PERMISSION_DENIED");
        client.DefaultRequestHeaders.Remove("X-Test-Denied");
        client.DefaultRequestHeaders.Add("X-Test-Denied", ReferenceDataPermissions.DynamicPropertyCreate);
        using (var allowed = await client.PostAsJsonAsync($"{Root}/{definition.Id}/records", Record(definition, "First", "true")))
            await ReadAsync<DynamicRecordMutationDto>(allowed, HttpStatusCode.Created);
        client.DefaultRequestHeaders.Remove("X-Test-Denied");
        using (var currentResponse = await client.GetAsync($"{Root}/{definition.Id}")) definition = await ReadAsync<DynamicConfigurationDetailDto>(currentResponse);
        var currentRecord = (await ReadAsync<PageResult<DynamicRecordDto>>(await client.GetAsync($"{Root}/{definition.Id}/records"))).Items[0];
        client.DefaultRequestHeaders.Add("X-Test-Denied", ReferenceDataPermissions.DynamicPropertyDisable);
        using (var denied = await client.PutAsJsonAsync($"{Root}/{definition.Id}/records/{currentRecord.Id}", Record(definition, currentRecord.NId, "true") with { Enabled = false }))
            await ErrorAsync(denied, HttpStatusCode.Forbidden, "ID_PERMISSION_DENIED");
        client.DefaultRequestHeaders.Remove("X-Test-Denied");
        client.DefaultRequestHeaders.Add("X-Test-Denied", ReferenceDataPermissions.PlatformManage);
        using (var denied = await client.PostAsJsonAsync($"{Root}/{platform.Id}/records", Record(platform, "Denied", "true")))
            await ErrorAsync(denied, HttpStatusCode.Forbidden, "ID_PERMISSION_DENIED");
        client.DefaultRequestHeaders.Remove("X-Test-Denied");
        client.DefaultRequestHeaders.Add("X-Test-Tenant", "TENANT-B");
        using (var hidden = await client.GetAsync($"{Root}/{definition.Id}/records"))
            await ErrorAsync(hidden, HttpStatusCode.NotFound, "REF-DYNAMIC-CONFIG-NOT-FOUND");
        using (var hidden = await client.PostAsJsonAsync($"{Root}/{definition.Id}/records", Record(definition, "Hidden", "true")))
            await ErrorAsync(hidden, HttpStatusCode.NotFound, "REF-DYNAMIC-CONFIG-NOT-FOUND");
    });

    [Fact]
    public Task Explicit_null_wrong_type_and_factory_are_rejected_without_changes() => WithServerAsync(async client =>
    {
        var definition = await CreateAsync(client, "Invalid");
        foreach (var invalid in InvalidBooleans)
        {
            using var response = await client.PostAsJsonAsync($"{Root}/{definition.Id}/records", Record(definition, "Bad", invalid));
            await ErrorAsync(response, HttpStatusCode.UnprocessableEntity, "REF-DYNAMIC-CONFIG-FIELD-INVALID");
        }
        using (var factory = await client.GetAsync($"{Root}?scopeType=Factory"))
            await ErrorAsync(factory, HttpStatusCode.Conflict, "REF-SCOPE-FACTORY-NOT-READY");
        using (var factory = await client.GetAsync($"{Runtime}/invalid/schema?factoryId=F1"))
            await ErrorAsync(factory, HttpStatusCode.Conflict, "REF-SCOPE-FACTORY-NOT-READY");
        using var fetched = await client.GetAsync($"{Root}/{definition.Id}");
        Assert.Equal(JsonSerializer.Serialize(definition), JsonSerializer.Serialize(await ReadAsync<DynamicConfigurationDetailDto>(fetched)));
    });

    private static async Task<DynamicConfigurationDetailDto> CreateAsync(HttpClient client, string nId, string scope = "Tenant")
    {
        using var response = await client.PostAsJsonAsync(Root, new CreateDynamicConfigurationRequest(scope, null, nId, nId, null,
            [new("Flag", "Flag", "Boolean", true, true, 0, Json("false"))]));
        return await ReadAsync<DynamicConfigurationDetailDto>(response, HttpStatusCode.Created);
    }
    private static DynamicRecordRequest Record(DynamicConfigurationDetailDto definition, string nId, string value) =>
        new(nId, null, "Group", 0, true, new Dictionary<string, JsonElement> { ["Flag"] = Json(value) }, definition.OptimisticVersion, definition.ConcurrencyVersion);
    private static PublishOrDisableRequest Version(DynamicConfigurationDetailDto definition) => new(definition.OptimisticVersion, definition.ConcurrencyVersion, "Test state change");
    private static JsonElement Json(string text) { using var json = JsonDocument.Parse(text); return json.RootElement.Clone(); }
    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, HttpStatusCode status = HttpStatusCode.OK)
    {
        Assert.True(response.StatusCode == status, await response.Content.ReadAsStringAsync());
        var result = await response.Content.ReadFromJsonAsync<ApiResult<T>>();
        Assert.NotNull(result); Assert.True(result.Success); Assert.NotNull(result.Data);
        Assert.False(string.IsNullOrWhiteSpace(result.TraceId)); return result.Data;
    }
    private static async Task ErrorAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<ApiResult>();
        Assert.NotNull(result); Assert.False(result.Success); Assert.Equal(code, result.Code);
        Assert.False(string.IsNullOrWhiteSpace(result.TraceId));
    }

    private static async Task WithServerAsync(Func<HttpClient, Task> test)
    {
        var path = Path.Combine(Path.GetTempPath(), $"pf03-dynamic-http-{Guid.NewGuid():N}.db");
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
                    services.AddAuthentication(options => { options.DefaultAuthenticateScheme = "Test"; options.DefaultChallengeScheme = "Test"; })
                        .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>("Test", _ => { });
                    services.AddScoped<IReferenceDataPermissionEvaluator, TestPermissionEvaluator>();
                });
            });
            using var client = factory.CreateClient(); await test(client);
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
            var identity = new ClaimsIdentity([new Claim(ClaimConstants.UserNId, "USER-A"), new Claim(ClaimConstants.TenantId, tenant),
                new Claim(ClaimConstants.SessionId, "SESSION-A"), new Claim(ClaimConstants.AuthVersion, "1")], "Test");
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), "Test")));
        }
    }
}
