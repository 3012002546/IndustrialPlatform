using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using IndustrialPlatform.Infrastructure.Database;
using IndustrialPlatform.ReferenceData.Api.Authorization;
using IndustrialPlatform.ReferenceData.Api.Endpoints;
using IndustrialPlatform.ReferenceData.Application.Authorization;
using IndustrialPlatform.ReferenceData.Application.CodingRule;
using IndustrialPlatform.ReferenceData.Contracts;
using IndustrialPlatform.ReferenceData.Contracts.CodingRule;
using IndustrialPlatform.ReferenceData.Infrastructure.CodingRule;
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

public sealed class CodingRuleHttpTests
{
    private const string Admin = "/api/v1/reference-data/admin/coding-rules";
    private const string Runtime = "/api/v1/reference-data/coding-rules";

    [Fact]
    public Task Management_preview_generate_and_fixed_revision_routes_use_stable_envelopes() =>
        WithServerAsync(async client =>
        {
            var draft = await CreateAsync(client, "HttpLot");
            using (var listed = await client.GetAsync($"{Admin}?pageIndex=1&pageSize=20&keyword=Http"))
                Assert.Contains((await ReadAsync<PageResult<CodingRuleSummaryDto>>(listed)).Items,
                    item => item.Id == draft.Id);
            using (var previewed = await client.PostAsJsonAsync($"{Admin}/{draft.Id}/preview",
                new PreviewCodeRequest()))
            {
                var preview = await ReadAsync<CodePreviewDto>(previewed);
                Assert.Equal("H-TENANT-A-001", preview.Code);
                Assert.False(preview.ConsumesSequence);
            }
            using (var published = await client.PostAsJsonAsync($"{Admin}/{draft.Id}/publish", Version(draft)))
                draft = await ReadAsync<CodingRuleDetailDto>(published);

            using (var missingSource = await client.PostAsJsonAsync($"{Runtime}/HttpLot/preview",
                new PreviewCodeRequest(null, null, 1)))
                await ErrorAsync(missingSource, HttpStatusCode.BadRequest, "REF-SCOPE-INVALID");
            using (var fixedRevision = await client.PostAsJsonAsync($"{Runtime}/HttpLot/preview",
                new PreviewCodeRequest("Tenant", "TENANT-A", 1)))
                Assert.Equal(1, (await ReadAsync<CodePreviewDto>(fixedRevision)).RuleRevision);

            using (var missingKey = await client.PostAsJsonAsync($"{Runtime}/HttpLot/generate",
                new GenerateCodeRequest("Tenant", "TENANT-A", 1)))
                await ErrorAsync(missingKey, HttpStatusCode.BadRequest, "REF-IDEMPOTENCY-REQUIRED");
            var first = await GenerateAsync(client, "HttpLot", 1, "http-key");
            var replay = await GenerateAsync(client, "HttpLot", 1, "http-key");
            var second = await GenerateAsync(client, "HttpLot", 1, "http-key-2");
            Assert.Equal(first, replay);
            Assert.Equal(1, first.Sequence);
            Assert.Equal(2, second.Sequence);

            CodingRuleDetailDto clone;
            using (var cloned = await client.PostAsJsonAsync($"{Admin}/{draft.Id}/clone", Version(draft)))
                clone = await ReadAsync<CodingRuleDetailDto>(cloned);
            Assert.NotEqual(draft.Id, clone.Id);
            using (var published = await client.PostAsJsonAsync($"{Admin}/{clone.Id}/publish", Version(clone)))
                clone = await ReadAsync<CodingRuleDetailDto>(published);
            using (var oldRevision = await client.PostAsJsonAsync($"{Runtime}/HttpLot/preview",
                new PreviewCodeRequest("Tenant", "TENANT-A", 1)))
                Assert.Equal(1, (await ReadAsync<CodePreviewDto>(oldRevision)).RuleRevision);
            using (var conflict = GenerateRequest("HttpLot", 2, "http-key"))
            using (var response = await client.SendAsync(conflict))
                await ErrorAsync(response, HttpStatusCode.Conflict, "REF-IDEMPOTENCY-CONFLICT");
            using (var disabled = await client.PostAsJsonAsync($"{Admin}/{clone.Id}/disable", Version(clone)))
                clone = await ReadAsync<CodingRuleDetailDto>(disabled);
            using (var disabledRevision = await client.PostAsJsonAsync($"{Runtime}/HttpLot/preview",
                new PreviewCodeRequest("Tenant", "TENANT-A", clone.Revision)))
            {
                var historical = await ReadAsync<CodePreviewDto>(disabledRevision);
                Assert.Equal(2, historical.RuleRevision);
                Assert.Equal("Disabled", clone.Status);
            }
        });

