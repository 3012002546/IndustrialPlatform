using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using IndustrialPlatform.Infrastructure.Database;
using IndustrialPlatform.ReferenceData.Api.Authorization;
using IndustrialPlatform.ReferenceData.Api.Endpoints;
using IndustrialPlatform.ReferenceData.Application.Authorization;
using IndustrialPlatform.ReferenceData.Application.StateMachine;
using IndustrialPlatform.ReferenceData.Contracts;
using IndustrialPlatform.ReferenceData.Contracts.StateMachine;
using IndustrialPlatform.ReferenceData.Infrastructure.StateMachine;
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

public sealed class StateMachineHttpTests
{
    private const string Admin = "/api/v1/reference-data/admin/state-machines";
    private const string Runtime = "/api/v1/reference-data/state-machines";

    [Fact]
    public Task Management_available_current_fixed_and_evaluate_routes_complete_the_definition_flow() =>
        WithServerAsync(async client =>
        {
            var draft = await CreateAsync(client, "HttpOrder");
            Assert.Equal("Draft", draft.Status);
            using (var listed = await client.GetAsync($"{Admin}?pageIndex=1&pageSize=20&keyword=Http"))
                Assert.Contains((await ReadAsync<PageResult<StateMachineSummaryDto>>(listed)).Items,
                    item => item.Id == draft.Id);
            using (var published = await client.PostAsJsonAsync($"{Admin}/{draft.Id}/publish", Version(draft)))
                draft = await ReadAsync<StateMachineDetailDto>(published);

            using (var available = await client.GetAsync($"{Runtime}?pageIndex=1&pageSize=20&keyword=HttpOrder"))
                Assert.Contains((await ReadAsync<PageResult<AvailableStateMachineDto>>(available)).Items,
                    item => item.NId == draft.NId && item.SourceScope == "Tenant"
                        && item.SourceTenantNId == "TENANT-A");
            using (var missingSource = await client.GetAsync($"{Runtime}/HttpOrder"))
                await ErrorAsync(missingSource, HttpStatusCode.BadRequest, "REF-SCOPE-INVALID");
            using (var current = await client.GetAsync(
                $"{Runtime}/HttpOrder?sourceScope=Tenant&sourceTenantNId=TENANT-A"))
            {
                var definition = await ReadAsync<StateMachineDefinitionDto>(current);
                Assert.Equal(1, definition.Revision);
                Assert.Equal("Tenant", definition.SourceScope);
                Assert.Equal(2, definition.Nodes.Count);
            }
            using (var fixedRevision = await client.GetAsync(
                $"{Runtime}/HttpOrder/revisions/1?sourceScope=Tenant&sourceTenantNId=TENANT-A"))
                Assert.Equal(1, (await ReadAsync<StateMachineDefinitionDto>(fixedRevision)).Revision);

            using (var allowed = await client.PostAsJsonAsync($"{Runtime}/HttpOrder/evaluate",
                new TransitionEvaluationRequest("Tenant", "TENANT-A", 1, "draft", "finish")))
            {
                var result = await ReadAsync<TransitionEvaluationDto>(allowed);
                Assert.True(result.AllowedByDefinition);
                Assert.Equal("DONE", result.ToStatusNId);
                Assert.Equal("Tenant", result.SourceScope);
            }
            using (var denied = await client.PostAsJsonAsync($"{Runtime}/HttpOrder/evaluate",
                new TransitionEvaluationRequest("Tenant", "TENANT-A", 1, "draft", "cancel")))
            {
                var result = await ReadAsync<TransitionEvaluationDto>(denied);
                Assert.False(result.AllowedByDefinition);
                Assert.Equal("TRANSITION_NOT_DEFINED", result.ReasonCode);
            }
            using (var invalid = await client.PostAsJsonAsync($"{Runtime}/HttpOrder/evaluate",
                new TransitionEvaluationRequest("Tenant", "TENANT-A", 1, "draft", "bad action")))
                await ErrorAsync(invalid, HttpStatusCode.BadRequest, "REF-VALIDATION-FAILED");
        });

