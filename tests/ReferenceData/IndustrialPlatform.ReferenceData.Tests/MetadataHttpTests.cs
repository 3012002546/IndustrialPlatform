using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using IndustrialPlatform.Application.Abstractions.Initialization;
using IndustrialPlatform.Infrastructure.Database;
using IndustrialPlatform.ReferenceData.Api.Authorization;
using IndustrialPlatform.ReferenceData.Api.Endpoints;
using IndustrialPlatform.ReferenceData.Application.Authorization;
using IndustrialPlatform.ReferenceData.Application.Dictionary;
using IndustrialPlatform.ReferenceData.Application.Metadata;
using IndustrialPlatform.ReferenceData.Application.UnitOfMeasure;
using IndustrialPlatform.ReferenceData.Contracts;
using IndustrialPlatform.ReferenceData.Contracts.Metadata;
using IndustrialPlatform.ReferenceData.Infrastructure.Dictionary;
using IndustrialPlatform.ReferenceData.Infrastructure.Initialization;
using IndustrialPlatform.ReferenceData.Infrastructure.Metadata;
using IndustrialPlatform.ReferenceData.Infrastructure.UnitOfMeasure;
using IndustrialPlatform.Security;
using IndustrialPlatform.SharedKernel.Topology;
using IndustrialPlatform.Web.Results;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SqlSugar;

namespace IndustrialPlatform.ReferenceData.Tests;

public sealed class MetadataHttpTests
{
    private const string Admin = "/api/v1/reference-data/admin/metadata-schemas";
    private const string Runtime = "/api/v1/reference-data/metadata-schemas";

    [Fact]
    public Task Management_publish_effective_and_fixed_history_use_api_envelopes() => WithServerAsync(async client =>
    {
        var draft = await CreateAsync(client, "Equipment");
        Assert.Equal("Draft", draft.Status);
        using (var listed = await client.GetAsync($"{Admin}?pageIndex=1&pageSize=20&keyword=Equip"))
            Assert.Contains((await ReadAsync<PageResult<MetadataSchemaSummaryDto>>(listed)).Items, item => item.Id == draft.Id);
        using (var check = await client.GetAsync($"{Admin}/{draft.Id}/publication-check"))
            Assert.Empty((await ReadAsync<MetadataPublicationCheckDto>(check)).Errors);
        using (var published = await client.PostAsJsonAsync($"{Admin}/{draft.Id}/publish", Version(draft)))
            draft = await ReadAsync<MetadataSchemaDetailDto>(published);
        using (var effective = await client.GetAsync($"{Runtime}/Equipment"))
        {
            var schema = await ReadAsync<EffectiveSchemaDto>(effective);
            Assert.Equal("Tenant", schema.SourceScope);
            Assert.Equal("TENANT-A", schema.SourceTenantNId);
            Assert.Equal(["SERIAL"], schema.Attributes.Select(attribute => attribute.NId).ToArray());
        }
        using (var history = await client.GetAsync($"{Runtime}/Equipment/revisions/1?sourceScope=Tenant&sourceTenantNId=TENANT-A"))
            Assert.Equal(1, (await ReadAsync<EffectiveSchemaDto>(history)).Revision);

        using var clonedResponse = await client.PostAsJsonAsync($"{Admin}/{draft.Id}/clone", Version(draft));
        var clone = await ReadAsync<MetadataSchemaDetailDto>(clonedResponse);
        Assert.NotEqual(draft.Id, clone.Id);
        using var stale = await client.PutAsJsonAsync($"{Admin}/{clone.Id}", new UpdateMetadataSchemaRequest(
            "Changed", null, clone.Attributes.Select(ToRequest).ToArray(), clone.OptimisticVersion - 1,
            clone.ConcurrencyVersion));
        await ErrorAsync(stale, HttpStatusCode.Conflict, "REF-CONCURRENCY-CONFLICT");
    });

