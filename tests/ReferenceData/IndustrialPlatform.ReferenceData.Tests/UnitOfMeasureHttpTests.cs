using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using IndustrialPlatform.Infrastructure.Database;
using IndustrialPlatform.ReferenceData.Api.Authorization;
using IndustrialPlatform.ReferenceData.Api.Endpoints;
using IndustrialPlatform.ReferenceData.Application.Authorization;
using IndustrialPlatform.ReferenceData.Application.UnitOfMeasure;
using IndustrialPlatform.ReferenceData.Contracts;
using IndustrialPlatform.ReferenceData.Contracts.UnitOfMeasure;
using IndustrialPlatform.ReferenceData.Infrastructure.UnitOfMeasure;
using IndustrialPlatform.ReferenceData.Infrastructure.Persistence;
using IndustrialPlatform.Security;
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

public sealed class UnitOfMeasureHttpTests
{
    private const string Admin = "/api/v1/reference-data/admin/units-of-measure/dimensions";
    private const string Runtime = "/api/v1/reference-data/units-of-measure";

    [Fact]
    public Task Management_current_fixed_and_conversion_routes_use_decimal_strings_and_explicit_sources() => WithServerAsync(async client =>
    {
        var draft = await CreateAsync(client, "HttpLength");
        Assert.Equal("Draft", draft.Status);
        using (var listed = await client.GetAsync($"{Admin}?pageIndex=1&pageSize=20&keyword=Http"))
            Assert.Contains((await ReadAsync<PageResult<UnitDimensionSummaryDto>>(listed)).Items, item => item.Id == draft.Id);
        using (var published = await client.PostAsJsonAsync($"{Admin}/{draft.Id}/publish", Version(draft)))
            draft = await ReadAsync<UnitDimensionDetailDto>(published);

        using (var available = await client.GetAsync($"{Runtime}/dimensions?pageIndex=1&pageSize=100&keyword=HttpLength"))
            Assert.Contains((await ReadAsync<PageResult<AvailableUnitDimensionDto>>(available)).Items,
                item => item.NId == draft.NId && item.SourceScope == "Tenant" && item.SourceTenantNId == "TENANT-A");
        using (var missingSource = await client.GetAsync($"{Runtime}/dimensions/HttpLength"))
            await ErrorAsync(missingSource, HttpStatusCode.BadRequest, "REF-SCOPE-INVALID");
        using (var current = await client.GetAsync($"{Runtime}/dimensions/HttpLength?sourceScope=Tenant&sourceTenantNId=TENANT-A"))
        {
            var runtime = await ReadAsync<UnitDimensionDto>(current);
            Assert.Equal(draft.NId, runtime.NId);
            Assert.Equal("Tenant", runtime.SourceScope);
            Assert.Equal(draft.Revision, runtime.Revision);
        }
        using (var fixedRevision = await client.GetAsync($"{Runtime}/dimensions/HttpLength/revisions/1?sourceScope=Tenant&sourceTenantNId=TENANT-A"))
            Assert.Equal(1, (await ReadAsync<UnitDimensionDto>(fixedRevision)).Revision);
        using (var converted = await client.PostAsJsonAsync($"{Runtime}/convert",
            new UnitConversionRequest("Tenant", "TENANT-A", "HttpLength", 1, "m", "mm", "1.234567")))
        {
            var result = await ReadAsync<UnitConversionResultDto>(converted);
            Assert.Equal("1234.567", result.ResultValue);
            Assert.Equal("0.001", result.ConversionSnapshot.TargetFactorToBase);
        }
    });