    [Fact]
    public Task Disabled_current_is_unavailable_but_its_ever_published_fixed_revision_and_evaluation_remain_readable() =>
        WithServerAsync(async client =>
        {
            var definition = await CreateAsync(client, "DisabledHistory");
            using (var published = await client.PostAsJsonAsync(
                $"{Admin}/{definition.Id}/publish", Version(definition)))
                definition = await ReadAsync<StateMachineDetailDto>(published);
            using (var disabled = await client.PostAsJsonAsync(
                $"{Admin}/{definition.Id}/disable", Version(definition, "No new selection")))
                definition = await ReadAsync<StateMachineDetailDto>(disabled);
            Assert.Equal("Disabled", definition.Status);

            using (var current = await client.GetAsync(
                $"{Runtime}/DisabledHistory?sourceScope=Tenant&sourceTenantNId=TENANT-A"))
                await ErrorAsync(current, HttpStatusCode.NotFound, "REF-STATE-MACHINE-NOT-FOUND");
            using (var fixedRevision = await client.GetAsync(
                $"{Runtime}/DisabledHistory/revisions/1?sourceScope=Tenant&sourceTenantNId=TENANT-A"))
                Assert.Equal("Disabled", (await ReadAsync<StateMachineDefinitionDto>(fixedRevision)).Status);
            using (var evaluation = await client.PostAsJsonAsync($"{Runtime}/DisabledHistory/evaluate",
                new TransitionEvaluationRequest("Tenant", "TENANT-A", 1, "draft", "finish")))
                Assert.True((await ReadAsync<TransitionEvaluationDto>(evaluation)).AllowedByDefinition);
        });

    [Fact]
    public Task Permissions_platform_factory_cross_tenant_and_invalid_graph_return_stable_errors() =>
        WithServerAsync(async client =>
        {
            client.DefaultRequestHeaders.Add("X-Test-Denied", ReferenceDataPermissions.StateMachineCreate);
            using (var denied = await client.PostAsJsonAsync(Admin, Request("Denied", "Tenant")))
                await ErrorAsync(denied, HttpStatusCode.Forbidden, "ID_PERMISSION_DENIED");
            client.DefaultRequestHeaders.Remove("X-Test-Denied");

            client.DefaultRequestHeaders.Add("X-Test-Denied", ReferenceDataPermissions.PlatformManage);
            using (var denied = await client.PostAsJsonAsync(Admin, Request("PlatformDenied", "Platform")))
                await ErrorAsync(denied, HttpStatusCode.Forbidden, "ID_PERMISSION_DENIED");
            client.DefaultRequestHeaders.Remove("X-Test-Denied");

            using (var factory = await client.PostAsJsonAsync(Admin, Request("FactoryDenied", "Factory")))
                await ErrorAsync(factory, HttpStatusCode.Conflict, "REF-SCOPE-FACTORY-NOT-READY");

            var privateDefinition = await CreateAsync(client, "PrivateFlow");
            using (var published = await client.PostAsJsonAsync(
                $"{Admin}/{privateDefinition.Id}/publish", Version(privateDefinition)))
                privateDefinition = await ReadAsync<StateMachineDetailDto>(published);
            client.DefaultRequestHeaders.Add("X-Test-Tenant", "TENANT-B");
            using (var hidden = await client.GetAsync($"{Admin}/{privateDefinition.Id}"))
                await ErrorAsync(hidden, HttpStatusCode.NotFound, "REF-STATE-MACHINE-NOT-FOUND");
            using (var hidden = await client.GetAsync(
                $"{Runtime}/PrivateFlow/revisions/1?sourceScope=Tenant&sourceTenantNId=TENANT-A"))
                await ErrorAsync(hidden, HttpStatusCode.NotFound, "REF-STATE-MACHINE-NOT-FOUND");
            client.DefaultRequestHeaders.Remove("X-Test-Tenant");

            var invalidRequest = Request("InvalidGraph", "Tenant") with
            {
                Nodes = [Node("one", initial: true), Node("two", initial: true)],
                Transitions = [Transition("one", "go", "Go", "two")],
            };
            using var created = await client.PostAsJsonAsync(Admin, invalidRequest);
            var invalid = await ReadAsync<StateMachineDetailDto>(created, HttpStatusCode.Created);
            using (var publicationCheck = await client.GetAsync(
                $"{Admin}/{invalid.Id}/publication-check"))
            {
                var check = await ReadAsync<StateMachinePublicationCheckDto>(publicationCheck);
                Assert.Equal("REF-STATE-MACHINE-INVALID", Assert.Single(check.Errors).Code);
                Assert.Equal("nodes", Assert.Single(check.Errors).Field);
            }
            using var publish = await client.PostAsJsonAsync($"{Admin}/{invalid.Id}/publish", Version(invalid));
            await ErrorAsync(publish, HttpStatusCode.UnprocessableEntity, "REF-STATE-MACHINE-INVALID");
        });