    [Fact]
    public Task Permission_platform_factory_cross_tenant_and_invalid_cross_references_are_rejected() => WithServerAsync(async client =>
    {
        client.DefaultRequestHeaders.Add("X-Test-Denied", ReferenceDataPermissions.MetadataCreate);
        using (var denied = await client.PostAsJsonAsync(Admin, Request("Denied", "Tenant")))
            await ErrorAsync(denied, HttpStatusCode.Forbidden, "ID_PERMISSION_DENIED");
        client.DefaultRequestHeaders.Remove("X-Test-Denied");
        client.DefaultRequestHeaders.Add("X-Test-Denied", ReferenceDataPermissions.PlatformManage);
        using (var platform = await client.PostAsJsonAsync(Admin, Request("PlatformDenied", "Platform")))
            await ErrorAsync(platform, HttpStatusCode.Forbidden, "ID_PERMISSION_DENIED");
        client.DefaultRequestHeaders.Remove("X-Test-Denied");
        using (var factory = await client.PostAsJsonAsync(Admin, Request("FactoryDenied", "Factory")))
            await ErrorAsync(factory, HttpStatusCode.Conflict, "REF-SCOPE-FACTORY-NOT-READY");

        var hidden = await CreateAsync(client, "TenantPrivate");
        client.DefaultRequestHeaders.Add("X-Test-Tenant", "TENANT-B");
        using (var response = await client.GetAsync($"{Admin}/{hidden.Id}"))
            await ErrorAsync(response, HttpStatusCode.NotFound, "REF-METADATA-NOT-FOUND");
        using (var response = await client.GetAsync($"{Runtime}/TenantPrivate/revisions/1?sourceScope=Tenant&sourceTenantNId=TENANT-A"))
            await ErrorAsync(response, HttpStatusCode.NotFound, "REF-METADATA-NOT-FOUND");
        client.DefaultRequestHeaders.Remove("X-Test-Tenant");

        var invalid = await CreateAsync(client, "InvalidDictionary",
            [Attribute("Mode", "Enum") with { DictionaryNId = "Missing" }]);
        using var publish = await client.PostAsJsonAsync($"{Admin}/{invalid.Id}/publish", Version(invalid));
        await ErrorAsync(publish, HttpStatusCode.UnprocessableEntity, "REF-METADATA-DICTIONARY-INVALID");
    });