    [Fact]
    public Task Permission_platform_tenant_factory_and_system_guards_return_stable_envelopes() => WithServerAsync(async client =>
    {
        client.DefaultRequestHeaders.Add("X-Test-Denied", ReferenceDataPermissions.UnitOfMeasureCreate);
        using (var denied = await client.PostAsJsonAsync(Admin, Request("Denied", "Tenant")))
            await ErrorAsync(denied, HttpStatusCode.Forbidden, "ID_PERMISSION_DENIED");
        client.DefaultRequestHeaders.Remove("X-Test-Denied");

        client.DefaultRequestHeaders.Add("X-Test-Denied", ReferenceDataPermissions.PlatformManage);
        using (var denied = await client.PostAsJsonAsync(Admin, Request("PlatformDenied", "Platform")))
            await ErrorAsync(denied, HttpStatusCode.Forbidden, "ID_PERMISSION_DENIED");
        client.DefaultRequestHeaders.Remove("X-Test-Denied");

        using (var factory = await client.PostAsJsonAsync(Admin, Request("FactoryDenied", "Factory")))
            await ErrorAsync(factory, HttpStatusCode.Conflict, "REF-SCOPE-FACTORY-NOT-READY");

        var tenant = await CreateAsync(client, "PrivateLength");
        client.DefaultRequestHeaders.Add("X-Test-Tenant", "TENANT-B");
        using (var hidden = await client.GetAsync($"{Admin}/{tenant.Id}"))
            await ErrorAsync(hidden, HttpStatusCode.NotFound, "REF-UNIT-DIMENSION-NOT-FOUND");
        using (var hidden = await client.GetAsync($"{Runtime}/dimensions/PrivateLength/revisions/1?sourceScope=Tenant&sourceTenantNId=TENANT-A"))
            await ErrorAsync(hidden, HttpStatusCode.NotFound, "REF-UNIT-DIMENSION-NOT-FOUND");
        client.DefaultRequestHeaders.Remove("X-Test-Tenant");

        UnitDimensionSummaryDto massSummary;
        using (var response = await client.GetAsync($"{Admin}?pageIndex=1&pageSize=100&keyword=Mass&scopeType=Platform&status=Published"))
            massSummary = (await ReadAsync<PageResult<UnitDimensionSummaryDto>>(response)).Items.Single(item => item.IsSystemDefined && item.NId == "MASS");
        UnitDimensionDetailDto mass;
        using (var response = await client.GetAsync($"{Admin}/{massSummary.Id}"))
            mass = await ReadAsync<UnitDimensionDetailDto>(response);
        using var systemWrite = await client.PutAsJsonAsync($"{Admin}/{mass.Id}", new UpdateUnitDimensionRequest(
            "Changed", null, mass.ConversionKind, mass.BaseUnitNId,
            mass.Units.Select(unit => new UnitDefinitionRequest(unit.NId, unit.Name, unit.Symbol, unit.FactorToBase,
                unit.OffsetToBase, unit.DecimalPlaces, unit.RoundingMode, unit.Enabled, unit.Sort)).ToArray(),
            mass.OptimisticVersion, mass.ConcurrencyVersion));
        await ErrorAsync(systemWrite, HttpStatusCode.Conflict, "REF-UNIT-SYSTEM-DEFINED");
    });

    [Fact]
    public Task Conversion_rejects_cross_revision_units_invalid_numbers_and_overflow_without_writes() => WithServerAsync(async client =>
    {
        var draft = await CreateAsync(client, "Bounds", factor: "2");
        using (var published = await client.PostAsJsonAsync($"{Admin}/{draft.Id}/publish", Version(draft)))
            draft = await ReadAsync<UnitDimensionDetailDto>(published);
        using (var unknown = await client.PostAsJsonAsync($"{Runtime}/convert",
            new UnitConversionRequest("Tenant", "TENANT-A", "Bounds", 1, "other", "base", "1")))
            await ErrorAsync(unknown, HttpStatusCode.UnprocessableEntity, "REF-UNIT-CONVERSION-INVALID");
        using (var invalid = await client.PostAsJsonAsync($"{Runtime}/convert",
            new UnitConversionRequest("Tenant", "TENANT-A", "Bounds", 1, "large", "base", "1e2")))
            await ErrorAsync(invalid, HttpStatusCode.UnprocessableEntity, "REF-UNIT-CONVERSION-INVALID");
        using (var overflow = await client.PostAsJsonAsync($"{Runtime}/convert",
            new UnitConversionRequest("Tenant", "TENANT-A", "Bounds", 1, "large", "base", decimal.MaxValue.ToString(System.Globalization.CultureInfo.InvariantCulture))))
            await ErrorAsync(overflow, HttpStatusCode.UnprocessableEntity, "REF-UNIT-NUMERIC-OVERFLOW");
        using (var wrongRevision = await client.PostAsJsonAsync($"{Runtime}/convert",
            new UnitConversionRequest("Tenant", "TENANT-A", "Bounds", 2, "large", "base", "1")))
            await ErrorAsync(wrongRevision, HttpStatusCode.NotFound, "REF-UNIT-DIMENSION-NOT-FOUND");
    });

