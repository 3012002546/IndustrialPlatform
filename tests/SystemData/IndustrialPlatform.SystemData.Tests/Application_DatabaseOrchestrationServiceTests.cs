using IndustrialPlatform.SharedKernel.Exceptions;
using IndustrialPlatform.SystemData.Application.DatabaseOrchestration;
using IndustrialPlatform.SystemData.Application.DatabaseOrchestration.Internal;
using IndustrialPlatform.SystemData.Application.DatabaseOrchestration.Options;
using NotFoundException = IndustrialPlatform.SystemData.Application.DatabaseOrchestration.NotFoundException;
using IndustrialPlatform.SystemData.Contracts.DatabaseOrchestration;
using IndustrialPlatform.SystemData.Domain.DatabaseOrchestration;
using IndustrialPlatform.SharedKernel.Topology;
using IndustrialPlatform.SystemData.Domain.Topology;
using Microsoft.Extensions.Options;

namespace IndustrialPlatform.SystemData.Application.Tests;

/// <summary>
/// 数据库编排应用层用例测试(TASK-SD-002)。进程内假 store(<see cref="FakeDatabaseOrchestrationStore"/>)
/// 模拟九张控制面表;开发拓扑(Shared/Sqlite)用于无门禁 apply,生产拓扑(PerService/PostgreSQL)
/// 用于审批+备份门禁。门禁链、幂等重放、错误码映射、readiness 形状全量覆盖。
/// </summary>
public sealed class DatabaseOrchestrationServiceTests
{
    private const string Tenant = "tenant-001";
    private const string Actor = "user-actor";

    private static readonly DatabaseTopology DevelopmentTopology = new(
        "Development",
        DatabaseTopologyMode.Shared,
        "industrial_platform_dev",
        "industrial-platform.db",
        new Dictionary<string, string>());

    private static readonly DatabaseTopology ProductionTopology = new(
        "Production",
        DatabaseTopologyMode.PerService,
        null,
        null,
        new Dictionary<string, string> { ["systemdata"] = "systemdata_prod" });

    // =====================================================================
    // 注册清单用例
    // =====================================================================

    [Fact]
    public async Task RegisterAsync_DevelopmentShared_CreatesSqliteRegistration()
    {
        var store = new FakeDatabaseOrchestrationStore();
        var service = CreateRegistrationService(store, DevelopmentTopology);
        var manifest = CreateManifest();

        var result = await service.RegisterAsync(Tenant, Actor, manifest, CancellationToken.None);

        Assert.Equal("systemdata", result.ServiceKey);
        Assert.Equal("Development", result.EnvironmentNId);
        Assert.Equal("Sqlite", result.Provider);
        Assert.Equal("Shared", result.TopologyMode);
        Assert.Equal("industrial-platform.db", result.PhysicalDatabaseName);
        Assert.True(result.IsSharedPhysicalDatabase);
        Assert.Equal("Registered", result.Status);
        Assert.Equal("SourceOfTruth", result.DesiredState);
        Assert.Equal(RequestHasher.HashManifest(manifest), result.ManifestChecksum);
        Assert.Equal(RegistrationNIdAsTopologyRevision(DevelopmentTopology), result.TopologyRevision);
    }

    [Fact]
    public async Task RegisterAsync_SameManifest_ReturnsExistingIdempotently()
    {
        var store = new FakeDatabaseOrchestrationStore();
        var service = CreateRegistrationService(store, DevelopmentTopology);
        var manifest = CreateManifest();

        var first = await service.RegisterAsync(Tenant, Actor, manifest, CancellationToken.None);
        var second = await service.RegisterAsync(Tenant, Actor, manifest, CancellationToken.None);

        Assert.Equal(first.ManifestChecksum, second.ManifestChecksum);
        Assert.Equal(first.RegisteredOn, second.RegisteredOn);
        Assert.Equal(first.PhysicalDatabaseName, second.PhysicalDatabaseName);
    }

    [Fact]
    public async Task RegisterModuleAsync_OldV2SameManifest_PromotesServiceInitializerMarker()
    {
        var store = new FakeDatabaseOrchestrationStore();
        var service = CreateRegistrationService(store, DevelopmentTopology);
        var manifest = new ServiceInitializationManifestV2
        {
            ServiceKey = "systemdata",
            ModuleKey = "systemdata",
            LogicalDatabaseName = "systemdata_db",
            Provider = "Sqlite",
            TopologyMode = "Shared",
            MigrationArtifactId = "artifact-1",
            RequestedVersion = "1.0.0",
            ArtifactChecksum = Sha("artifact"),
            ArtifactSignature = "sig-ref",
        };
        var oldV2 = DatabaseRegistration.Register(
            Tenant,
            DevelopmentTopology.EnvironmentName,
            "systemdata",
            "Sqlite",
            "systemdata_db",
            "industrial-platform.db",
            isSharedPhysicalDatabase: true,
            "Shared",
            RegistrationNIdAsTopologyRevision(DevelopmentTopology),
            "artifact-1",
            "1.0.0",
            Sha("artifact"),
            "sig-ref",
            Actor,
            DesiredState.SourceOfTruth,
            autoProvision: false,
            autoMigrate: false,
            "1",
            RequestHasher.HashManifestV2(manifest),
            moduleKey: "systemdata",
            usesServiceInitializer: false);
        await store.AddRegistrationAsync(oldV2, CancellationToken.None);

        var result = await service.RegisterModuleAsync(Tenant, Actor, manifest, CancellationToken.None);

        Assert.True(result.UsesServiceInitializer);
        Assert.True((await store.GetRegistrationAsync(
            Tenant, DevelopmentTopology.EnvironmentName, "systemdata", "systemdata", CancellationToken.None))!.UsesServiceInitializer);
    }

    [Fact]
    public async Task RegisterAsync_NewVersion_UpdatesExisting()
    {
        var store = new FakeDatabaseOrchestrationStore();
        var service = CreateRegistrationService(store, DevelopmentTopology);

        await service.RegisterAsync(Tenant, Actor, CreateManifest(version: "1.0.0"), CancellationToken.None);
        var updated = await service.RegisterAsync(Tenant, Actor, CreateManifest(version: "2.0.0"), CancellationToken.None);

        Assert.Equal("2.0.0", updated.MigrationVersion);
        Assert.NotEqual(updated.RegisteredOn, updated.LastUpdatedOn);
    }

    [Fact]
    public async Task RegisterAsync_UnsupportedProvider_ThrowsProviderUnsupported()
    {
        var store = new FakeDatabaseOrchestrationStore();
        var service = CreateRegistrationService(store, DevelopmentTopology);
        var manifest = CreateManifest() with { Provider = "Oracle" };

        var ex = await Assert.ThrowsAsync<ProviderUnsupportedException>(
            () => service.RegisterAsync(Tenant, Actor, manifest, CancellationToken.None));

        Assert.Equal(400, ex.StatusCode);
        Assert.Equal("SD_DB_PROVIDER_UNSUPPORTED", ex.Code);
    }

