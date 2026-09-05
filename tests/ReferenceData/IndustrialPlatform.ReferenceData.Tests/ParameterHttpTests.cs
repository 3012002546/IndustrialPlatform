using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using IndustrialPlatform.ReferenceData.Application.Authorization;
using IndustrialPlatform.ReferenceData.Contracts;
using IndustrialPlatform.ReferenceData.Contracts.Parameter;
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

public sealed class ParameterHttpTests
{
    private const string Root = "/api/v1/reference-data/admin/configuration-domains";
    private const string EffectiveRoot = "/api/v1/reference-data/configuration-domains";
    private static readonly string[] ConfigurationModes = ["Single", "Multi"];

    [Fact]
    public Task Child_writes_return_root_versions_and_reject_either_stale_token() => WithServerAsync(async client =>
    {
        var domain = await CreateAsync(client, "Versions");
        Assert.Equal("TENANT-A", domain.TenantNId);
        Assert.Equal("Active", domain.Status);
        Assert.Equal(1L, domain.Revision);
        Assert.Empty(domain.Keys);
        var before = domain;
        domain = await AddKeyAsync(client, domain, NewKey(domain, "Ranges", "Integer", "Multi"));
        AssertAdvanced(before, domain);
        var key = Assert.Single(domain.Keys);
        var valuesPath = $"{Root}/{domain.Id}/keys/{key.Id}/values";

        before = domain;
        using (var added = await client.PostAsJsonAsync(valuesPath, NewValue(domain, "First", "1")))
            domain = await ReadAsync<ConfigurationDomainDetailDto>(added, HttpStatusCode.Created);
        AssertAdvanced(before, domain);
        var value = Assert.Single(Assert.Single(domain.Keys).MultiValues);
        before = domain;
        using (var updated = await client.PutAsJsonAsync($"{valuesPath}/{value.Id}", NewValue(domain, value.NId, "2")))
            domain = await ReadAsync<ConfigurationDomainDetailDto>(updated);
        AssertAdvanced(before, domain);
        Assert.Equal("2", Assert.Single(Assert.Single(domain.Keys).MultiValues).ValueJson);

        var rejected = NewValue(domain, value.NId, "3");
        using (var staleVersion = await client.PutAsJsonAsync($"{valuesPath}/{value.Id}", rejected with
        {
            ExpectedAppDomainOptimisticVersion = before.OptimisticVersion,
        }))
            await AssertErrorAsync(staleVersion, HttpStatusCode.Conflict, "REF-CONCURRENCY-CONFLICT");
        using (var staleToken = await client.PutAsJsonAsync($"{valuesPath}/{value.Id}", rejected with
        {
            ExpectedAppDomainConcurrencyVersion = before.ConcurrencyVersion,
        }))
            await AssertErrorAsync(staleToken, HttpStatusCode.Conflict, "REF-CONCURRENCY-CONFLICT");
        await AssertUnchangedAsync(client, domain, 4);

        using (var listed = await client.GetAsync(Root))
        {
            var page = await ReadAsync<PageResult<ConfigurationDomainSummaryDto>>(listed);
            Assert.Equal(1L, page.Total);
            Assert.Equal(1, Assert.Single(page.Items).KeyCount);
            using var json = JsonDocument.Parse(await listed.Content.ReadAsStringAsync());
            Assert.False(json.RootElement.GetProperty("data").GetProperty("items")[0].TryGetProperty("keys", out _));
        }

        before = domain;
        using (var disabled = await client.PostAsJsonAsync($"{valuesPath}/{value.Id}/disable", ChildVersion(domain)))
            domain = await ReadAsync<ConfigurationDomainDetailDto>(disabled);
        AssertAdvanced(before, domain);
        Assert.False(Assert.Single(Assert.Single(domain.Keys).MultiValues).Enabled);
        before = domain;
        using (var disabled = await client.PostAsJsonAsync($"{Root}/{domain.Id}/disable", DomainVersion(domain)))
            domain = await ReadAsync<ConfigurationDomainDetailDto>(disabled);
        AssertAdvanced(before, domain);
        Assert.Equal("Disabled", domain.Status);
        before = domain;
        using (var enabled = await client.PostAsJsonAsync($"{Root}/{domain.Id}/enable", DomainVersion(domain)))
            domain = await ReadAsync<ConfigurationDomainDetailDto>(enabled);
        AssertAdvanced(before, domain);
        Assert.Equal("Active", domain.Status);
        await AssertUnchangedAsync(client, domain, 7);
    });