    [Fact]
    public Task Every_coding_permission_platform_manage_factory_and_tenant_boundaries_are_enforced() =>
        WithServerAsync(async client =>
        {
            client.DefaultRequestHeaders.Add("X-Test-Denied", ReferenceDataPermissions.CodingRuleView);
            using (var denied = await client.GetAsync(Admin))
                await ErrorAsync(denied, HttpStatusCode.Forbidden, "ID_PERMISSION_DENIED");
            client.DefaultRequestHeaders.Remove("X-Test-Denied");

            client.DefaultRequestHeaders.Add("X-Test-Denied", ReferenceDataPermissions.CodingRuleCreate);
            using (var denied = await client.PostAsJsonAsync(Admin, Request("CreateDenied", "Tenant")))
                await ErrorAsync(denied, HttpStatusCode.Forbidden, "ID_PERMISSION_DENIED");
            client.DefaultRequestHeaders.Remove("X-Test-Denied");

            client.DefaultRequestHeaders.Add("X-Test-Denied", ReferenceDataPermissions.PlatformManage);
            using (var denied = await client.PostAsJsonAsync(Admin, Request("PlatformDenied", "Platform")))
                await ErrorAsync(denied, HttpStatusCode.Forbidden, "ID_PERMISSION_DENIED");
            client.DefaultRequestHeaders.Remove("X-Test-Denied");

            using (var factory = await client.PostAsJsonAsync(Admin, Request("FactoryDenied", "Factory")))
                await ErrorAsync(factory, HttpStatusCode.Conflict, "REF-SCOPE-FACTORY-NOT-READY");

            var draft = await CreateAsync(client, "PermissionRule");
            client.DefaultRequestHeaders.Add("X-Test-Denied", ReferenceDataPermissions.CodingRuleUpdate);
            using (var denied = await client.PutAsJsonAsync($"{Admin}/{draft.Id}", new UpdateCodingRuleRequest(
                draft.Name, draft.TargetEntityNId, draft.Template, draft.ResetPolicy,
                draft.OptimisticVersion, draft.ConcurrencyVersion)))
                await ErrorAsync(denied, HttpStatusCode.Forbidden, "ID_PERMISSION_DENIED");
            client.DefaultRequestHeaders.Remove("X-Test-Denied");

            client.DefaultRequestHeaders.Add("X-Test-Denied", ReferenceDataPermissions.CodingRulePreview);
            using (var denied = await client.PostAsJsonAsync($"{Admin}/{draft.Id}/preview",
                new PreviewCodeRequest()))
                await ErrorAsync(denied, HttpStatusCode.Forbidden, "ID_PERMISSION_DENIED");
            client.DefaultRequestHeaders.Remove("X-Test-Denied");

            client.DefaultRequestHeaders.Add("X-Test-Denied", ReferenceDataPermissions.CodingRulePublish);
            using (var denied = await client.PostAsJsonAsync($"{Admin}/{draft.Id}/publish", Version(draft)))
                await ErrorAsync(denied, HttpStatusCode.Forbidden, "ID_PERMISSION_DENIED");
            client.DefaultRequestHeaders.Remove("X-Test-Denied");
            using (var published = await client.PostAsJsonAsync($"{Admin}/{draft.Id}/publish", Version(draft)))
                draft = await ReadAsync<CodingRuleDetailDto>(published);

            client.DefaultRequestHeaders.Add("X-Test-Denied", ReferenceDataPermissions.CodingRuleGenerate);
            using (var deniedRequest = GenerateRequest("PermissionRule", 1, "denied"))
            using (var denied = await client.SendAsync(deniedRequest))
                await ErrorAsync(denied, HttpStatusCode.Forbidden, "ID_PERMISSION_DENIED");
            client.DefaultRequestHeaders.Remove("X-Test-Denied");

            client.DefaultRequestHeaders.Add("X-Test-Denied", ReferenceDataPermissions.CodingRuleDisable);
            using (var denied = await client.PostAsJsonAsync($"{Admin}/{draft.Id}/disable", Version(draft)))
                await ErrorAsync(denied, HttpStatusCode.Forbidden, "ID_PERMISSION_DENIED");
            client.DefaultRequestHeaders.Remove("X-Test-Denied");

            client.DefaultRequestHeaders.Add("X-Test-Tenant", "TENANT-B");
            using (var hidden = await client.GetAsync($"{Admin}/{draft.Id}"))
                await ErrorAsync(hidden, HttpStatusCode.NotFound, "REF-CODING-RULE-NOT-FOUND");
            using (var hidden = await client.PostAsJsonAsync($"{Runtime}/PermissionRule/preview",
                new PreviewCodeRequest("Tenant", "TENANT-A", 1)))
                await ErrorAsync(hidden, HttpStatusCode.NotFound, "REF-CODING-RULE-NOT-FOUND");
        });