    [Fact]
    public async Task RegisterAsync_TopologyModeMismatch_ThrowsTopologyUnsupported()
    {
        var store = new FakeDatabaseOrchestrationStore();
        var service = CreateRegistrationService(store, DevelopmentTopology);
        var manifest = CreateManifest() with { TopologyMode = "PerService" };

        var ex = await Assert.ThrowsAsync<TopologyUnsupportedException>(
            () => service.RegisterAsync(Tenant, Actor, manifest, CancellationToken.None));

        Assert.Equal("SD_DB_TOPOLOGY_UNSUPPORTED", ex.Code);
    }

    [Fact]
    public async Task RegisterAsync_SameVersionDifferentArtifact_ThrowsArtifactInvalid()
    {
        var store = new FakeDatabaseOrchestrationStore();
        var service = CreateRegistrationService(store, DevelopmentTopology);
        await SeedRegistrationAsync(store, DevelopmentTopology, version: "1.0.0", artifactChecksum: Sha("artifact-a"));

        var manifest = CreateManifest(version: "1.0.0") with { ArtifactChecksum = Sha("artifact-b") };

        var ex = await Assert.ThrowsAsync<ArtifactInvalidException>(
            () => service.RegisterAsync(Tenant, Actor, manifest, CancellationToken.None));

        Assert.Equal(409, ex.StatusCode);
        Assert.Equal("SD_DB_ARTIFACT_INVALID", ex.Code);
    }

    [Fact]
    public async Task RegisterAsync_ConcurrentModification_ThrowsOperationConflict()
    {
        var store = new FakeDatabaseOrchestrationStore();
        var service = CreateRegistrationService(store, DevelopmentTopology);
        var registration = await SeedRegistrationAsync(store, DevelopmentTopology, version: "1.0.0");

        // 外部写入者直接修改聚合推进版本(不经 store 提交),服务再以新清单重注册 →
        // 读到的期望版本与 store 快照不一致 → ConcurrencyException → SD_DB_OPERATION_CONFLICT。
        registration.UpdateDesiredState(DesiredState.Operational);
        var manifest = CreateManifest(version: "2.0.0");

        var ex = await Assert.ThrowsAsync<OperationConflictException>(
            () => service.RegisterAsync(Tenant, Actor, manifest, CancellationToken.None));

        Assert.Equal(409, ex.StatusCode);
        Assert.Equal("SD_DB_OPERATION_CONFLICT", ex.Code);
    }

    [Fact]
    public async Task GetAsync_MissingRegistration_ThrowsRegistrationNotFound()
    {
        var store = new FakeDatabaseOrchestrationStore();
        var service = CreateRegistrationService(store, DevelopmentTopology);

        var ex = await Assert.ThrowsAsync<RegistrationNotFoundException>(
            () => service.GetAsync(Tenant, "systemdata", CancellationToken.None));

        Assert.Equal(404, ex.StatusCode);
        Assert.Equal("SD_DB_REGISTRATION_NOT_FOUND", ex.Code);
    }

    [Fact]
    public async Task ListAsync_FiltersByServiceKey()
    {
        var store = new FakeDatabaseOrchestrationStore();
        var service = CreateRegistrationService(store, DevelopmentTopology);
        await SeedRegistrationAsync(store, DevelopmentTopology, serviceKey: "systemdata");
        await SeedRegistrationAsync(store, DevelopmentTopology, serviceKey: "referencedata");

        var page = await service.ListAsync(Tenant, "system", 1, 20, CancellationToken.None);

        Assert.Equal(1, page.Total);
        Assert.Single(page.Items);
        Assert.Equal("systemdata", page.Items[0].ServiceKey);
    }

    [Fact]
    public async Task UpdateDesiredStateAsync_MissingRegistration_ThrowsRegistrationNotFound()
    {
        var store = new FakeDatabaseOrchestrationStore();
        var service = CreateRegistrationService(store, DevelopmentTopology);

        await Assert.ThrowsAsync<RegistrationNotFoundException>(
            () => service.UpdateDesiredStateAsync(Tenant, Actor, "systemdata", "Retiring", CancellationToken.None));
    }

    [Fact]
    public async Task UpdateDesiredStateAsync_ChangesState()
    {
        var store = new FakeDatabaseOrchestrationStore();
        var service = CreateRegistrationService(store, DevelopmentTopology);
        await SeedRegistrationAsync(store, DevelopmentTopology);

        var result = await service.UpdateDesiredStateAsync(Tenant, Actor, "systemdata", "Retiring", CancellationToken.None);

        Assert.Equal("Retiring", result.DesiredState);
    }

    // =====================================================================
    // 计划用例
    // =====================================================================

    [Fact]
    public async Task EnqueuePlanAsync_MissingRegistration_ThrowsRegistrationNotFound()
    {
        var store = new FakeDatabaseOrchestrationStore();
        var service = CreatePlanService(store, DevelopmentTopology);

        await Assert.ThrowsAsync<RegistrationNotFoundException>(() =>
            service.EnqueuePlanAsync(Tenant, Actor, "idem-plan-1", CreatePlanRequest(), "trace-001", CancellationToken.None));
    }

    [Fact]
    public async Task EnqueuePlanAsync_CreatesQueuedPlanOperation()
    {
        var store = new FakeDatabaseOrchestrationStore();
        var service = CreatePlanService(store, DevelopmentTopology);
        await SeedRegistrationAsync(store, DevelopmentTopology);

        var result = await service.EnqueuePlanAsync(Tenant, Actor, "idem-plan-1", CreatePlanRequest(), "trace-001", CancellationToken.None);

        Assert.False(string.IsNullOrWhiteSpace(result.OperationNId));
        Assert.Equal("Plan", result.Kind);
        Assert.Equal("Queued", result.Status);
        Assert.Equal("Validate", result.Phase);
        Assert.NotEqual(default, result.AcceptedOn);
    }

    [Fact]
    public async Task EnqueuePlanAsync_SameKeySameRequest_ReplaysExistingOperation()
    {
        var store = new FakeDatabaseOrchestrationStore();
        var service = CreatePlanService(store, DevelopmentTopology);
        await SeedRegistrationAsync(store, DevelopmentTopology);
        var request = CreatePlanRequest();

        var first = await service.EnqueuePlanAsync(Tenant, Actor, "idem-plan-1", request, "trace-001", CancellationToken.None);
        var second = await service.EnqueuePlanAsync(Tenant, Actor, "idem-plan-1", request, "trace-001", CancellationToken.None);

        Assert.Equal(first.OperationNId, second.OperationNId);
        Assert.Equal(first.AcceptedOn, second.AcceptedOn);
    }