    [Fact]
    public Task Effective_optional_null_is_explicit_and_blocks_platform_inheritance() => WithServerAsync(async client =>
    {
        var platform = await CreateAsync(client, "Optional", "Platform");
        await AddKeyAsync(client, platform, NewKey(platform, "Setting", "String") with { Value = Json("\"platform-value\"") });
        var tenant = await CreateAsync(client, "Optional");
        tenant = await AddKeyAsync(client, tenant, NewKey(tenant, "Setting", "String") with
        {
            Value = Json("null"), DefaultValue = Json("null"),
        });
        using (var response = await client.GetAsync($"{EffectiveRoot}/optional/keys/setting"))
        {
            var effective = await ReadAsync<EffectiveConfigurationDto>(response);
            Assert.Equal("Tenant", effective.SourceScope);
            Assert.Equal("TENANT-A", effective.SourceTenantNId);
            Assert.Equal("OPTIONAL.SETTING", effective.FullNId);
            Assert.True(effective.BlocksInheritance);
            Assert.False(effective.UsesDefaultValue);
            Assert.Null(effective.Value);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("data").GetProperty("value").ValueKind);
        }
        using (var response = await client.GetAsync($"{EffectiveRoot}/optional"))
        {
            await ReadAsync<EffectiveConfigurationDomainDto>(response);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("data").GetProperty("keys")[0].GetProperty("value").ValueKind);
        }
        using (var response = await client.GetAsync($"{Root}/{tenant.Id}"))
        {
            await ReadAsync<ConfigurationDomainDetailDto>(response);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var key = json.RootElement.GetProperty("data").GetProperty("keys")[0];
            Assert.Equal(JsonValueKind.Null, key.GetProperty("value").ValueKind);
            Assert.Equal(JsonValueKind.Null, key.GetProperty("defaultValue").ValueKind);
        }
        using var disabled = await client.PostAsJsonAsync($"{Root}/{tenant.Id}/keys/{Assert.Single(tenant.Keys).Id}/disable", ChildVersion(tenant));
        await ReadAsync<ConfigurationDomainDetailDto>(disabled);
        using var inherited = await client.GetAsync($"{EffectiveRoot}/optional/keys/setting");
        var fallback = await ReadAsync<EffectiveConfigurationDto>(inherited);
        Assert.Equal("Platform", fallback.SourceScope);
        Assert.Equal("platform-value", fallback.Value!.Value.GetString());
    });

    [Fact]
    public Task Invalid_scalar_types_and_missing_mandatory_values_have_distinct_errors_and_no_history() => WithServerAsync(async client =>
    {
        var domain = await CreateAsync(client, "Validation");
        foreach (var (type, value) in new[] { ("Boolean", "\"true\""), ("Integer", "1.5"), ("String", "42"), ("Decimal", "\"1.2\"") })
        {
            using var response = await client.PostAsJsonAsync($"{Root}/{domain.Id}/keys", NewKey(domain, "Invalid", type) with { Value = Json(value) });
            await AssertErrorAsync(response, HttpStatusCode.BadRequest, "REF-VALIDATION-FAILED", "value");
        }
        foreach (var mode in ConfigurationModes)
        {
            using var response = await client.PostAsJsonAsync($"{Root}/{domain.Id}/keys", NewKey(domain, "Required", "String", mode) with { IsMandatory = true });
            await AssertErrorAsync(response, HttpStatusCode.UnprocessableEntity, "REF-CONFIG-MANDATORY-VALUE-MISSING", "value");
        }
        await AssertUnchangedAsync(client, domain, 1);
    });

    [Fact]
    public Task Readonly_key_cannot_be_unlocked_disabled_or_changed_through_multi_endpoints() => WithServerAsync(async client =>
    {
        var domain = await CreateAsync(client, "Readonly");
        domain = await AddKeyAsync(client, domain, NewKey(domain, "Choices", "String", "Multi") with
        {
            IsReadOnly = true,
            InitialValues = [new("Fixed", "Fixed", Json("\"fixed\""), 0, false, true)],
        });
        var key = Assert.Single(domain.Keys);
        var value = Assert.Single(key.MultiValues);
        var keyPath = $"{Root}/{domain.Id}/keys/{key.Id}";
        using (var unlocked = await client.PutAsJsonAsync(keyPath, NewKey(domain, key.NId, "String", "Multi") with { IsReadOnly = false }))
            await AssertErrorAsync(unlocked, HttpStatusCode.Conflict, "REF-CONFIG-READ-ONLY");
        using (var disabled = await client.PostAsJsonAsync($"{keyPath}/disable", ChildVersion(domain)))
            await AssertErrorAsync(disabled, HttpStatusCode.Conflict, "REF-CONFIG-READ-ONLY");
        using (var added = await client.PostAsJsonAsync($"{keyPath}/values", NewValue(domain, "Another", "\"another\"")))
            await AssertErrorAsync(added, HttpStatusCode.Conflict, "REF-CONFIG-READ-ONLY");
        using (var updated = await client.PutAsJsonAsync($"{keyPath}/values/{value.Id}", NewValue(domain, value.NId, "\"changed\"")))
            await AssertErrorAsync(updated, HttpStatusCode.Conflict, "REF-CONFIG-READ-ONLY");
        using (var disabled = await client.PostAsJsonAsync($"{keyPath}/values/{value.Id}/disable", ChildVersion(domain)))
            await AssertErrorAsync(disabled, HttpStatusCode.Conflict, "REF-CONFIG-READ-ONLY");
        await AssertUnchangedAsync(client, domain, 2);
    });

    [Fact]
    public Task Permissions_tenant_isolation_and_factory_gate_apply_to_real_routes() => WithServerAsync(async client =>
    {
        var domain = await CreateAsync(client, "PrivateDomain");
        domain = await AddKeyAsync(client, domain, NewKey(domain, "Setting", "String"));
        var key = Assert.Single(domain.Keys);
        var platform = await CreateAsync(client, "SharedDomain", "Platform");

        client.DefaultRequestHeaders.Add("X-Test-Denied", ReferenceDataPermissions.ParameterView);
        using (var denied = await client.GetAsync(Root))
            await AssertErrorAsync(denied, HttpStatusCode.Forbidden, "ID_PERMISSION_DENIED");
        using (var denied = await client.GetAsync($"{EffectiveRoot}/privatedomain/keys/setting"))
            await AssertErrorAsync(denied, HttpStatusCode.Forbidden, "ID_PERMISSION_DENIED");
        client.DefaultRequestHeaders.Remove("X-Test-Denied");
        client.DefaultRequestHeaders.Add("X-Test-Denied", ReferenceDataPermissions.ParameterCreate);
        using (var denied = await client.PostAsJsonAsync($"{Root}/{domain.Id}/keys", NewKey(domain, "Denied", "String")))
            await AssertErrorAsync(denied, HttpStatusCode.Forbidden, "ID_PERMISSION_DENIED");
        client.DefaultRequestHeaders.Remove("X-Test-Denied");
        client.DefaultRequestHeaders.Add("X-Test-Denied", ReferenceDataPermissions.ParameterUpdate);
        using (var denied = await client.PutAsJsonAsync($"{Root}/{domain.Id}/keys/{key.Id}", NewKey(domain, key.NId, "String") with { Name = "Changed" }))
            await AssertErrorAsync(denied, HttpStatusCode.Forbidden, "ID_PERMISSION_DENIED");
        client.DefaultRequestHeaders.Remove("X-Test-Denied");
        client.DefaultRequestHeaders.Add("X-Test-Denied", ReferenceDataPermissions.ParameterDisable);
        using (var denied = await client.PostAsJsonAsync($"{Root}/{domain.Id}/disable", DomainVersion(domain)))
            await AssertErrorAsync(denied, HttpStatusCode.Forbidden, "ID_PERMISSION_DENIED");
        client.DefaultRequestHeaders.Remove("X-Test-Denied");

        client.DefaultRequestHeaders.Add("X-Test-Denied", ReferenceDataPermissions.PlatformManage);
        using (var denied = await client.PostAsJsonAsync(Root, new CreateConfigurationDomainRequest("Platform", null, "DeniedGlobal", "Denied", null, "Permission probe")))
            await AssertErrorAsync(denied, HttpStatusCode.Forbidden, "ID_PERMISSION_DENIED");
        using (var denied = await client.PostAsJsonAsync($"{Root}/{platform.Id}/keys", NewKey(platform, "Denied", "String")))
            await AssertErrorAsync(denied, HttpStatusCode.Forbidden, "ID_PERMISSION_DENIED");
        using (var allowed = await client.GetAsync($"{Root}/{platform.Id}"))
            await ReadAsync<ConfigurationDomainDetailDto>(allowed);
        await CreateAsync(client, "AllowedTenant");
        client.DefaultRequestHeaders.Remove("X-Test-Denied");

        client.DefaultRequestHeaders.Add("X-Test-Tenant", "TENANT-B");
        foreach (var path in new[] { $"{Root}/{domain.Id}", $"{Root}/{domain.Id}/history", $"{Root}/{domain.Id}/keys/{key.Id}/history", $"{EffectiveRoot}/privatedomain/keys/setting" })
        {
            using var hidden = await client.GetAsync(path);
            await AssertErrorAsync(hidden, HttpStatusCode.NotFound, "REF-CONFIG-DOMAIN-NOT-FOUND");
        }
        using (var hidden = await client.PostAsJsonAsync($"{Root}/{domain.Id}/keys", NewKey(domain, "Hidden", "String")))
            await AssertErrorAsync(hidden, HttpStatusCode.NotFound, "REF-CONFIG-DOMAIN-NOT-FOUND");
        using (var listed = await client.GetAsync(Root))
        {
            var page = await ReadAsync<PageResult<ConfigurationDomainSummaryDto>>(listed);
            Assert.Equal(platform.Id, Assert.Single(page.Items).Id);
        }
        client.DefaultRequestHeaders.Remove("X-Test-Tenant");

        using (var factory = await client.PostAsJsonAsync(Root, new CreateConfigurationDomainRequest("Factory", "FACTORY-A", "FactoryDomain", "Factory", null, "Factory probe")))
            await AssertErrorAsync(factory, HttpStatusCode.Conflict, "REF-SCOPE-FACTORY-NOT-READY");
        foreach (var path in new[] { $"{Root}?scopeType=Factory", $"{EffectiveRoot}/privatedomain?factoryId=FACTORY-A", $"{EffectiveRoot}/privatedomain/keys/setting?factoryId=FACTORY-A" })
        {
            using var factory = await client.GetAsync(path);
            await AssertErrorAsync(factory, HttpStatusCode.Conflict, "REF-SCOPE-FACTORY-NOT-READY");
        }
        await AssertUnchangedAsync(client, domain, 2);
        await AssertUnchangedAsync(client, platform, 1);
    });

    [Fact]
    public Task Update_permission_cannot_disable_through_put_but_allows_edits_without_a_disabling_transition() => WithServerAsync(async client =>
    {
        var domain = await CreateAsync(client, "DisablePermission");
        domain = await AddKeyAsync(client, domain, NewKey(domain, "Setting", "String") with { Value = Json("\"initial\"") });
        domain = await AddKeyAsync(client, domain, NewKey(domain, "Choices", "Integer", "Multi") with
        {
            InitialValues = [new("First", null, Json("1"), 0, false, true)],
        });
        var singleKey = Assert.Single(domain.Keys, item => item.NId == "SETTING");
        var multiKey = Assert.Single(domain.Keys, item => item.NId == "CHOICES");
        var value = Assert.Single(multiKey.MultiValues);
        var keyPath = $"{Root}/{domain.Id}/keys/{singleKey.Id}";
        var valuePath = $"{Root}/{domain.Id}/keys/{multiKey.Id}/values/{value.Id}";

        client.DefaultRequestHeaders.Add("X-Test-Denied", ReferenceDataPermissions.ParameterDisable);
        using (var denied = await client.PutAsJsonAsync(keyPath, NewKey(domain, "Setting", "String") with
        {
            Value = Json("\"must-not-save\""), Status = "Disabled",
        }))
            await AssertErrorAsync(denied, HttpStatusCode.Forbidden, "ID_PERMISSION_DENIED");
        using (var denied = await client.PutAsJsonAsync(valuePath, NewValue(domain, value.NId, "99") with { Enabled = false }))
            await AssertErrorAsync(denied, HttpStatusCode.Forbidden, "ID_PERMISSION_DENIED");
        await AssertUnchangedAsync(client, domain, 3);

        var before = domain;
        using (var updated = await client.PutAsJsonAsync(keyPath, NewKey(domain, "Setting", "String") with { Value = Json("\"edited\"") }))
            domain = await ReadAsync<ConfigurationDomainDetailDto>(updated);
        AssertAdvanced(before, domain);
        singleKey = Assert.Single(domain.Keys, item => item.Id == singleKey.Id);
        Assert.Equal("Active", singleKey.Status);
        Assert.Equal("edited", singleKey.Value!.Value.GetString());
        before = domain;
        using (var updated = await client.PutAsJsonAsync(valuePath, NewValue(domain, value.NId, "2")))
            domain = await ReadAsync<ConfigurationDomainDetailDto>(updated);
        AssertAdvanced(before, domain);
        value = Assert.Single(Assert.Single(domain.Keys, item => item.Id == multiKey.Id).MultiValues);
        Assert.True(value.Enabled);
        Assert.Equal("2", value.ValueJson);

        client.DefaultRequestHeaders.Remove("X-Test-Denied");
        using (var disabled = await client.PutAsJsonAsync(keyPath, NewKey(domain, "Setting", "String") with
        {
            Value = Json("\"edited\""), Status = "Disabled",
        }))
            domain = await ReadAsync<ConfigurationDomainDetailDto>(disabled);
        Assert.Equal("Disabled", Assert.Single(domain.Keys, item => item.Id == singleKey.Id).Status);
        using (var disabled = await client.PutAsJsonAsync(valuePath, NewValue(domain, value.NId, "2") with { Enabled = false }))
            domain = await ReadAsync<ConfigurationDomainDetailDto>(disabled);
        Assert.False(Assert.Single(Assert.Single(domain.Keys, item => item.Id == multiKey.Id).MultiValues).Enabled);

        client.DefaultRequestHeaders.Add("X-Test-Denied", ReferenceDataPermissions.ParameterDisable);
        before = domain;
        using (var updated = await client.PutAsJsonAsync(keyPath, NewKey(domain, "Setting", "String") with
        {
            Value = Json("\"edited-while-disabled\""), Status = "Disabled",
        }))
            domain = await ReadAsync<ConfigurationDomainDetailDto>(updated);
        AssertAdvanced(before, domain);
        singleKey = Assert.Single(domain.Keys, item => item.Id == singleKey.Id);
        Assert.Equal("Disabled", singleKey.Status);
        Assert.Equal("edited-while-disabled", singleKey.Value!.Value.GetString());
        before = domain;
        using (var updated = await client.PutAsJsonAsync(valuePath, NewValue(domain, value.NId, "3") with { Enabled = false }))
            domain = await ReadAsync<ConfigurationDomainDetailDto>(updated);
        AssertAdvanced(before, domain);
        value = Assert.Single(Assert.Single(domain.Keys, item => item.Id == multiKey.Id).MultiValues);
        Assert.False(value.Enabled);
        Assert.Equal("3", value.ValueJson);
        await AssertUnchangedAsync(client, domain, 9);
    });

    [Fact]
    public Task Sensitive_rejections_do_not_echo_inputs_or_append_history() => WithServerAsync(async client =>
    {
        const string acceptedValue = "ordinary-value-not-for-history";
        const string sensitiveValue = "Bearer pf03-http-sensitive-marker";
        const string sensitiveReason = "password=pf03-http-reason-marker";
        var domain = await CreateAsync(client, "Redaction");
        domain = await AddKeyAsync(client, domain, NewKey(domain, "Setting", "String") with { Value = Json(JsonSerializer.Serialize(acceptedValue)) });
        var key = Assert.Single(domain.Keys);
        var request = NewKey(domain, key.NId, "String") with { Value = Json(JsonSerializer.Serialize(sensitiveValue)) };
        using (var rejected = await client.PutAsJsonAsync($"{Root}/{domain.Id}/keys/{key.Id}", request))
        {
            await AssertErrorAsync(rejected, HttpStatusCode.BadRequest, "REF-CONFIG-SENSITIVE-REJECTED");
            Assert.DoesNotContain(sensitiveValue, await rejected.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        }
        using (var rejected = await client.PostAsJsonAsync($"{Root}/{domain.Id}/keys", NewKey(domain, "ApiKey", "String")))
        {
            await AssertErrorAsync(rejected, HttpStatusCode.BadRequest, "REF-CONFIG-SENSITIVE-REJECTED");
            Assert.DoesNotContain("ApiKey", await rejected.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
        }
        using (var rejected = await client.PostAsJsonAsync($"{Root}/{domain.Id}/disable", DomainVersion(domain) with { ChangeReason = sensitiveReason }))
        {
            await AssertErrorAsync(rejected, HttpStatusCode.BadRequest, "REF-CONFIG-SENSITIVE-REJECTED");
            Assert.DoesNotContain(sensitiveReason, await rejected.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        }
        await AssertUnchangedAsync(client, domain, 2);
        foreach (var path in new[] { $"{Root}/{domain.Id}/history", $"{Root}/{domain.Id}/keys/{key.Id}/history" })
        {
            using var response = await client.GetAsync(path);
            var history = await ReadAsync<PageResult<ConfigurationHistoryDto>>(response);
            Assert.All(history.Items, item =>
            {
                Assert.Equal("USER-A", item.UserNId);
                Assert.False(string.IsNullOrWhiteSpace(item.TraceId));
            });
            var body = await response.Content.ReadAsStringAsync();
            foreach (var marker in new[] { acceptedValue, sensitiveValue, sensitiveReason, "pf03-http-sensitive-marker", "pf03-http-reason-marker", "ApiKey" })
                Assert.DoesNotContain(marker, body, StringComparison.OrdinalIgnoreCase);
        }
    });

    private static async Task<ConfigurationDomainDetailDto> CreateAsync(HttpClient client, string nId, string scope = "Tenant")
    {
        using var response = await client.PostAsJsonAsync(Root, new CreateConfigurationDomainRequest(scope, null, nId, nId, null, "Create test domain"));
        return await ReadAsync<ConfigurationDomainDetailDto>(response, HttpStatusCode.Created);
    }

    private static async Task<ConfigurationDomainDetailDto> AddKeyAsync(HttpClient client, ConfigurationDomainDetailDto domain, ConfigurationKeyRequest request)
    {
        using var response = await client.PostAsJsonAsync($"{Root}/{domain.Id}/keys", request);
        return await ReadAsync<ConfigurationDomainDetailDto>(response, HttpStatusCode.Created);
    }

    private static ConfigurationKeyRequest NewKey(ConfigurationDomainDetailDto domain, string nId, string type, string mode = "Single") =>
        new(nId, nId, null, type, mode, null, null, false, false, null, null, "Active", 0, "Change test key", domain.OptimisticVersion, domain.ConcurrencyVersion);
    private static ConfigurationValueRequest NewValue(ConfigurationDomainDetailDto domain, string nId, string value) =>
        new(nId, null, Json(value), 0, false, true, "Change test value", domain.OptimisticVersion, domain.ConcurrencyVersion);
    private static ConfigurationChildStateRequest ChildVersion(ConfigurationDomainDetailDto domain) =>
        new("Change test state", domain.OptimisticVersion, domain.ConcurrencyVersion);
    private static ConfigurationDomainStateRequest DomainVersion(ConfigurationDomainDetailDto domain) =>
        new("Change test state", domain.OptimisticVersion, domain.ConcurrencyVersion);
    private static JsonElement Json(string value) { using var document = JsonDocument.Parse(value); return document.RootElement.Clone(); }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, HttpStatusCode status = HttpStatusCode.OK)
    {
        Assert.Equal(status, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<ApiResult<T>>();
        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.False(string.IsNullOrWhiteSpace(result.TraceId));
        Assert.NotNull(result.Data);
        return result.Data;
    }

    private static async Task AssertErrorAsync(HttpResponseMessage response, HttpStatusCode status, string code, string? field = null)
    {
        Assert.Equal(status, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<ApiResult>();
        Assert.NotNull(result);
        Assert.False(result.Success);
        Assert.Equal(code, result.Code);
        Assert.False(string.IsNullOrWhiteSpace(result.TraceId));
        if (field is not null)
        {
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal(field, json.RootElement.GetProperty("parameters").GetProperty("field").GetString());
        }
    }

    private static void AssertAdvanced(ConfigurationDomainDetailDto before, ConfigurationDomainDetailDto after)
    {
        Assert.Equal(before.Id, after.Id);
        Assert.Equal(before.Revision + 1, after.Revision);
        Assert.Equal(before.OptimisticVersion + 1, after.OptimisticVersion);
        Assert.NotEqual(before.ConcurrencyVersion, after.ConcurrencyVersion);
        Assert.True(after.LastUpdatedOn >= before.LastUpdatedOn);
    }

    private static async Task AssertUnchangedAsync(HttpClient client, ConfigurationDomainDetailDto before, long historyCount)
    {
        using var response = await client.GetAsync($"{Root}/{before.Id}");
        var after = await ReadAsync<ConfigurationDomainDetailDto>(response);
        Assert.Equal(JsonSerializer.Serialize(before), JsonSerializer.Serialize(after));
        using var historyResponse = await client.GetAsync($"{Root}/{before.Id}/history");
        var history = await ReadAsync<PageResult<ConfigurationHistoryDto>>(historyResponse);
        Assert.Equal(historyCount, history.Total);
    }

    private static async Task WithServerAsync(Func<HttpClient, Task> test)
    {
        var path = Path.Combine(Path.GetTempPath(), $"pf03-parameter-http-{Guid.NewGuid():N}.db");
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
            await test(client);
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