    [Fact]
    public Task Invalid_templates_and_factory_context_return_documented_errors() => WithServerAsync(async client =>
    {
        CodingRuleDetailDto invalid;
        using (var created = await client.PostAsJsonAsync(Admin,
            Request("BadTemplate", "Tenant") with { Template = "{UNKNOWN}-{SEQ:2}" }))
            invalid = await ReadAsync<CodingRuleDetailDto>(created, HttpStatusCode.Created);
        using (var published = await client.PostAsJsonAsync($"{Admin}/{invalid.Id}/publish", Version(invalid)))
            await ErrorAsync(published, HttpStatusCode.UnprocessableEntity, "REF-CODING-TEMPLATE-INVALID");

        CodingRuleDetailDto factory;
        using (var created = await client.PostAsJsonAsync(Admin,
            Request("FactoryToken", "Tenant") with { Template = "{FACTORY}-{SEQ:2}" }))
            factory = await ReadAsync<CodingRuleDetailDto>(created, HttpStatusCode.Created);
        using (var published = await client.PostAsJsonAsync($"{Admin}/{factory.Id}/publish", Version(factory)))
            await ErrorAsync(published, HttpStatusCode.Conflict, "REF-SCOPE-FACTORY-NOT-READY");
    });

    private static async Task<CodingRuleDetailDto> CreateAsync(HttpClient client, string nId)
    {
        using var response = await client.PostAsJsonAsync(Admin, Request(nId, "Tenant"));
        return await ReadAsync<CodingRuleDetailDto>(response, HttpStatusCode.Created);
    }

    private static CreateCodingRuleRequest Request(string nId, string scope) =>
        new(scope, nId, nId, "WorkOrder", "H-{TENANT}-{SEQ:3}", "Never");

    private static PublishOrDisableRequest Version(CodingRuleDetailDto rule) =>
        new(rule.OptimisticVersion, rule.ConcurrencyVersion, "HTTP test");

    private static async Task<GeneratedCodeDto> GenerateAsync(
        HttpClient client, string nId, int revision, string key)
    {
        using var request = GenerateRequest(nId, revision, key);
        using var response = await client.SendAsync(request);
        return await ReadAsync<GeneratedCodeDto>(response);
    }

    private static HttpRequestMessage GenerateRequest(string nId, int revision, string key)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"{Runtime}/{nId}/generate")
        {
            Content = JsonContent.Create(new GenerateCodeRequest("Tenant", "TENANT-A", revision)),
        };
        request.Headers.TryAddWithoutValidation("Idempotency-Key", key);
        return request;
    }

    private static async Task<T> ReadAsync<T>(
        HttpResponseMessage response, HttpStatusCode status = HttpStatusCode.OK)
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
        var path = Path.Combine(Path.GetTempPath(), $"pf03-coding-http-{Guid.NewGuid():N}.db");
        var context = new SqlSugarDbContext(Options.Create(new SqlSugarOptions
        {
            DbType = DbType.Sqlite,
            ConnectionString = $"Data Source={path};Pooling=False",
        }));
        WebApplication? app = null;
        try
        {
            await context.SqlSugar.Ado.ExecuteCommandAsync(
                "PRAGMA foreign_keys=ON;" + CodingRuleMigration.Sql(false)
                + ReferenceDataSharedMigration.Sql(false)
                + ReferenceDataCacheGenerationMigration.Sql(false));
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseTestServer();
            builder.Services.AddSingleton(context);
            builder.Services.AddScoped<ICodingRuleRepository, CodingRuleRepository>();
            builder.Services.AddScoped<CodingRuleService>();
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
            app.MapCodingRuleEndpoints();
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
        public Task<ReferenceDataPermissionDecision> EvaluateAsync(
            ReferenceDataPermissionRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(accessor.HttpContext!.Request.Headers["X-Test-Denied"] == request.PermissionNId
                ? new ReferenceDataPermissionDecision(false, ReferenceDataPermissionDenialReason.MissingPermission)
                : new ReferenceDataPermissionDecision(true, ReferenceDataPermissionDenialReason.None));
    }

    private sealed class TestAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var tenant = Request.Headers["X-Test-Tenant"].FirstOrDefault() ?? "TENANT-A";
            var identity = new ClaimsIdentity([
                new Claim(ClaimConstants.UserNId, "USER-A"),
                new Claim(ClaimConstants.TenantId, tenant),
                new Claim(ClaimConstants.SessionId, "SESSION-A"),
                new Claim(ClaimConstants.AuthVersion, "1"),
            ], "Test");
            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(new ClaimsPrincipal(identity), "Test")));
        }
    }
}