    [Fact]
    public async Task EnqueuePlanAsync_SameKeyDifferentRequest_ThrowsOperationConflict()
    {
        var store = new FakeDatabaseOrchestrationStore();
        var service = CreatePlanService(store, DevelopmentTopology);
        await SeedRegistrationAsync(store, DevelopmentTopology);

        await service.EnqueuePlanAsync(Tenant, Actor, "idem-plan-1", CreatePlanRequest(version: "1.0.0"), "trace-001", CancellationToken.None);
        var ex = await Assert.ThrowsAsync<OperationConflictException>(() =>
            service.EnqueuePlanAsync(Tenant, Actor, "idem-plan-1", CreatePlanRequest(version: "2.0.0"), "trace-001", CancellationToken.None));

        Assert.Equal("SD_DB_OPERATION_CONFLICT", ex.Code);
    }

    [Fact]
    public async Task GetEffectivePolicyAsync_ProductionTopologyCannotBeWeakenedByStoredPolicy()
    {
        var store = new FakeDatabaseOrchestrationStore();
        var service = CreatePlanService(store, ProductionTopology);
        var stored = DatabaseEnvironmentPolicy.Create(
            Tenant,
            ProductionTopology.EnvironmentName,
            DatabaseEnvironmentKind.Development,
            approvalRequired: false,
            backupRequired: false,
            planTtlSeconds: 60,
            planTimeoutSeconds: 60,
            applyTimeoutSeconds: 60,
            maxPreMigrationRetries: 0);
        await store.AddEnvironmentPolicyAsync(stored, CancellationToken.None);

        var result = await service.GetEffectivePolicyAsync(Tenant, CancellationToken.None);

        Assert.Equal("Production", result.EnvironmentKind);
        Assert.True(result.ApprovalRequired);
        Assert.True(result.BackupRequired);
        Assert.Equal("Advanced", result.InitializationPolicy);
        Assert.True(result.IsExplicit);
        Assert.Equal(stored.PolicyRevision, result.PolicyRevision);
    }

    [Fact]
    public async Task GetAsync_MissingPlan_ThrowsNotFound()
    {
        var store = new FakeDatabaseOrchestrationStore();
        var service = CreatePlanService(store, DevelopmentTopology);

        var ex = await Assert.ThrowsAsync<NotFoundException>(
            () => service.GetAsync(Tenant, "PLAN-NOPE", CancellationToken.None));

        Assert.Equal(404, ex.StatusCode);
        Assert.Equal("SD_NOT_FOUND", ex.Code);
    }

    [Fact]
    public async Task GetAsync_ReturnsSeededPlan()
    {
        var store = new FakeDatabaseOrchestrationStore();
        var service = CreatePlanService(store, DevelopmentTopology);
        var plan = await SeedPlanAsync(store, DevelopmentTopology);

        var result = await service.GetAsync(Tenant, plan.PlanNId, CancellationToken.None);

        Assert.Equal(plan.PlanNId, result.PlanNId);
        Assert.Equal("1.0.0", result.RequestedMigrationVersion);
        Assert.Equal(plan.PlanChecksum, result.PlanChecksum);
        Assert.False(result.IsExpired);
        Assert.Equal(3, result.Steps.Count);
    }

    [Fact]
    public async Task ListAsync_ReturnsSeededPlans()
    {
        var store = new FakeDatabaseOrchestrationStore();
        var service = CreatePlanService(store, DevelopmentTopology);
        await SeedPlanAsync(store, DevelopmentTopology);

        var page = await service.ListAsync(Tenant, 1, 20, CancellationToken.None);

        Assert.Equal(1, page.Total);
        Assert.Single(page.Items);
    }

    // =====================================================================
    // apply 门禁链(Development 无门禁 / Production 审批+备份)
    // =====================================================================

    [Fact]
    public async Task EnqueueApplyAsync_MissingPlan_ThrowsNotFound()
    {
        var store = new FakeDatabaseOrchestrationStore();
        var service = CreateOperationService(store, DevelopmentTopology);

        var ex = await Assert.ThrowsAsync<NotFoundException>(() =>
            service.EnqueueApplyAsync(Tenant, Actor, "idem-apply-1", CreateApplyRequest("PLAN-NOPE"), "trace-001", CancellationToken.None));

        Assert.Equal("SD_NOT_FOUND", ex.Code);
    }

    [Fact]
    public async Task EnqueueApplyAsync_ExpiredPlan_ThrowsPlanExpired()
    {
        var store = new FakeDatabaseOrchestrationStore();
        var service = CreateOperationService(store, DevelopmentTopology);
        await SeedRegistrationAsync(store, DevelopmentTopology);
        await SeedPlanAsync(store, DevelopmentTopology, expiresOn: DateTimeOffset.UtcNow.AddMinutes(-5));

        var ex = await Assert.ThrowsAsync<PlanExpiredException>(() =>
            service.EnqueueApplyAsync(Tenant, Actor, "idem-apply-1", CreateApplyRequest(), "trace-001", CancellationToken.None));

        Assert.Equal(409, ex.StatusCode);
        Assert.Equal("SD_DB_PLAN_EXPIRED", ex.Code);
    }

    [Fact]
    public async Task EnqueueApplyAsync_MissingRegistration_ThrowsTargetMismatch()
    {
        var store = new FakeDatabaseOrchestrationStore();
        var service = CreateOperationService(store, DevelopmentTopology);
        await SeedPlanAsync(store, DevelopmentTopology, seedRegistration: false);

        var ex = await Assert.ThrowsAsync<TargetMismatchException>(() =>
            service.EnqueueApplyAsync(Tenant, Actor, "idem-apply-1", CreateApplyRequest(), "trace-001", CancellationToken.None));

        Assert.Equal("SD_DB_TARGET_MISMATCH", ex.Code);
    }

    [Fact]
    public async Task EnqueueApplyAsync_TopologyDrift_ThrowsTopologyDrift()
    {
        var store = new FakeDatabaseOrchestrationStore();
        var service = CreateOperationService(store, DevelopmentTopology);
        await SeedRegistrationAsync(store, DevelopmentTopology, topologyRevision: Sha("stale-topology"));
        await SeedPlanAsync(store, DevelopmentTopology);

        var ex = await Assert.ThrowsAsync<TopologyDriftException>(() =>
            service.EnqueueApplyAsync(Tenant, Actor, "idem-apply-1", CreateApplyRequest(), "trace-001", CancellationToken.None));

        Assert.Equal(409, ex.StatusCode);
        Assert.Equal("SD_DB_TOPOLOGY_DRIFT", ex.Code);
    }