    private static CreateMetadataSchemaRequest Request(string nId, string scope,
        IReadOnlyList<MetadataAttributeRequest>? attributes = null) =>
        new(scope, nId, nId, null, attributes ?? [Attribute("Serial", "String")]);
    private static MetadataAttributeRequest Attribute(string nId, string type) => new(
        nId, nId, type, false, false, true, 0, null, null, null, null, null, null, null, null,
        null, null, null, null, null, null, null, null);
    private static MetadataAttributeRequest ToRequest(MetadataAttributeDto attribute) => new(
        attribute.NId, attribute.Name, attribute.DataType, attribute.Required, attribute.IsArray, attribute.Enabled,
        attribute.Sort, attribute.DefaultValue, attribute.MinLength, attribute.MaxLength, attribute.MinValue,
        attribute.MaxValue, attribute.Pattern, attribute.DictionaryNId, attribute.ReferenceTarget, attribute.Precision,
        attribute.Scale, attribute.UnitDimensionNId, attribute.DefaultUnitNId, attribute.UnitRevision,
        attribute.UnitSourceScope, attribute.UnitSourceTenantNId, attribute.Description);
    private static Task<MetadataSchemaDetailDto> CreateAsync(HttpClient client, string nId,
        IReadOnlyList<MetadataAttributeRequest>? attributes = null) => PostAsync(client, Request(nId, "Tenant", attributes));
    private static async Task<MetadataSchemaDetailDto> PostAsync(HttpClient client, CreateMetadataSchemaRequest request)
    {
        using var response = await client.PostAsJsonAsync(Admin, request);
        return await ReadAsync<MetadataSchemaDetailDto>(response, HttpStatusCode.Created);
    }
    private static PublishOrDisableRequest Version(MetadataSchemaDetailDto schema) =>
        new(schema.OptimisticVersion, schema.ConcurrencyVersion, "HTTP test");
    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, HttpStatusCode status = HttpStatusCode.OK)
    {
        Assert.True(response.StatusCode == status, await response.Content.ReadAsStringAsync());
        var envelope = await response.Content.ReadFromJsonAsync<ApiResult<T>>();
        Assert.NotNull(envelope);
        Assert.True(envelope.Success);
        Assert.NotNull(envelope.Data);
        Assert.False(string.IsNullOrWhiteSpace(envelope.TraceId));
        return envelope.Data;
    }
    private static async Task ErrorAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        var envelope = await response.Content.ReadFromJsonAsync<ApiResult>();
        Assert.NotNull(envelope);
        Assert.False(envelope.Success);
        Assert.Equal(code, envelope.Code);
    }

    private static async Task WithServerAsync(Func<HttpClient, Task> test)
    {
        var path = Path.Combine(Path.GetTempPath(), $"pf03-metadata-http-{Guid.NewGuid():N}.db");
        var context = new SqlSugarDbContext(Options.Create(new SqlSugarOptions
        {
            DbType = DbType.Sqlite,
            ConnectionString = $"Data Source={path};Pooling=False;Foreign Keys=True",
        }));
        WebApplication? app = null;
        try
        {
            var initializer = new ReferenceDataServiceInitializer(new ReferenceDataInitializationLedger(context));
            var initialization = new ServiceInitializationContext("Test", "TENANT-A", "OP-METADATA-HTTP",
                "referencedata", "referencedata", new ResolvedDatabaseTarget("Test",
                    DatabaseTopologyMode.PerService, "referencedata", DatabaseProvider.Sqlite,
                    "referencedata_db", path, false), ReferenceDataServiceInitializer.CurrentVersion,
                ServiceInitializationPolicy.Standard, "metadata-http");
            var inspection = await initializer.InspectAsync(initialization, CancellationToken.None);
            var applied = await initializer.ApplyAsync(initialization,
                await initializer.PlanAsync(initialization, inspection, CancellationToken.None), CancellationToken.None);
            Assert.True(applied.Ready);
            await context.SqlSugar.Ado.ExecuteCommandAsync("PRAGMA foreign_keys=ON");
            if (await context.SqlSugar.Ado.GetIntAsync(
                    "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='reference_data_metadata_entity_schema'") == 0)
                await context.SqlSugar.Ado.ExecuteCommandAsync(MetadataMigration.Sql(false));
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseTestServer();
            builder.Services.AddSingleton(context);
            builder.Services.AddScoped<IMetadataSchemaRepository, MetadataSchemaRepository>();
            builder.Services.AddScoped<IDictionaryRepository, DictionaryRepository>();
            builder.Services.AddScoped<IUnitDimensionRepository, UnitDimensionRepository>();
            builder.Services.AddScoped<DictionaryService>();
            builder.Services.AddScoped<UnitDimensionService>();
            builder.Services.AddScoped<MetadataSchemaService>();
            builder.Services.AddHttpContextAccessor();
            builder.Services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = "Test";
                options.DefaultChallengeScheme = "Test";
            }).AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>("Test", _ => { });
            builder.Services.AddAuthorization();
            builder.Services.AddScoped<IReferenceDataPermissionEvaluator, TestPermissionEvaluator>();
            app = builder.Build();
            app.UseReferenceDataRequestErrors();
            app.UseAuthentication();
            app.UseAuthorization();
            app.MapMetadataEndpoints();
            await app.StartAsync();
            using var client = app.GetTestClient();
            await test(client);
        }
        finally
        {
            if (app is not null) await app.DisposeAsync();
            context.Dispose();
            SqliteConnection.ClearAllPools();
            if (File.Exists(path)) File.Delete(path);
        }
    }
    private sealed class TestPermissionEvaluator(IHttpContextAccessor accessor) : IReferenceDataPermissionEvaluator
    {
        public Task<ReferenceDataPermissionDecision> EvaluateAsync(ReferenceDataPermissionRequest request,
            CancellationToken cancellationToken) => Task.FromResult(
            accessor.HttpContext!.Request.Headers["X-Test-Denied"] == request.PermissionNId
                ? new ReferenceDataPermissionDecision(false, ReferenceDataPermissionDenialReason.MissingPermission)
                : new ReferenceDataPermissionDecision(true, ReferenceDataPermissionDenialReason.None));
    }
    private sealed class TestAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger, UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
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