    [Fact]
    public Task Route_permissions_are_attached_to_the_exact_state_machine_actions() => WithServerAsync(async client =>
    {
        var draft = await CreateAsync(client, "PermissionFlow");
        foreach (var permission in new[]
        {
            ReferenceDataPermissions.StateMachineView,
            ReferenceDataPermissions.StateMachineUpdate,
            ReferenceDataPermissions.StateMachinePublish,
            ReferenceDataPermissions.StateMachineDisable,
        })
        {
            client.DefaultRequestHeaders.Add("X-Test-Denied", permission);
            HttpResponseMessage response;
            if (permission == ReferenceDataPermissions.StateMachineView)
                response = await client.GetAsync($"{Admin}/{draft.Id}");
            else if (permission == ReferenceDataPermissions.StateMachineUpdate)
                response = await client.PutAsJsonAsync($"{Admin}/{draft.Id}", new UpdateStateMachineRequest(
                    draft.Name, draft.Description, RequestNodes(), RequestTransitions(),
                    draft.OptimisticVersion, draft.ConcurrencyVersion));
            else if (permission == ReferenceDataPermissions.StateMachinePublish)
                response = await client.PostAsJsonAsync($"{Admin}/{draft.Id}/publish", Version(draft));
            else
                response = await client.PostAsJsonAsync($"{Admin}/{draft.Id}/disable", Version(draft));
            using (response) await ErrorAsync(response, HttpStatusCode.Forbidden, "ID_PERMISSION_DENIED");
            client.DefaultRequestHeaders.Remove("X-Test-Denied");
        }
    });

    private static async Task<StateMachineDetailDto> CreateAsync(HttpClient client, string nId)
    {
        using var response = await client.PostAsJsonAsync(Admin, Request(nId, "Tenant"));
        return await ReadAsync<StateMachineDetailDto>(response, HttpStatusCode.Created);
    }

    private static CreateStateMachineRequest Request(string nId, string scope) =>
        new(scope, nId, nId, null, RequestNodes(), RequestTransitions());

    private static StateNodeRequest[] RequestNodes() =>
        [Node("draft", initial: true), Node("done", terminal: true, outcome: "Success")];

    private static StateTransitionRequest[] RequestTransitions() =>
        [Transition("draft", "finish", "Finish", "done")];

    private static StateNodeRequest Node(
        string nId, bool initial = false, bool terminal = false, string outcome = "None") =>
        new(nId, nId, null, initial, terminal, outcome, "#0088FF", 0);

    private static StateTransitionRequest Transition(
        string from, string action, string actionName, string to) =>
        new(from, action, actionName, to, null);

    private static PublishOrDisableRequest Version(
        StateMachineDetailDto definition, string reason = "HTTP state machine test") =>
        new(definition.OptimisticVersion, definition.ConcurrencyVersion, reason);

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
        var path = Path.Combine(Path.GetTempPath(), $"pf03-state-machine-http-{Guid.NewGuid():N}.db");
        var context = new SqlSugarDbContext(Options.Create(new SqlSugarOptions
        {
            DbType = DbType.Sqlite,
            ConnectionString = $"Data Source={path};Pooling=False",
        }));
        WebApplication? app = null;
        try
        {
            await context.SqlSugar.Ado.ExecuteCommandAsync(
                "PRAGMA foreign_keys=ON;" + StateMachineMigration.Sql(false)
                + ReferenceDataSharedMigration.Sql(false)
                + ReferenceDataCacheGenerationMigration.Sql(false));
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseTestServer();
            builder.Services.AddSingleton(context);
            builder.Services.AddScoped<IStateMachineRepository, StateMachineRepository>();
            builder.Services.AddScoped<StateMachineService>();
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
            app.MapStateMachineEndpoints();
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
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var tenant = Request.Headers["X-Test-Tenant"].FirstOrDefault() ?? "TENANT-A";
            var identity = new ClaimsIdentity(
            [
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