    [Fact]
    public async Task EnqueueApplyAsync_PlanDrift_ThrowsPlanDrift()
    {
        var store = new FakeDatabaseOrchestrationStore();
        var service = CreateOperationService(store, DevelopmentTopology);
        var registration = await SeedRegistrationAsync(store, DevelopmentTopology);
        // 计划指纹按另一个产物校验和固化,门禁按注册清单字段重算指纹不匹配 → SD_DB_PLAN_DRIFT。
        var driftFingerprint = DatabaseTopologyFingerprint.ComputeTargetStateFingerprint(
            registration.EnvironmentNId,
            registration.ServiceKey,
            registration.Provider,
            registration.LogicalDatabaseName,
            registration.PhysicalDatabaseName,
            registration.TopologyMode,
            registration.TopologyRevision,
            Sha("artifact-a"),
            "1.0.0",
            registration.DesiredState.ToString(),
            approvalRequired: false,
            backupRequired: false);
        await SeedPlanAsync(store, DevelopmentTopology, fingerprintOverride: driftFingerprint);

        var ex = await Assert.ThrowsAsync<PlanDriftException>(() =>
            service.EnqueueApplyAsync(Tenant, Actor, "idem-apply-1", CreateApplyRequest(), "trace-001", CancellationToken.None));

        Assert.Equal("SD_DB_PLAN_DRIFT", ex.Code);
    }

    [Fact]
    public async Task EnqueueApplyAsync_VersionMismatch_ThrowsTargetMismatch()
    {
        var store = new FakeDatabaseOrchestrationStore();
        var service = CreateOperationService(store, DevelopmentTopology);
        await SeedRegistrationAsync(store, DevelopmentTopology, version: "1.0.0");
        await SeedPlanAsync(store, DevelopmentTopology, requestedVersion: "2.0.0");

        var ex = await Assert.ThrowsAsync<TargetMismatchException>(() =>
            service.EnqueueApplyAsync(Tenant, Actor, "idem-apply-1", CreateApplyRequest(version: "2.0.0"), "trace-001", CancellationToken.None));

        Assert.Equal("SD_DB_TARGET_MISMATCH", ex.Code);
    }

    [Fact]
    public async Task EnqueueApplyAsync_Development_CreatesQueuedApplyOperation()
    {
        var store = new FakeDatabaseOrchestrationStore();
        var service = CreateOperationService(store, DevelopmentTopology);
        await SeedRegistrationAsync(store, DevelopmentTopology);
        await SeedPlanAsync(store, DevelopmentTopology);

        var result = await service.EnqueueApplyAsync(Tenant, Actor, "idem-apply-1", CreateApplyRequest(), "trace-001", CancellationToken.None);

        Assert.False(string.IsNullOrWhiteSpace(result.OperationNId));
        Assert.Equal("Apply", result.Kind);
        Assert.Equal("Queued", result.Status);
        Assert.Equal("Validate", result.Phase);
        Assert.NotEqual(default, result.AcceptedOn);

        var stored = await service.GetAsync(Tenant, result.OperationNId, CancellationToken.None);
        Assert.Equal("PLAN-001", stored.PlanNId);
    }

    [Fact]
    public async Task EnqueueApplyAsync_SameKeySameRequest_ReplaysExistingOperation()
    {
        var store = new FakeDatabaseOrchestrationStore();
        var service = CreateOperationService(store, DevelopmentTopology);
        await SeedRegistrationAsync(store, DevelopmentTopology);
        await SeedPlanAsync(store, DevelopmentTopology);
        var request = CreateApplyRequest();

        var first = await service.EnqueueApplyAsync(Tenant, Actor, "idem-apply-1", request, "trace-001", CancellationToken.None);
        var second = await service.EnqueueApplyAsync(Tenant, Actor, "idem-apply-1", request, "trace-001", CancellationToken.None);

        Assert.Equal(first.OperationNId, second.OperationNId);
    }

    [Fact]
    public async Task EnqueueApplyAsync_SameKeyDifferentRequest_ThrowsOperationConflict()
    {
        var store = new FakeDatabaseOrchestrationStore();
        var service = CreateOperationService(store, DevelopmentTopology);
        await SeedRegistrationAsync(store, DevelopmentTopology);
        await SeedPlanAsync(store, DevelopmentTopology);

        await service.EnqueueApplyAsync(Tenant, Actor, "idem-apply-1", CreateApplyRequest(version: "1.0.0"), "trace-001", CancellationToken.None);
        var ex = await Assert.ThrowsAsync<OperationConflictException>(() =>
            service.EnqueueApplyAsync(Tenant, Actor, "idem-apply-1", CreateApplyRequest(version: "2.0.0"), "trace-001", CancellationToken.None));

        Assert.Equal("SD_DB_OPERATION_CONFLICT", ex.Code);
    }

    // ===== 生产门禁 =====

    [Fact]
    public async Task EnqueueApplyAsync_ProductionWithoutApproval_ThrowsApprovalRequired()
    {
        var store = new FakeDatabaseOrchestrationStore();
        var service = CreateOperationService(store, ProductionTopology);
        await SeedRegistrationAsync(store, ProductionTopology);
        await SeedPlanAsync(store, ProductionTopology);

        var ex = await Assert.ThrowsAsync<ApprovalRequiredException>(() =>
            service.EnqueueApplyAsync(Tenant, Actor, "idem-apply-1", CreateApplyRequest(), "trace-001", CancellationToken.None));

        Assert.Equal(409, ex.StatusCode);
        Assert.Equal("SD_DB_APPROVAL_REQUIRED", ex.Code);
    }

    [Fact]
    public async Task EnqueueApplyAsync_ProductionWithoutBackup_ThrowsBackupRequired()
    {
        var store = new FakeDatabaseOrchestrationStore();
        var service = CreateOperationService(store, ProductionTopology);
        await SeedRegistrationAsync(store, ProductionTopology);
        var plan = await SeedPlanAsync(store, ProductionTopology);
        await SeedApprovalAsync(store, plan);

        var ex = await Assert.ThrowsAsync<BackupRequiredException>(() =>
            service.EnqueueApplyAsync(Tenant, Actor, "idem-apply-1", CreateApplyRequest(), "trace-001", CancellationToken.None));

        Assert.Equal("SD_DB_BACKUP_REQUIRED", ex.Code);
    }

    [Fact]
    public async Task EnqueueApplyAsync_ProductionWithApprovalAndBackup_CreatesOperation()
    {
        var store = new FakeDatabaseOrchestrationStore();
        var service = CreateOperationService(store, ProductionTopology);
        await SeedRegistrationAsync(store, ProductionTopology);
        var plan = await SeedPlanAsync(store, ProductionTopology);
        await SeedApprovalAsync(store, plan);
        await SeedVerifiedBackupAsync(store, plan);

        var result = await service.EnqueueApplyAsync(Tenant, Actor, "idem-apply-1", CreateApplyRequest(), "trace-001", CancellationToken.None);

        Assert.Equal("Apply", result.Kind);
        Assert.Equal("Queued", result.Status);
    }