    [Fact]
    public Task Current_selection_only_returns_enabled_units_while_fixed_history_keeps_the_snapshot() => WithServerAsync(async client =>
    {
        var request = Request("SelectableLength", "Tenant") with
        {
            Units = Request("SelectableLength", "Tenant").Units
                .Select(unit => unit.NId == "large" ? unit with { Enabled = false } : unit)
                .ToArray(),
        };
        using var createdResponse = await client.PostAsJsonAsync(Admin, request);
        var dimension = await ReadAsync<UnitDimensionDetailDto>(createdResponse, HttpStatusCode.Created);
        using (var published = await client.PostAsJsonAsync($"{Admin}/{dimension.Id}/publish", Version(dimension)))
            dimension = await ReadAsync<UnitDimensionDetailDto>(published);

        using (var current = await client.GetAsync($"{Runtime}/dimensions/SelectableLength?sourceScope=Tenant&sourceTenantNId=TENANT-A"))
        {
            var selected = await ReadAsync<UnitDimensionDto>(current);
            Assert.DoesNotContain(selected.Units, unit => unit.NId == "LARGE");
            Assert.All(selected.Units, unit => Assert.True(unit.Enabled));
        }
        using (var fixedRevision = await client.GetAsync($"{Runtime}/dimensions/SelectableLength/revisions/1?sourceScope=Tenant&sourceTenantNId=TENANT-A"))
        {
            var historical = await ReadAsync<UnitDimensionDto>(fixedRevision);
            Assert.Contains(historical.Units, unit => unit.NId == "LARGE" && !unit.Enabled);
        }
    });

    private static async Task<UnitDimensionDetailDto> CreateAsync(HttpClient client, string nId, string factor = "0.001")
    {
        using var response = await client.PostAsJsonAsync(Admin, Request(nId, "Tenant", factor));
        return await ReadAsync<UnitDimensionDetailDto>(response, HttpStatusCode.Created);
    }
    private static CreateUnitDimensionRequest Request(string nId, string scope, string factor = "0.001") =>
        new(scope, null, nId, nId, null, "Ratio", "base",
        [
            new("base", "Base", "b", "1", "0", 3, "ToEven", true, 0),
            new("large", "Large", "l", factor, "0", 3, "AwayFromZero", true, 1),
            new("mm", "Millimetre", "mm", "0.001", "0", 3, "ToEven", true, 2),
            new("m", "Metre", "m", "1", "0", 3, "ToEven", true, 3),
        ]);
    private static PublishOrDisableRequest Version(UnitDimensionDetailDto dto) =>
        new(dto.OptimisticVersion, dto.ConcurrencyVersion, "HTTP test");
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
        Assert.False(string.IsNullOrWhiteSpace(envelope.TraceId));
    }

    private static async Task WithServerAsync(Func<HttpClient, Task> test)
    {
        var path = Path.Combine(Path.GetTempPath(), $"pf03-uom-http-{Guid.NewGuid():N}.db");
        var context = new SqlSugarDbContext(Options.Create(new SqlSugarOptions
        {
            DbType = DbType.Sqlite,
            ConnectionString = $"Data Source={path};Pooling=False",
        }));
        WebApplication? app = null;
        try
        {
            await context.SqlSugar.Ado.ExecuteCommandAsync("PRAGMA foreign_keys=ON;"
                + UnitOfMeasureMigration.Sql(false) + ReferenceDataSharedMigration.Sql(false)
                + ReferenceDataCacheGenerationMigration.Sql(false));
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseTestServer();
            builder.Services.AddSingleton(context);
            builder.Services.AddScoped<IUnitDimensionRepository, UnitDimensionRepository>();
            builder.Services.AddScoped<UnitDimensionService>();
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
            app.MapUnitOfMeasureEndpoints();
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
