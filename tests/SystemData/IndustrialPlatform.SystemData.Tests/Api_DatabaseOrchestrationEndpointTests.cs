using System.Net;
using System.Text;
using System.Text.Json;
using IndustrialPlatform.Security;
using IndustrialPlatform.SystemData.Application.DatabaseOrchestration;
using IndustrialPlatform.SystemData.Application.Authorization;
using IndustrialPlatform.SystemData.Domain.DatabaseOrchestration;
using IndustrialPlatform.SharedKernel.Topology;
using IndustrialPlatform.SystemData.Domain.Topology;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace IndustrialPlatform.SystemData.Api.Tests;

/// <summary>
/// 数据库编排控制面端点全栈测试(05 方案 §9.2):真实 Program/控制器/服务,
/// 以假存储与可信拓扑替换端口,验证统一 ApiResult 信封(202/200/400/404/409/503)、
/// 401(无 actor)、幂等键冲突与 readiness 形状。
/// SD-006 接入 [Authorize] 权限策略后,以测试方案注入编排/初始化权限通过授权管线。
/// </summary>
public sealed class DatabaseOrchestrationEndpointTests
{
    private const string Tenant = "tenant-001";
    private const string Actor = "user-actor";

    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    // ===== 注册清单 =====

    [Fact]
    public async Task Register_WithoutActor_Returns401Envelope()
    {
        using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();

        using var response = await SendAsync(
            client,
            HttpMethod.Put,
            "/api/v1/database-orchestration/registrations/systemdata",
            Manifest());
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("401", payload.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Register_ValidManifest_ReturnsRegistrationEnvelope()
    {
        var store = new FakeDatabaseOrchestrationStore();
        using var factory = CreateFactory(store, DevelopmentTopology);
        using var client = factory.CreateClient();

        using var response = await SendAsync(
            client,
            HttpMethod.Put,
            "/api/v1/database-orchestration/registrations/systemdata",
            Manifest());
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = payload.RootElement.GetProperty("data");
        Assert.Equal("systemdata", data.GetProperty("serviceKey").GetString());
        Assert.Equal("Development", data.GetProperty("environmentNId").GetString());
        Assert.Equal("Sqlite", data.GetProperty("provider").GetString());
        Assert.Equal("SourceOfTruth", data.GetProperty("desiredState").GetString());
        Assert.Equal("Registered", data.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Register_SameManifest_IsIdempotentSingleRegistration()
    {
        var store = new FakeDatabaseOrchestrationStore();
        using var factory = CreateFactory(store, DevelopmentTopology);
        using var client = factory.CreateClient();

        await SendAsync(client, HttpMethod.Put, "/api/v1/database-orchestration/registrations/systemdata", Manifest());
        await SendAsync(client, HttpMethod.Put, "/api/v1/database-orchestration/registrations/systemdata", Manifest());

        var page = await store.QueryRegistrationsAsync(new RegistrationListFilter(Tenant, null, ModuleKey: null, 1, 20), CancellationToken.None);
        Assert.Equal(1, page.Total);
    }

    [Fact]
    public async Task Register_MissingServiceKey_Returns400ValidationEnvelope()
    {
        var store = new FakeDatabaseOrchestrationStore();
        using var factory = CreateFactory(store, DevelopmentTopology);
        using var client = factory.CreateClient();

        using var response = await SendAsync(
            client,
            HttpMethod.Put,
            "/api/v1/database-orchestration/registrations/systemdata",
            Json(new { logicalDatabaseName = "systemdata_db", requestedVersion = "1.0.0", artifactChecksum = Sha("artifact") }));
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("SD_VALIDATION_FAILED", payload.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task ListRegistrations_ReturnsPageEnvelope()
    {
        var store = new FakeDatabaseOrchestrationStore();
        await store.AddRegistrationAsync(CreateRegistration(DevelopmentTopology, serviceKey: "systemdata"), CancellationToken.None);
        await store.AddRegistrationAsync(CreateRegistration(DevelopmentTopology, serviceKey: "referencedata"), CancellationToken.None);
        using var factory = CreateFactory(store, DevelopmentTopology);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/v1/database-orchestration/registrations");
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = payload.RootElement.GetProperty("data");
        Assert.Equal(2, data.GetProperty("total").GetInt32());
        Assert.Equal(2, data.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task GetRegistration_NotFound_Returns404Envelope()
    {
        using var factory = CreateFactory(new FakeDatabaseOrchestrationStore(), DevelopmentTopology);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/v1/database-orchestration/registrations/systemdata");
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("SD_DB_REGISTRATION_NOT_FOUND", payload.RootElement.GetProperty("code").GetString());
    }

    // ===== 计划 =====

    [Fact]
    public async Task EnqueuePlan_WithRegistration_Returns202Envelope()
    {
        var store = new FakeDatabaseOrchestrationStore();
        await store.AddRegistrationAsync(CreateRegistration(DevelopmentTopology), CancellationToken.None);
        using var factory = CreateFactory(store, DevelopmentTopology);
        using var client = factory.CreateClient();

        using var response = await SendAsync(
            client,
            HttpMethod.Post,
            "/api/v1/database-orchestration/plans",
            Json(new { serviceKey = "systemdata", requestedVersion = "1.0.0" }),
            idempotencyKey: "idem-plan-1");
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var data = payload.RootElement.GetProperty("data");
        Assert.Equal("Plan", data.GetProperty("kind").GetString());
        Assert.Equal("Queued", data.GetProperty("status").GetString());
        Assert.Equal("Validate", data.GetProperty("phase").GetString());
        Assert.False(string.IsNullOrWhiteSpace(data.GetProperty("operationNId").GetString()));
    }

    [Fact]
    public async Task EnqueuePlan_SameKeySameRequest_ReplaysSameOperation()
    {
        var store = new FakeDatabaseOrchestrationStore();
        await store.AddRegistrationAsync(CreateRegistration(DevelopmentTopology), CancellationToken.None);
        using var factory = CreateFactory(store, DevelopmentTopology);
        using var client = factory.CreateClient();
        var body = Json(new { serviceKey = "systemdata", requestedVersion = "1.0.0" });

        var first = await SendAsync(client, HttpMethod.Post, "/api/v1/database-orchestration/plans", body, idempotencyKey: "idem-plan-1");
        var second = await SendAsync(client, HttpMethod.Post, "/api/v1/database-orchestration/plans", body, idempotencyKey: "idem-plan-1");

        using var firstPayload = JsonDocument.Parse(await first.Content.ReadAsStreamAsync());
        using var secondPayload = JsonDocument.Parse(await second.Content.ReadAsStreamAsync());
        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, second.StatusCode);
        Assert.Equal(
            firstPayload.RootElement.GetProperty("data").GetProperty("operationNId").GetString(),
            secondPayload.RootElement.GetProperty("data").GetProperty("operationNId").GetString());
    }

    [Fact]
    public async Task EnqueuePlan_SameKeyDifferentRequest_Returns409ConflictEnvelope()
    {
        var store = new FakeDatabaseOrchestrationStore();
        await store.AddRegistrationAsync(CreateRegistration(DevelopmentTopology), CancellationToken.None);
        using var factory = CreateFactory(store, DevelopmentTopology);
        using var client = factory.CreateClient();

        await SendAsync(client, HttpMethod.Post, "/api/v1/database-orchestration/plans", Json(new { serviceKey = "systemdata", requestedVersion = "1.0.0" }), idempotencyKey: "idem-plan-1");
        using var response = await SendAsync(client, HttpMethod.Post, "/api/v1/database-orchestration/plans", Json(new { serviceKey = "systemdata", requestedVersion = "1.1.0" }), idempotencyKey: "idem-plan-1");
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("SD_DB_OPERATION_CONFLICT", payload.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task GetPlan_ReturnsPlanEnvelope()
    {
        var store = new FakeDatabaseOrchestrationStore();
        var registration = CreateRegistration(DevelopmentTopology);
        await store.AddRegistrationAsync(registration, CancellationToken.None);
        await store.AddPlanAsync(CreatePlan(registration, requestedVersion: "1.0.0"), CancellationToken.None);
        using var factory = CreateFactory(store, DevelopmentTopology);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/v1/database-orchestration/plans/PLAN-systemdata");
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = payload.RootElement.GetProperty("data");
        Assert.Equal("PLAN-systemdata", data.GetProperty("planNId").GetString());
        Assert.Equal("systemdata", data.GetProperty("serviceKey").GetString());
        Assert.Equal(3, data.GetProperty("steps").GetArrayLength());
    }

    [Fact]
    public async Task GetPlan_NotFound_Returns404Envelope()
    {
        using var factory = CreateFactory(new FakeDatabaseOrchestrationStore(), DevelopmentTopology);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/v1/database-orchestration/plans/PLAN-missing");
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("SD_NOT_FOUND", payload.RootElement.GetProperty("code").GetString());
    }

    // ===== apply =====

    [Fact]
    public async Task EnqueueApply_Development_Returns202Envelope()
    {
        var store = new FakeDatabaseOrchestrationStore();
        var registration = CreateRegistration(DevelopmentTopology);
        await store.AddRegistrationAsync(registration, CancellationToken.None);
        await store.AddPlanAsync(CreatePlan(registration, requestedVersion: "1.0.0"), CancellationToken.None);
        using var factory = CreateFactory(store, DevelopmentTopology);
        using var client = factory.CreateClient();

        using var response = await SendAsync(
            client,
            HttpMethod.Post,
            "/api/v1/database-orchestration/operations/apply",
            Json(new { planNId = "PLAN-systemdata", requestedVersion = "1.0.0" }),
            idempotencyKey: "idem-apply-1");
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var data = payload.RootElement.GetProperty("data");
        Assert.Equal("Apply", data.GetProperty("kind").GetString());
        Assert.Equal("Queued", data.GetProperty("status").GetString());
    }

    [Fact]
    public async Task EnqueueApply_MissingPlan_Returns404Envelope()
    {
        using var factory = CreateFactory(new FakeDatabaseOrchestrationStore(), DevelopmentTopology);
        using var client = factory.CreateClient();

        using var response = await SendAsync(
            client,
            HttpMethod.Post,
            "/api/v1/database-orchestration/operations/apply",
            Json(new { planNId = "PLAN-missing", requestedVersion = "1.0.0" }),
            idempotencyKey: "idem-apply-x");
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("SD_NOT_FOUND", payload.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task EnqueueApply_Production_WithoutApproval_Returns409ApprovalRequired()
    {
        var store = new FakeDatabaseOrchestrationStore();
        var registration = CreateRegistration(ProductionTopology);
        await store.AddRegistrationAsync(registration, CancellationToken.None);
        await store.AddPlanAsync(CreatePlan(registration, requestedVersion: "1.0.0"), CancellationToken.None);
        using var factory = CreateFactory(store, ProductionTopology);
        using var client = factory.CreateClient();

        using var response = await SendAsync(
            client,
            HttpMethod.Post,
            "/api/v1/database-orchestration/operations/apply",
            Json(new { planNId = "PLAN-systemdata", requestedVersion = "1.0.0" }),
            idempotencyKey: "idem-apply-prod");
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("SD_DB_APPROVAL_REQUIRED", payload.RootElement.GetProperty("code").GetString());
    }

    // ===== Operation =====

    [Fact]
    public async Task GetOperation_ReturnsOperationEnvelope()
    {
        var store = new FakeDatabaseOrchestrationStore();
        await store.AddOperationAsync(CreateOperation("OP-001", "idem-op-1"), CancellationToken.None);
        using var factory = CreateFactory(store, DevelopmentTopology);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/v1/database-orchestration/operations/OP-001");
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = payload.RootElement.GetProperty("data");
        Assert.Equal("OP-001", data.GetProperty("operationNId").GetString());
        Assert.Equal("Apply", data.GetProperty("kind").GetString());
        Assert.Equal(DatabaseProvisionOperation.AllPhases.Length, data.GetProperty("steps").GetArrayLength());
    }

    [Fact]
    public async Task CancelOperation_Queued_ReturnsCancelledStatus()
    {
        var store = new FakeDatabaseOrchestrationStore();
        await store.AddOperationAsync(CreateOperation("OP-001", "idem-op-1"), CancellationToken.None);
        using var factory = CreateFactory(store, DevelopmentTopology);
        using var client = factory.CreateClient();

        using var response = await client.PostAsync("/api/v1/database-orchestration/operations/OP-001/cancel", null);
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Cancelled", payload.RootElement.GetProperty("data").GetProperty("status").GetString());
    }

    // ===== readiness =====

    [Fact]
    public async Task Readiness_Ready_Returns200ReadyShape()
    {
        var store = new FakeDatabaseOrchestrationStore();
        var registration = CreateRegistration(DevelopmentTopology);
        await store.AddRegistrationAsync(registration, CancellationToken.None);
        await store.AddObservationAsync(CreateObservation("1.0.0"), CancellationToken.None);
        using var factory = CreateFactory(store, DevelopmentTopology);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/v1/database-orchestration/readiness/systemdata");
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = payload.RootElement.GetProperty("data");
        Assert.True(data.GetProperty("ready").GetBoolean());
        Assert.Equal("Ready", data.GetProperty("status").GetString());
        Assert.Equal("systemdata", data.GetProperty("serviceKey").GetString());
        Assert.Equal("1.0.0", data.GetProperty("observedVersion").GetString());
    }

    [Fact]
    public async Task Readiness_NotRegistered_Returns503NotReadyEnvelopeWithMaskedTarget()
    {
        using var factory = CreateFactory(new FakeDatabaseOrchestrationStore(), DevelopmentTopology);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/v1/database-orchestration/readiness/systemdata");
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("SD_DB_NOT_READY", payload.RootElement.GetProperty("code").GetString());
        var data = payload.RootElement.GetProperty("data");
        Assert.False(data.GetProperty("ready").GetBoolean());
        Assert.Equal("NotReady", data.GetProperty("status").GetString());
        Assert.Equal("***", data.GetProperty("physicalDatabaseTarget").GetString());
    }

    // ===== 夹具与种子 =====

    private static WebApplicationFactory<Program> CreateFactory(FakeDatabaseOrchestrationStore store, DatabaseTopology topology)
        => new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IDatabaseOrchestrationStore>();
                services.AddSingleton<IDatabaseOrchestrationStore>(store);
                services.RemoveAll<IDatabaseTopologyProvider>();
                services.AddSingleton<IDatabaseTopologyProvider>(new FakeTopologyProvider(topology));
                services.RemoveAll<ICurrentUser>();
                services.AddScoped<ICurrentUser>(_ => new FakeCurrentUser(Tenant, Actor));
                services.RemoveAll<ISystemDataPermissionEvaluator>();
                services.AddSingleton<ISystemDataPermissionEvaluator, TestSystemDataPermissionEvaluator>();

                // SD-006 已为控制面端点接入 [Authorize] 权限策略(§9.6):
                // 以测试方案替换 JwtBearer,注入全部编排/初始化权限,actor 仍由 FakeCurrentUser 提供。
                services.AddAuthentication(TestAuthDefaults.Scheme)
                    .AddScheme<TestAuthHandlerOptions, TestAuthHandler>(
                        TestAuthDefaults.Scheme,
                        options => options.PrincipalFactory = _ => TestAuthHandler.BuildPrincipal(OrchestrationAdmin()));
            }));

    private static TestUser OrchestrationAdmin() => new()
    {
        UserNId = Actor,
        UserName = "api-tester",
        TenantNId = Tenant,
        PermissionNIds =
        [
            "systemdata.database-orchestration.view",
            "systemdata.database-orchestration.register",
            "systemdata.database-orchestration.plan",
            "systemdata.database-orchestration.apply",
            "systemdata.database-orchestration.approve",
            "systemdata.database-orchestration.backup",
            "systemdata.database-orchestration.cancel",
            "systemdata.service-initialization.view",
            "systemdata.service-initialization.register",
            "systemdata.service-initialization.plan",
            "systemdata.service-initialization.apply",
            "systemdata.service-initialization.approve",
            "systemdata.service-initialization.backup",
            "systemdata.service-initialization.cancel",
        ],
    };

    private sealed class TestSystemDataPermissionEvaluator : ISystemDataPermissionEvaluator
    {
        public Task<SystemDataPermissionDecision> EvaluateAsync(SystemDataPermissionRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new SystemDataPermissionDecision(
                request.TenantNId == Tenant && request.UserNId == Actor,
                request.TenantNId == Tenant && request.UserNId == Actor
                    ? SystemDataPermissionDenialReason.None
                    : SystemDataPermissionDenialReason.MissingPermission));
    }

    private static async Task<HttpResponseMessage> SendAsync(
        HttpClient client,
        HttpMethod method,
        string url,
        string? json = null,
        string? idempotencyKey = null)
    {
        var request = new HttpRequestMessage(method, url);
        if (idempotencyKey is not null)
        {
            request.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);
        }

        if (json is not null)
        {
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        return await client.SendAsync(request);
    }

    private static string Manifest() =>
        Json(new
        {
            serviceKey = "systemdata",
            logicalDatabaseName = "systemdata_db",
            requestedVersion = "1.0.0",
            artifactChecksum = Sha("artifact"),
        });

    private static string Json(object value) => JsonSerializer.Serialize(value, WebJson);

    private static string Sha(string value) => DatabaseTopologyFingerprint.Sha256Hex(value);

    private static DatabaseTopology DevelopmentTopology => new(
        "Development",
        DatabaseTopologyMode.Shared,
        "industrial_platform_dev",
        "industrial-platform.db",
        new Dictionary<string, string>());

    private static DatabaseTopology ProductionTopology => new(
        "Production",
        DatabaseTopologyMode.PerService,
        null,
        null,
        new Dictionary<string, string> { ["systemdata"] = "systemdata_prod" });

    private static DatabaseRegistration CreateRegistration(DatabaseTopology topology, string serviceKey = "systemdata", string version = "1.0.0")
    {
        var isShared = topology.Mode == DatabaseTopologyMode.Shared;
        var physical = isShared ? topology.SharedSqliteFile! : topology.ServiceDatabases[serviceKey];
        return DatabaseRegistration.Register(
            Tenant,
            topology.EnvironmentName,
            serviceKey,
            isShared ? "Sqlite" : "PostgreSQL",
            $"{serviceKey}_db",
            physical,
            isShared,
            isShared ? "Shared" : "PerService",
            DatabaseTopologyFingerprint.ComputeTopologyRevision(topology),
            "artifact-1",
            version,
            Sha($"artifact-{serviceKey}"),
            "sig-ref",
            "user-owner",
            DesiredState.SourceOfTruth,
            autoProvision: false,
            autoMigrate: false,
            "1",
            Sha($"manifest-{serviceKey}"));
    }

    private static DatabaseProvisionPlan CreatePlan(DatabaseRegistration registration, string requestedVersion)
        => DatabaseProvisionPlan.Create(
            Tenant,
            $"PLAN-{registration.ServiceKey}",
            registration.EnvironmentNId,
            registration.ServiceKey,
            requestedVersion,
            "0.9.0",
            ComputeTargetFingerprint(registration, requestedVersion),
            RiskLevel.Medium,
            destructiveChangeDetected: false,
            DatabasePlanRequiredPolicies.None,
            DateTimeOffset.UtcNow.AddMinutes(30),
            "user-planner",
            [
                new DatabasePlanStep(1, "validate", null, null, null, RiskLevel.Low),
                new DatabasePlanStep(2, "inspect", null, null, null, RiskLevel.Low),
                new DatabasePlanStep(3, "migrate", null, null, null, RiskLevel.High),
            ]);

    /// <summary>重算 apply 门禁期望的目标状态指纹(与 DatabaseOperationService 同输入同算法)。</summary>
    private static string ComputeTargetFingerprint(DatabaseRegistration registration, string requestedVersion)
    {
        var production = string.Equals(registration.EnvironmentNId, "Production", StringComparison.Ordinal);
        return DatabaseTopologyFingerprint.ComputeTargetStateFingerprint(
            registration.EnvironmentNId,
            registration.ServiceKey,
            registration.Provider,
            registration.LogicalDatabaseName,
            registration.PhysicalDatabaseName,
            registration.TopologyMode,
            registration.TopologyRevision,
            registration.ArtifactChecksum,
            requestedVersion,
            registration.DesiredState.ToString(),
            production,
            production);
    }

    private static DatabaseProvisionOperation CreateOperation(string operationNId, string idempotencyKey)
        => DatabaseProvisionOperation.Enqueue(
            Tenant,
            operationNId,
            OperationKind.Apply,
            "Development",
            "systemdata",
            "PLAN-systemdata",
            "1.0.0",
            idempotencyKey,
            Sha($"request-{operationNId}"),
            DateTimeOffset.UtcNow.AddMinutes(30),
            "trace-001",
            Actor);

    private static DatabaseMigrationObservation CreateObservation(string observedVersion)
        => DatabaseMigrationObservation.Record(
            Tenant,
            "Development",
            "systemdata",
            Sha("identity"),
            observedVersion,
            Sha("artifact"),
            DateTimeOffset.UtcNow.AddMinutes(-1),
            "OP-001",
            VerificationStatus.Verified);

    private sealed class FakeCurrentUser : ICurrentUser
    {
        private readonly string? _tenantId;
        private readonly string? _userNId;

        public FakeCurrentUser(string? tenantId, string? userNId)
        {
            _tenantId = tenantId;
            _userNId = userNId;
        }

        public bool IsAuthenticated => _tenantId is not null && _userNId is not null;

        public string? UserNId => _userNId;

        public string? UserName => "api-tester";

        public string? TenantId => _tenantId;

        public IReadOnlyCollection<string> Roles => [];
    }
}