    [Fact]
    public async Task EnqueueApplyAsync_ProductionWithExpiredApproval_ThrowsApprovalRequired()
    {
        var store = new FakeDatabaseOrchestrationStore();
        var service = CreateOperationService(store, ProductionTopology);
        await SeedRegistrationAsync(store, ProductionTopology);
        var plan = await SeedPlanAsync(store, ProductionTopology);
        await SeedApprovalAsync(store, plan, approvedOn: DateTimeOffset.UtcNow.AddDays(-10), expiresOn: DateTimeOffset.UtcNow.AddDays(-5));

        await Assert.ThrowsAsync<ApprovalRequiredException>(() =>
            service.EnqueueApplyAsync(Tenant, Actor, "idem-apply-1", CreateApplyRequest(), "trace-001", CancellationToken.None));
    }

    // =====================================================================
    // 操作查询/取消/租约
    // =====================================================================

    [Fact]
    public async Task GetAsync_MissingOperation_ThrowsNotFound()
    {
        var store = new FakeDatabaseOrchestrationStore();
        var service = CreateOperationService(store, DevelopmentTopology);

        await Assert.ThrowsAsync<NotFoundException>(
            () => service.GetAsync(Tenant, "OP-NOPE", CancellationToken.None));
    }

    [Fact]
    public async Task ListAsync_FiltersByKindAndStatus()
    {
        var store = new FakeDatabaseOrchestrationStore();
        var service = CreateOperationService(store, DevelopmentTopology);
        await SeedOperationAsync(store, "OP-001", OperationKind.Apply, OperationStatus.Queued);
        await SeedOperationAsync(store, "OP-002", OperationKind.Plan, OperationStatus.Queued);

        var page = await service.ListAsync(Tenant, "Apply", null, 1, 20, CancellationToken.None);

        Assert.Equal(1, page.Total);
        Assert.Equal("Apply", page.Items[0].Kind);
    }

    [Fact]
    public async Task CancelAsync_QueuedOperation_Cancels()
    {
        var store = new FakeDatabaseOrchestrationStore();
        var service = CreateOperationService(store, DevelopmentTopology);
        await SeedOperationAsync(store, "OP-001", OperationKind.Apply, OperationStatus.Queued);

        var result = await service.CancelAsync(Tenant, "OP-001", CancellationToken.None);

        Assert.Equal("Cancelled", result.Status);
        Assert.NotNull(result.CompletedOn);
    }

    [Fact]
    public async Task CancelAsync_PastInspect_ThrowsOperationNotCancellable()
    {
        var store = new FakeDatabaseOrchestrationStore();
        var service = CreateOperationService(store, DevelopmentTopology);
        await SeedOperationAsync(store, "OP-001", OperationKind.Apply, OperationStatus.Running, phase: OperationPhase.ProvisionDatabase);

        var ex = await Assert.ThrowsAsync<OperationNotCancellableException>(
            () => service.CancelAsync(Tenant, "OP-001", CancellationToken.None));

        Assert.Equal(409, ex.StatusCode);
        Assert.Equal("SD_DB_OPERATION_NOT_CANCELLABLE", ex.Code);
    }

    [Fact]
    public async Task CancelAsync_QueuedOnly_CancelsRunningAtInspect()
    {
        var store = new FakeDatabaseOrchestrationStore();
        var service = CreateOperationService(store, DevelopmentTopology);
        await SeedOperationAsync(store, "OP-001", OperationKind.Apply, OperationStatus.Running, phase: OperationPhase.Inspect);

        var result = await service.CancelAsync(Tenant, "OP-001", CancellationToken.None);

        Assert.Equal("Cancelled", result.Status);
    }

    [Fact]
    public async Task HeartbeatAsync_WithOwner_RenewsLease()
    {
        var store = new FakeDatabaseOrchestrationStore();
        var service = CreateOperationService(store, DevelopmentTopology);
        await SeedOperationAsync(store, "OP-001", OperationKind.Apply, OperationStatus.Running, phase: OperationPhase.Validate, leaseOwner: "runner-1");

        var result = await service.HeartbeatAsync(Tenant, "OP-001", "runner-1", CancellationToken.None);

        Assert.Equal("Running", result.Status);
        Assert.Equal("runner-1", result.LeaseOwner);
    }

    [Fact]
    public async Task HeartbeatAsync_WrongOwner_ThrowsOperationConflict()
    {
        var store = new FakeDatabaseOrchestrationStore();
        var service = CreateOperationService(store, DevelopmentTopology);
        await SeedOperationAsync(store, "OP-001", OperationKind.Apply, OperationStatus.Running, phase: OperationPhase.Validate, leaseOwner: "runner-1");

        await Assert.ThrowsAsync<OperationConflictException>(
            () => service.HeartbeatAsync(Tenant, "OP-001", "runner-2", CancellationToken.None));
    }

    [Fact]
    public async Task ReleaseLeaseAsync_WithOwner_ReleasesLease()
    {
        var store = new FakeDatabaseOrchestrationStore();
        var service = CreateOperationService(store, DevelopmentTopology);
        await SeedOperationAsync(store, "OP-001", OperationKind.Apply, OperationStatus.Running, phase: OperationPhase.Validate, leaseOwner: "runner-1");

        var result = await service.ReleaseLeaseAsync(Tenant, "OP-001", "runner-1", CancellationToken.None);

        Assert.Null(result.LeaseOwner);
    }

    [Fact]
    public async Task ReleaseLeaseAsync_WrongOwner_ThrowsOperationConflict()
    {
        var store = new FakeDatabaseOrchestrationStore();
        var service = CreateOperationService(store, DevelopmentTopology);
        await SeedOperationAsync(store, "OP-001", OperationKind.Apply, OperationStatus.Running, phase: OperationPhase.Validate, leaseOwner: "runner-1");

        await Assert.ThrowsAsync<OperationConflictException>(
            () => service.ReleaseLeaseAsync(Tenant, "OP-001", "runner-2", CancellationToken.None));
    }

    // =====================================================================
    // readiness(注册清单 + 最近观察)
    // =====================================================================

    [Fact]
    public async Task GetReadinessAsync_NoRegistration_NotReady()
    {
        var store = new FakeDatabaseOrchestrationStore();
        var service = CreateOperationService(store, DevelopmentTopology);

        var result = await service.GetReadinessAsync(Tenant, "systemdata", "trace-001", CancellationToken.None);

        Assert.False(result.Ready);
        Assert.Equal("NotReady", result.Status);
        Assert.Equal("服务数据库尚未注册。", result.Reason);
        Assert.Equal("***", result.PhysicalDatabaseTarget);
    }

    [Fact]
    public async Task GetReadinessAsync_NoObservation_NotReady()
    {
        var store = new FakeDatabaseOrchestrationStore();
        var service = CreateOperationService(store, DevelopmentTopology);
        await SeedRegistrationAsync(store, DevelopmentTopology);

        var result = await service.GetReadinessAsync(Tenant, "systemdata", "trace-001", CancellationToken.None);

        Assert.False(result.Ready);
        Assert.Equal("尚未观察到目标数据库迁移状态。", result.Reason);
        Assert.Equal("systemdata_db", result.LogicalDatabaseName);
        Assert.NotEmpty(result.DatabaseIdentityFingerprint);
    }

    [Fact]
    public async Task GetReadinessAsync_MismatchedObservation_NotReady()
    {
        var store = new FakeDatabaseOrchestrationStore();
        var service = CreateOperationService(store, DevelopmentTopology);
        var registration = await SeedRegistrationAsync(store, DevelopmentTopology, version: "1.0.0");
        await SeedObservationAsync(store, registration, observedVersion: "0.9.0");

        var result = await service.GetReadinessAsync(Tenant, "systemdata", "trace-001", CancellationToken.None);

        Assert.False(result.Ready);
        Assert.Equal("观察版本与期望版本不一致。", result.Reason);
        Assert.Equal("0.9.0", result.ObservedVersion);
    }

    [Fact]
    public async Task GetReadinessAsync_MatchingObservation_Ready()
    {
        var store = new FakeDatabaseOrchestrationStore();
        var service = CreateOperationService(store, DevelopmentTopology);
        var registration = await SeedRegistrationAsync(store, DevelopmentTopology, version: "1.0.0");
        await SeedObservationAsync(store, registration, observedVersion: "1.0.0");

        var result = await service.GetReadinessAsync(Tenant, "systemdata", "trace-001", CancellationToken.None);

        Assert.True(result.Ready);
        Assert.Equal("Ready", result.Status);
        Assert.Equal("1.0.0", result.DesiredVersion);
        Assert.Equal("1.0.0", result.ObservedVersion);
        Assert.NotNull(result.ObservedOn);
        Assert.Equal("ind***db", result.PhysicalDatabaseTarget);
        Assert.Equal(registration.TopologyRevision, result.TopologyRevision);
        Assert.Equal(registration.ArtifactChecksum, result.ArtifactChecksum);
    }

    // =====================================================================
    // 审批用例
    // =====================================================================

    [Fact]
    public async Task CreateApprovalAsync_MissingPlan_ThrowsNotFound()
    {
        var store = new FakeDatabaseOrchestrationStore();
        var service = CreateApprovalService(store);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            service.CreateAsync(Tenant, Actor, "PLAN-NOPE", new DatabaseApprovalRequestV1(), CancellationToken.None));
    }

    [Fact]
    public async Task CreateApprovalAsync_ExpiredPlan_ThrowsPlanExpired()
    {
        var store = new FakeDatabaseOrchestrationStore();
        var service = CreateApprovalService(store);
        await SeedPlanAsync(store, DevelopmentTopology, expiresOn: DateTimeOffset.UtcNow.AddMinutes(-5));

        await Assert.ThrowsAsync<PlanExpiredException>(() =>
            service.CreateAsync(Tenant, Actor, "PLAN-001", new DatabaseApprovalRequestV1(), CancellationToken.None));
    }

    [Fact]
    public async Task CreateApprovalAsync_CreatesApproval()
    {
        var store = new FakeDatabaseOrchestrationStore();
        var service = CreateApprovalService(store);
        var plan = await SeedPlanAsync(store, DevelopmentTopology);

        var result = await service.CreateAsync(
            Tenant, Actor, plan.PlanNId, new DatabaseApprovalRequestV1 { Reason = "approved" }, CancellationToken.None);

        Assert.Equal("Approved", result.Status);
        Assert.Equal(plan.PlanNId, result.PlanNId);
        Assert.Equal(plan.PlanChecksum, result.PlanChecksum);
        Assert.Equal(plan.TargetStateFingerprint, result.TargetStateFingerprint);
        Assert.Equal(plan.ExpiresOn, result.ExpiresOn);
        Assert.Equal(Actor, result.ApprovedByUserNId);
    }

    [Fact]
    public async Task IsApprovedForAsync_NoApproval_ReturnsFalse()
    {
        var store = new FakeDatabaseOrchestrationStore();
        var service = CreateApprovalService(store);
        var plan = await SeedPlanAsync(store, DevelopmentTopology);

        var approved = await service.IsApprovedForAsync(Tenant, plan, CancellationToken.None);

        Assert.False(approved);
    }

    [Fact]
    public async Task IsApprovedForAsync_WithValidApproval_ReturnsTrue()
    {
        var store = new FakeDatabaseOrchestrationStore();
        var service = CreateApprovalService(store);
        var plan = await SeedPlanAsync(store, DevelopmentTopology);
        await SeedApprovalAsync(store, plan);

        var approved = await service.IsApprovedForAsync(Tenant, plan, CancellationToken.None);

        Assert.True(approved);
    }

    // =====================================================================
    // 备份证据用例
    // =====================================================================

    [Fact]
    public async Task CreateBackupEvidenceAsync_MissingPlan_ThrowsNotFound()
    {
        var store = new FakeDatabaseOrchestrationStore();
        var service = CreateBackupService(store);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            service.CreateAsync(Tenant, Actor, "PLAN-NOPE", new DatabaseBackupEvidenceRequestV1(), CancellationToken.None));
    }

    [Fact]
    public async Task CreateBackupEvidenceAsync_MissingProvider_ThrowsValidationFailed()
    {
        var store = new FakeDatabaseOrchestrationStore();
        var service = CreateBackupService(store);
        await SeedPlanAsync(store, DevelopmentTopology);

        var ex = await Assert.ThrowsAsync<ValidationFailedException>(() =>
            service.CreateAsync(Tenant, Actor, "PLAN-001", new DatabaseBackupEvidenceRequestV1(), CancellationToken.None));

        Assert.Equal(400, ex.StatusCode);
        Assert.Equal("SD_VALIDATION_FAILED", ex.Code);
    }

    [Fact]
    public async Task CreateBackupEvidenceAsync_CreatesCapturedEvidence()
    {
        var store = new FakeDatabaseOrchestrationStore();
        var service = CreateBackupService(store);
        var plan = await SeedPlanAsync(store, DevelopmentTopology);

        var result = await service.CreateAsync(
            Tenant, Actor, plan.PlanNId,
            new DatabaseBackupEvidenceRequestV1 { BackupProvider = "aws-s3", BackupReference = "snapshot-001" },
            CancellationToken.None);

        Assert.Equal("Captured", result.Status);
        Assert.Equal("aws-s3", result.BackupProvider);
        Assert.Equal("snapshot-001", result.BackupReference);
        Assert.Null(result.VerifiedOn);
        Assert.True(result.RetentionUntil > result.CapturedOn);
    }

    [Fact]
    public async Task VerifyBackupEvidenceAsync_MissingEvidence_ThrowsNotFound()
    {
        var store = new FakeDatabaseOrchestrationStore();
        var service = CreateBackupService(store);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            service.VerifyAsync(Tenant, Actor, "EVD-NOPE", CancellationToken.None));
    }

    [Fact]
    public async Task VerifyBackupEvidenceAsync_TransitionsToVerified()
    {
        var store = new FakeDatabaseOrchestrationStore();
        var service = CreateBackupService(store);
        var plan = await SeedPlanAsync(store, DevelopmentTopology);
        var created = await service.CreateAsync(
            Tenant, Actor, plan.PlanNId,
            new DatabaseBackupEvidenceRequestV1 { BackupProvider = "aws-s3", BackupReference = "snapshot-001" },
            CancellationToken.None);

        var verified = await service.VerifyAsync(Tenant, Actor, created.EvidenceNId, CancellationToken.None);

        Assert.Equal("Verified", verified.Status);
        Assert.NotNull(verified.VerifiedOn);
        Assert.Equal(Actor, verified.VerifiedByUserNId);
    }

    [Fact]
    public async Task VerifyBackupEvidenceAsync_AlreadyVerified_Idempotent()
    {
        var store = new FakeDatabaseOrchestrationStore();
        var service = CreateBackupService(store);
        var plan = await SeedPlanAsync(store, DevelopmentTopology);
        var created = await service.CreateAsync(
            Tenant, Actor, plan.PlanNId,
            new DatabaseBackupEvidenceRequestV1 { BackupProvider = "aws-s3", BackupReference = "snapshot-001" },
            CancellationToken.None);
        await service.VerifyAsync(Tenant, Actor, created.EvidenceNId, CancellationToken.None);

        var again = await service.VerifyAsync(Tenant, Actor, created.EvidenceNId, CancellationToken.None);

        Assert.Equal("Verified", again.Status);
    }

    [Fact]
    public async Task IsVerifiedForAsync_Unverified_ReturnsFalse()
    {
        var store = new FakeDatabaseOrchestrationStore();
        var service = CreateBackupService(store);
        var plan = await SeedPlanAsync(store, DevelopmentTopology);
        await SeedBackupEvidenceAsync(store, plan, verified: false);

        var verified = await service.IsVerifiedForAsync(Tenant, plan, CancellationToken.None);

        Assert.False(verified);
    }

    [Fact]
    public async Task IsVerifiedForAsync_Verified_ReturnsTrue()
    {
        var store = new FakeDatabaseOrchestrationStore();
        var service = CreateBackupService(store);
        var plan = await SeedPlanAsync(store, DevelopmentTopology);
        await SeedBackupEvidenceAsync(store, plan, verified: true);

        var verified = await service.IsVerifiedForAsync(Tenant, plan, CancellationToken.None);

        Assert.True(verified);
    }

    // =====================================================================
    // 假 store 并发语义(版本快照一致性)
    // =====================================================================

    [Fact]
    public async Task FakeStore_StaleVersionUpdate_ThrowsConcurrencyException()
    {
        var store = new FakeDatabaseOrchestrationStore();
        var registration = await SeedRegistrationAsync(store, DevelopmentTopology);

        var staleOptimistic = registration.OptimisticVersion;
        var staleConcurrency = registration.ConcurrencyVersion;

        // 另一写入者提交一次
        registration.UpdateDesiredState(DesiredState.Operational);
        await store.UpdateRegistrationAsync(registration, staleOptimistic, staleConcurrency, CancellationToken.None);

        // 用陈旧期望版本重放 → 版本不匹配
        registration.UpdateDesiredState(DesiredState.Retiring);
        await Assert.ThrowsAsync<ConcurrencyException>(() =>
            store.UpdateRegistrationAsync(registration, staleOptimistic, staleConcurrency, CancellationToken.None));
    }

    [Fact]
    public async Task FakeStore_DuplicateRegistrationKey_ThrowsConcurrencyException()
    {
        var store = new FakeDatabaseOrchestrationStore();
        await SeedRegistrationAsync(store, DevelopmentTopology);

        var duplicate = DatabaseRegistration.Register(
            Tenant,
            "Development",
            "systemdata",
            "Sqlite",
            "systemdata_db",
            "industrial-platform.db",
            true,
            "Shared",
            RegistrationNIdAsTopologyRevision(DevelopmentTopology),
            "artifact-1",
            "1.0.0",
            Sha("artifact"),
            "sig",
            "user-owner",
            DesiredState.SourceOfTruth,
            false,
            false,
            "1",
            Sha("manifest"));

        await Assert.ThrowsAsync<ConcurrencyException>(() =>
            store.AddRegistrationAsync(duplicate, CancellationToken.None));
    }

    // =====================================================================
    // 服务工厂与种子助手
    // =====================================================================

    private static DatabaseRegistrationService CreateRegistrationService(FakeDatabaseOrchestrationStore store, DatabaseTopology topology) =>
        new DatabaseRegistrationService(store, new FakeTopologyProvider(topology));

    private static DatabasePlanService CreatePlanService(FakeDatabaseOrchestrationStore store, DatabaseTopology topology) =>
        new DatabasePlanService(store, new FakeTopologyProvider(topology), Options.Create(new DatabaseOrchestrationOptions()));

    private static DatabaseOperationService CreateOperationService(FakeDatabaseOrchestrationStore store, DatabaseTopology topology)
    {
        var options = Options.Create(new DatabaseOrchestrationOptions());
        return new DatabaseOperationService(
            store,
            new FakeTopologyProvider(topology),
            new DatabaseApprovalService(store),
            new DatabaseBackupService(store, options),
            options);
    }

    private static DatabaseApprovalService CreateApprovalService(FakeDatabaseOrchestrationStore store) =>
        new DatabaseApprovalService(store);

    private static DatabaseBackupService CreateBackupService(FakeDatabaseOrchestrationStore store) =>
        new DatabaseBackupService(store, Options.Create(new DatabaseOrchestrationOptions()));

    private static DatabaseRegistrationManifestV1 CreateManifest(string version = "1.0.0") => new()
    {
        ServiceKey = "systemdata",
        LogicalDatabaseName = "systemdata_db",
        RequestedVersion = version,
        ArtifactChecksum = Sha("artifact"),
        ArtifactSignature = "sig-ref",
    };

    private static DatabasePlanRequestV1 CreatePlanRequest(string version = "1.0.0") => new()
    {
        ServiceKey = "systemdata",
        RequestedVersion = version,
    };

    private static DatabaseApplyRequestV1 CreateApplyRequest(string planNId = "PLAN-001", string version = "1.0.0") => new()
    {
        PlanNId = planNId,
        RequestedVersion = version,
    };

    private static async Task<DatabaseRegistration> SeedRegistrationAsync(
        FakeDatabaseOrchestrationStore store,
        DatabaseTopology topology,
        string serviceKey = "systemdata",
        string version = "1.0.0",
        string? artifactChecksum = null,
        string? topologyRevision = null)
    {
        var provider = topology.EnvironmentName == "Development" ? "Sqlite" : "PostgreSQL";
        var physical = topology.EnvironmentName == "Development"
            ? topology.SharedSqliteFile ?? string.Empty
            : topology.ServiceDatabases.TryGetValue(serviceKey, out var mapped) ? mapped : $"{serviceKey}_db";
        var registration = DatabaseRegistration.Register(
            Tenant,
            topology.EnvironmentName,
            serviceKey,
            provider,
            "systemdata_db",
            physical,
            topology.Mode == DatabaseTopologyMode.Shared,
            topology.Mode.ToString(),
            topologyRevision ?? RegistrationNIdAsTopologyRevision(topology),
            "artifact-1",
            version,
            artifactChecksum ?? Sha("artifact"),
            "sig",
            "user-owner",
            DesiredState.SourceOfTruth,
            false,
            false,
            "1",
            Sha("manifest"));
        await store.AddRegistrationAsync(registration, CancellationToken.None);
        return registration;
    }

    private static async Task<DatabaseProvisionPlan> SeedPlanAsync(
        FakeDatabaseOrchestrationStore store,
        DatabaseTopology topology,
        string requestedVersion = "1.0.0",
        string? fingerprintOverride = null,
        DateTimeOffset? expiresOn = null,
        bool seedRegistration = true)
    {
        var registration = seedRegistration
            ? await store.GetRegistrationAsync(Tenant, topology.EnvironmentName, "systemdata", CancellationToken.None)
                ?? await SeedRegistrationAsync(store, topology)
            : null;
        var fingerprint = fingerprintOverride ?? (registration is null
            ? Sha($"stale-plan-{requestedVersion}")
            : ComputeTargetFingerprint(registration, requestedVersion));
        var plan = DatabaseProvisionPlan.Create(
            Tenant,
            "PLAN-001",
            topology.EnvironmentName,
            "systemdata",
            requestedVersion,
            "0.9.0",
            fingerprint,
            RiskLevel.Medium,
            destructiveChangeDetected: false,
            DatabasePlanRequiredPolicies.None,
            expiresOn ?? DateTimeOffset.UtcNow.AddMinutes(30),
            "user-planner",
            [
                new DatabasePlanStep(1, "validate", null, null, null, RiskLevel.Low),
                new DatabasePlanStep(2, "inspect", null, null, null, RiskLevel.Low),
                new DatabasePlanStep(3, "migrate", null, null, null, RiskLevel.High),
            ]);
        await store.AddPlanAsync(plan, CancellationToken.None);
        return plan;
    }

    private static string ComputeTargetFingerprint(DatabaseRegistration registration, string requestedVersion)
    {
        var production = registration.EnvironmentNId == "Production";
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
            approvalRequired: production,
            backupRequired: production);
    }

    private static async Task SeedApprovalAsync(
        FakeDatabaseOrchestrationStore store,
        DatabaseProvisionPlan plan,
        DateTimeOffset? approvedOn = null,
        DateTimeOffset? expiresOn = null)
    {
        var approval = DatabaseApproval.Create(
            Tenant,
            "APR-001",
            plan.PlanNId,
            plan.PlanChecksum,
            plan.TargetStateFingerprint,
            "user-approver",
            "approved",
            approvedOn ?? DateTimeOffset.UtcNow.AddMinutes(-5),
            expiresOn ?? DateTimeOffset.UtcNow.AddMinutes(30));
        await store.AddApprovalAsync(approval, CancellationToken.None);
    }

    private static Task<DatabaseBackupEvidence> SeedVerifiedBackupAsync(FakeDatabaseOrchestrationStore store, DatabaseProvisionPlan plan) =>
        SeedBackupEvidenceAsync(store, plan, verified: true);

    private static async Task<DatabaseBackupEvidence> SeedBackupEvidenceAsync(
        FakeDatabaseOrchestrationStore store,
        DatabaseProvisionPlan plan,
        bool verified)
    {
        var evidence = DatabaseBackupEvidence.Create(
            Tenant,
            "EVD-001",
            plan.PlanNId,
            plan.PlanChecksum,
            plan.TargetStateFingerprint,
            "aws-s3",
            "snapshot-001",
            DateTimeOffset.UtcNow.AddMinutes(-10),
            DateTimeOffset.UtcNow.AddDays(7));
        if (verified)
        {
            evidence.Verify("user-verifier", DateTimeOffset.UtcNow);
        }

        await store.AddBackupEvidenceAsync(evidence, CancellationToken.None);
        return evidence;
    }

    private static async Task SeedObservationAsync(
        FakeDatabaseOrchestrationStore store,
        DatabaseRegistration registration,
        string observedVersion)
    {
        var observation = DatabaseMigrationObservation.Record(
            Tenant,
            registration.EnvironmentNId,
            registration.ServiceKey,
            Sha("identity"),
            observedVersion,
            registration.ArtifactChecksum,
            DateTimeOffset.UtcNow.AddMinutes(-1),
            "OP-001",
            VerificationStatus.Verified);
        await store.AddObservationAsync(observation, CancellationToken.None);
    }

    private static async Task SeedOperationAsync(
        FakeDatabaseOrchestrationStore store,
        string operationNId,
        OperationKind kind,
        OperationStatus status,
        OperationPhase phase = OperationPhase.Validate,
        string? leaseOwner = null)
    {
        var operation = DatabaseProvisionOperation.Enqueue(
            Tenant,
            operationNId,
            kind,
            "Development",
            "systemdata",
            kind == OperationKind.Apply ? "PLAN-001" : null,
            "1.0.0",
            $"idem-{operationNId}",
            Sha($"req-{operationNId}"),
            DateTimeOffset.UtcNow.AddMinutes(30),
            "trace-001",
            Actor);
        if (status == OperationStatus.Running)
        {
            operation.Start(leaseOwner ?? "runner-1", DateTimeOffset.UtcNow, TimeSpan.FromSeconds(60));
            while (operation.Phase < phase)
            {
                operation.AdvanceToNextPhase(DateTimeOffset.UtcNow);
            }
        }

        await store.AddOperationAsync(operation, CancellationToken.None);
    }

    private static string RegistrationNIdAsTopologyRevision(DatabaseTopology topology) =>
        DatabaseTopologyFingerprint.ComputeTopologyRevision(topology);

    private static string Sha(string value) => DatabaseTopologyFingerprint.Sha256Hex(value);
}
