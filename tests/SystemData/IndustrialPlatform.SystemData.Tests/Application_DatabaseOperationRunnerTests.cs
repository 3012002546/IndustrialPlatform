using IndustrialPlatform.Application.Abstractions.Initialization;
using IndustrialPlatform.Identity.Application.Bootstrap;
using IndustrialPlatform.Identity.Infrastructure.Bootstrap;
using IndustrialPlatform.Identity.Infrastructure.Passwords;
using IndustrialPlatform.Identity.Infrastructure.Persistence.Migrations;
using IndustrialPlatform.Identity.Infrastructure.Persistence.Repositories;
using IndustrialPlatform.Identity.Infrastructure.Persistence.Seeds;
using IndustrialPlatform.SystemData.Application.IdentityDirectory;
using IndustrialPlatform.SystemData.Contracts.Administration;
using IndustrialPlatform.ReferenceData.Infrastructure.Initialization;
using IndustrialPlatform.SystemData.Application.DatabaseOrchestration;
using IndustrialPlatform.SystemData.Application.DatabaseOrchestration.Initialization;
using IndustrialPlatform.SystemData.Application.DatabaseOrchestration.Options;
using IndustrialPlatform.SystemData.Application.DatabaseOrchestration.Runner;
using IndustrialPlatform.SystemData.Contracts.DatabaseOrchestration;
using IndustrialPlatform.SystemData.Domain.DatabaseOrchestration;
using IndustrialPlatform.SharedKernel.Topology;
using IndustrialPlatform.SystemData.Domain.Topology;
using IndustrialPlatform.SystemData.Infrastructure.DatabaseOrchestration.Initialization;
using Microsoft.Extensions.Logging.Abstractions;
using IndustrialPlatform.Infrastructure.Database;
using Microsoft.Extensions.Options;
using SqlSugar;
using NotFoundException = IndustrialPlatform.SystemData.Application.DatabaseOrchestration.NotFoundException;

namespace IndustrialPlatform.SystemData.Application.Tests;

/// <summary>
/// 数据库编排 Runner 编排核心测试(TASK-SD-003)。进程内假端口驱动 Runner:
/// Plan 操作生成不可变计划后完成;Apply 全链路(advisory lock→provision→migrate→verify→观察)成功后完成;
/// 迁移失败标记注册 NotReady;锁超时映射 SD_DB_OPERATION_CONFLICT;迁移前瞬时失败受限重试后成功。
/// 目标适配器/凭据/产物均以假端口注入,不触碰真实数据库。
/// </summary>
public sealed class DatabaseOperationRunnerTests
{
    private const string Tenant = "tenant-001";
    private const string Actor = "user-actor";

    private static readonly DatabaseTopology DevelopmentTopology = new(
        "Development",
        DatabaseTopologyMode.Shared,
        "industrial_platform_dev",
        "industrial-platform.db",
        new Dictionary<string, string>());

    // ===== 领取与计划 =====

    [Fact]
    public async Task RunOnceAsync_NoQueuedOperation_ReturnsZero()
    {
        var store = new FakeDatabaseOrchestrationStore();
        var runner = CreateRunner(store, new FakeTargetAdapter(), DevelopmentTopology);

        var processed = await runner.RunOnceAsync(CancellationToken.None);

        Assert.Equal(0, processed);
    }

    [Fact]
    public async Task RunOnceAsync_PlanOperation_GeneratesPlanAndCompletes()
    {
        var store = new FakeDatabaseOrchestrationStore();
        var adapter = new FakeTargetAdapter(inspections: [new DatabaseTargetInspection(true, null, null, [])]);
        var runner = CreateRunner(store, adapter, DevelopmentTopology);
        await SeedRegistrationAsync(store, DevelopmentTopology, version: "9.9.9");
        await SeedOperationAsync(store, "OP-PLAN", OperationKind.Plan, planNId: null, requestedVersion: "9.9.9");

        var processed = await runner.RunOnceAsync(CancellationToken.None);

        Assert.Equal(1, processed);
        var operation = store.Operations.Single();
        Assert.Equal(OperationStatus.Succeeded, operation.Status);
        var plan = store.Plans.Single();
        Assert.Equal("9.9.9", plan.RequestedMigrationVersion);
        Assert.False(string.IsNullOrWhiteSpace(plan.TargetStateFingerprint));
    }

    [Fact]
    public async Task RunOnceAsync_ServiceOwnedRegistration_UsesInvokerForPlanAndApplyOnly()
    {
        var store = new FakeDatabaseOrchestrationStore();
        var adapter = new FakeTargetAdapter();
        var invoker = new CountingServiceInitializationInvoker();
        var runner = CreateRunner(store, adapter, DevelopmentTopology, serviceInitializationInvoker: invoker);
        var registration = await SeedRegistrationAsync(
            store,
            DevelopmentTopology,
            version: "9.9.9",
            moduleKey: "systemdata",
            usesServiceInitializer: true);
        await SeedOperationAsync(store, "OP-SERVICE-PLAN", OperationKind.Plan, null, "9.9.9", "systemdata");

        Assert.Equal(1, await runner.RunOnceAsync(CancellationToken.None));

        var plan = store.Plans.Single();
        Assert.Equal(OperationStatus.Succeeded, store.Operations.Single().Status);
        Assert.Equal(1, invoker.InspectCount);
        Assert.Equal(1, invoker.PlanCount);
        Assert.Equal(0, invoker.ApplyCount);
        Assert.Equal(0, adapter.MigrationApplyCount);

        await SeedOperationAsync(store, "OP-SERVICE-APPLY", OperationKind.Apply, plan.PlanNId, "9.9.9", "systemdata");
        Assert.Equal(1, await runner.RunOnceAsync(CancellationToken.None));

        Assert.Equal(OperationStatus.Succeeded, store.Operations.Single(item => item.OperationNId == "OP-SERVICE-APPLY").Status);
        Assert.Equal(2, invoker.InspectCount);
        Assert.Equal(2, invoker.PlanCount);
        Assert.Equal(1, invoker.ApplyCount);
        Assert.Equal(0, adapter.MigrationApplyCount);
    }

    [Fact]
    public async Task RunOnceAsync_ServiceOwnedApply_RejectsChangedServicePlanBeforeApply()
    {
        var store = new FakeDatabaseOrchestrationStore();
        var invoker = new CountingServiceInitializationInvoker(
            planSteps: planCall => planCall == 1 ? ["approved-step"] : ["changed-unapproved-step"]);
        var runner = CreateRunner(store, new FakeTargetAdapter(), DevelopmentTopology, serviceInitializationInvoker: invoker);
        var registration = await SeedRegistrationWithSeedsAsync(
            store,
            DevelopmentTopology,
            version: "9.9.9",
            moduleKey: "systemdata",
            usesServiceInitializer: true);
        await SeedOperationAsync(store, "OP-SERVICE-DRIFT-PLAN", OperationKind.Plan, null, "9.9.9", "systemdata");
        await runner.RunOnceAsync(CancellationToken.None);
        var plan = store.Plans.Single();

        await SeedOperationAsync(store, "OP-SERVICE-DRIFT-APPLY", OperationKind.Apply, plan.PlanNId, "9.9.9", "systemdata");
        await runner.RunOnceAsync(CancellationToken.None);

        var operation = store.Operations.Single(item => item.OperationNId == "OP-SERVICE-DRIFT-APPLY");
        Assert.Equal(OperationStatus.Failed, operation.Status);
        Assert.Equal(DatabaseOrchestrationRunnerErrors.PlanDrift, operation.SanitizedErrorCode);
        Assert.Equal(0, invoker.ApplyCount);
        Assert.NotNull(registration);
    }

    [Fact]
    public async Task RunOnceAsync_ServiceOwnedVerify_RejectsMissingSeedFact()
    {
        var store = new FakeDatabaseOrchestrationStore();
        var seed = CreateServiceSeed();
        var invoker = new CountingServiceInitializationInvoker(verificationSeeds: []);
        var runner = CreateRunner(store, new FakeTargetAdapter(), DevelopmentTopology, serviceInitializationInvoker: invoker);
        var registration = await SeedRegistrationWithSeedsAsync(
            store,
            DevelopmentTopology,
            version: "9.9.9",
            moduleKey: "systemdata",
            usesServiceInitializer: true,
            seedSets: [seed]);
        await SeedOperationAsync(store, "OP-SERVICE-SEED-MISSING-PLAN", OperationKind.Plan, null, "9.9.9", "systemdata");
        await runner.RunOnceAsync(CancellationToken.None);
        var plan = store.Plans.Single();

        await SeedOperationAsync(store, "OP-SERVICE-SEED-MISSING-APPLY", OperationKind.Apply, plan.PlanNId, "9.9.9", "systemdata");
        await runner.RunOnceAsync(CancellationToken.None);

        var operation = store.Operations.Single(item => item.OperationNId == "OP-SERVICE-SEED-MISSING-APPLY");
        Assert.Equal(OperationStatus.Failed, operation.Status);
        Assert.Equal(DatabaseOrchestrationRunnerErrors.TargetMismatch, operation.SanitizedErrorCode);
        Assert.Null(await store.GetLatestSeedObservationAsync(
            Tenant, registration.EnvironmentNId, registration.ServiceKey, registration.ModuleKey, seed.SeedKey, CancellationToken.None));
    }

    [Fact]
    public async Task RunOnceAsync_ServiceOwnedVerify_RejectsSeedChecksumDriftAndRequiredSkipped()
    {
        foreach (var (suffix, status, checksum) in new[]
        {
            ("CHECKSUM", "Applied", Sha("wrong-service-seed")),
            ("SKIPPED", "Skipped", Sha("service-seed")),
        })
        {
            var store = new FakeDatabaseOrchestrationStore();
            var seed = CreateServiceSeed();
            var invoker = new CountingServiceInitializationInvoker(
                verificationSeeds:
                [new ServiceInitializationSeedState(
                    seed.SeedKey,
                    seed.SeedVersion,
                    status,
                    DateTimeOffset.UtcNow,
                    checksum,
                    seed.Scope.ToString())]);
            var runner = CreateRunner(store, new FakeTargetAdapter(), DevelopmentTopology, serviceInitializationInvoker: invoker);
            var registration = await SeedRegistrationWithSeedsAsync(
                store,
                DevelopmentTopology,
                version: "9.9.9",
                moduleKey: "systemdata",
                usesServiceInitializer: true,
                seedSets: [seed]);
            await SeedOperationAsync(store, $"OP-SERVICE-SEED-{suffix}-PLAN", OperationKind.Plan, null, "9.9.9", "systemdata");
            await runner.RunOnceAsync(CancellationToken.None);
            var plan = store.Plans.Single();

            await SeedOperationAsync(store, $"OP-SERVICE-SEED-{suffix}-APPLY", OperationKind.Apply, plan.PlanNId, "9.9.9", "systemdata");
            await runner.RunOnceAsync(CancellationToken.None);

            var operation = store.Operations.Single(item => item.OperationNId == $"OP-SERVICE-SEED-{suffix}-APPLY");
            Assert.Equal(OperationStatus.Failed, operation.Status);
            Assert.Equal(DatabaseOrchestrationRunnerErrors.TargetMismatch, operation.SanitizedErrorCode);
            Assert.Null(await store.GetLatestSeedObservationAsync(
                Tenant, registration.EnvironmentNId, registration.ServiceKey, registration.ModuleKey, seed.SeedKey, CancellationToken.None));
        }
    }

    [Fact]
    public async Task RunOnceAsync_ServiceOwnedRegistrationWithoutInvoker_FailsClosed()
    {
        var store = new FakeDatabaseOrchestrationStore();
        var runner = CreateRunner(store, new FakeTargetAdapter(), DevelopmentTopology);
        await SeedRegistrationAsync(
            store,
            DevelopmentTopology,
            version: "9.9.9",
            moduleKey: "systemdata",
            usesServiceInitializer: true);
        await SeedOperationAsync(store, "OP-SERVICE-NO-INVOKER", OperationKind.Plan, null, "9.9.9", "systemdata");

        await runner.RunOnceAsync(CancellationToken.None);

        var operation = store.Operations.Single();
        Assert.Equal(OperationStatus.Failed, operation.Status);
        Assert.Equal(DatabaseOrchestrationRunnerErrors.InternalFailure, operation.SanitizedErrorCode);
    }

    [Fact]
    public async Task RunOnceAsync_ServiceOwnedReferenceData_UsesRealInitializerAndReadiness()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"industrial-platform-runner-referencedata-{Guid.NewGuid():N}.db");
        try
        {
            using var dbContext = new IndustrialPlatform.Infrastructure.Database.SqlSugarDbContext(Options.Create(new SqlSugarOptions
            {
                ConnectionString = $"Data Source={dbPath}",
                DbType = DbType.Sqlite,
            }));
            var initializer = new ReferenceDataServiceInitializer(new ReferenceDataInitializationLedger(dbContext));
            var invoker = new InProcessServiceInitializationInvoker([initializer]);
            var store = new FakeDatabaseOrchestrationStore();
            var runner = CreateRunner(store, new FakeTargetAdapter(), DevelopmentTopology, serviceInitializationInvoker: invoker);
            var seed = new SeedSet(
                ReferenceDataServiceInitializer.BaselineSeedKey,
                ReferenceDataServiceInitializer.BaselineVersion,
                SeedClass.SystemBaseline,
                SeedScope.System,
                "reference-data-baseline",
                ReferenceDataServiceInitializer.BaselineChecksum,
                "sig-ref",
                requiredForReadiness: true,
                allowedEnvironments: string.Empty,
                dependsOnMigrationVersion: ReferenceDataServiceInitializer.BaselineVersion,
                dependsOnSeedKeys: null,
                BootstrapPolicy.FailClosed);
            var registration = await SeedServiceRegistrationAsync(
                store,
                DevelopmentTopology,
                "referencedata",
                "referencedata",
                ReferenceDataServiceInitializer.BaselineVersion,
                [seed]);

            await SeedServiceOperationAsync(
                store,
                DevelopmentTopology,
                "OP-REAL-REFERENCEDATA-PLAN",
                OperationKind.Plan,
                "referencedata",
                "referencedata",
                null,
                ReferenceDataServiceInitializer.BaselineVersion);
            Assert.Equal(1, await runner.RunOnceAsync(CancellationToken.None));
            var plan = store.Plans.Single();
            Assert.True(plan.ServiceRequiresApply);

            await SeedServiceOperationAsync(
                store,
                DevelopmentTopology,
                "OP-REAL-REFERENCEDATA-APPLY",
                OperationKind.Apply,
                "referencedata",
                "referencedata",
                plan.PlanNId,
                ReferenceDataServiceInitializer.BaselineVersion);
            Assert.Equal(1, await runner.RunOnceAsync(CancellationToken.None));

            var apply = store.Operations.Single(item => item.OperationNId == "OP-REAL-REFERENCEDATA-APPLY");
            Assert.Equal(OperationStatus.Succeeded, apply.Status);
            var observation = await store.GetLatestSeedObservationAsync(
                Tenant,
                registration.EnvironmentNId,
                registration.ServiceKey,
                registration.ModuleKey,
                seed.SeedKey,
                CancellationToken.None);
            Assert.NotNull(observation);
            Assert.Equal(seed.SeedChecksum, observation!.Checksum);
            Assert.Equal(seed.Scope, observation.Scope);

            var readiness = await CreateOperationService(store).GetReadinessV2Async(
                Tenant,
                "referencedata",
                "referencedata",
                "trace-real-referencedata",
                CancellationToken.None);
            Assert.True(readiness.Ready);
            Assert.True(readiness.RequiredSeedReady);
        }
        finally
        {
            TryDeleteTempFile(dbPath);
        }
    }

    [Fact]
    public async Task RunOnceAsync_ServiceOwnedIdentity_UsesRealInitializerSeedFactsAndReadiness()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"industrial-platform-runner-identity-{Guid.NewGuid():N}.db");
        try
        {
            using var dbContext = new SqlSugarDbContext(Options.Create(new SqlSugarOptions
            {
                ConnectionString = $"Data Source={dbPath};Foreign Keys=True;Default Timeout=30",
                DbType = DbType.Sqlite,
            }));
            var bootstrapStore = new BootstrapStore(dbContext, new UserRepository(dbContext));
            var initialization = new IdentityInitializationService(
                new SchemaMigrationRunner(dbContext, IdentitySchemaMigrations.All, NullLogger<SchemaMigrationRunner>.Instance),
                new IdentitySeedRunner(dbContext, new BcryptPasswordHasher(), new BootstrapCredentialStore(dbContext)),
                bootstrapStore);
            var initializer = new IdentityServiceInitializer(
                initialization,
                new BootstrapService(
                    bootstrapStore,
                    new BootstrapCredentialStore(dbContext),
                    new TemporaryPasswordGenerator(),
                    new BcryptPasswordHasher()));
            var invoker = new InProcessServiceInitializationInvoker([initializer]);
            var store = new FakeDatabaseOrchestrationStore();
            var runner = CreateRunner(store, new FakeTargetAdapter(), DevelopmentTopology, serviceInitializationInvoker: invoker);
            var systemChecksum = BootstrapSeedCatalog.Checksum(BootstrapSeedCatalog.SystemCatalogContent());
            var tenantChecksum = BootstrapSeedCatalog.Checksum(BootstrapSeedCatalog.TenantSecurityContent(Tenant));
            var seeds = new SeedSet[]
            {
                new(
                    BootstrapSeedCatalog.SystemCatalogSeedKey,
                    BootstrapSeedCatalog.SeedVersion,
                    SeedClass.SystemBaseline,
                    SeedScope.System,
                    "identity-system-catalog",
                    systemChecksum,
                    "sig-identity",
                    requiredForReadiness: true,
                    allowedEnvironments: string.Empty,
                    dependsOnMigrationVersion: IdentitySchemaMigrations.All[^1].Id,
                    dependsOnSeedKeys: null,
                    BootstrapPolicy.FailClosed),
                new(
                    BootstrapSeedCatalog.TenantSecuritySeedKey,
                    BootstrapSeedCatalog.SeedVersion,
                    SeedClass.TenantBaseline,
                    SeedScope.Tenant,
                    "identity-tenant-security",
                    tenantChecksum,
                    "sig-identity",
                    requiredForReadiness: true,
                    allowedEnvironments: string.Empty,
                    dependsOnMigrationVersion: IdentitySchemaMigrations.All[^1].Id,
                    dependsOnSeedKeys: null,
                    BootstrapPolicy.FailClosed),
            };
            var registration = await SeedServiceRegistrationAsync(
                store,
                DevelopmentTopology,
                "identity",
                "identity",
                IdentitySchemaMigrations.All[^1].Id,
                seeds);

            await SeedServiceOperationAsync(
                store,
                DevelopmentTopology,
                "OP-REAL-IDENTITY-PLAN",
                OperationKind.Plan,
                "identity",
                "identity",
                null,
                IdentitySchemaMigrations.All[^1].Id);
            Assert.Equal(1, await runner.RunOnceAsync(CancellationToken.None));
            var plan = store.Plans.Single();
            Assert.True(plan.ServiceRequiresApply);

            await SeedServiceOperationAsync(
                store,
                DevelopmentTopology,
                "OP-REAL-IDENTITY-APPLY",
                OperationKind.Apply,
                "identity",
                "identity",
                plan.PlanNId,
                IdentitySchemaMigrations.All[^1].Id);
            Assert.Equal(1, await runner.RunOnceAsync(CancellationToken.None));

            var apply = store.Operations.Single(item => item.OperationNId == "OP-REAL-IDENTITY-APPLY");
            Assert.Equal(OperationStatus.Succeeded, apply.Status);
            foreach (var seed in seeds)
            {
                var observation = await store.GetLatestSeedObservationAsync(
                    Tenant,
                    registration.EnvironmentNId,
                    registration.ServiceKey,
                    registration.ModuleKey,
                    seed.SeedKey,
                    CancellationToken.None);
                Assert.NotNull(observation);
                Assert.Equal(seed.SeedChecksum, observation!.Checksum);
                Assert.Equal(seed.Scope, observation.Scope);
            }

            var finalReadiness = await CreateOperationService(store).GetReadinessV2Async(
                Tenant,
                "identity",
                "identity",
                "trace-real-identity",
                CancellationToken.None);
            Assert.True(finalReadiness.Ready);
            Assert.True(finalReadiness.RequiredSeedReady);
        }
        finally
        {
            TryDeleteTempFile(dbPath);
        }
    }

    // ===== Apply 全链路 =====

    [Fact]
    public async Task RunOnceAsync_ApplyOperation_CompletesThroughVerifyAndRecordsObservation()
    {
        var store = new FakeDatabaseOrchestrationStore();
        // 第 1 次 inspect(Apply 门禁)报告目标已存在且版本与计划一致;第 2 次(Verify)报告达到期望版本。
        var adapter = new FakeTargetAdapter(
            inspections:
            [
                new DatabaseTargetInspection(true, "0.9.0", null, []),
                new DatabaseTargetInspection(true, "9.9.9", null, ["step-1", "step-2"]),
            ]);
        var runner = CreateRunner(store, adapter, DevelopmentTopology);
        var registration = await SeedRegistrationAsync(store, DevelopmentTopology, version: "9.9.9");
        var plan = await SeedPlanAsync(store, DevelopmentTopology, registration, requestedVersion: "9.9.9");
        await SeedOperationAsync(store, "OP-APPLY", OperationKind.Apply, planNId: plan.PlanNId, requestedVersion: "9.9.9");

        var processed = await runner.RunOnceAsync(CancellationToken.None);

        Assert.Equal(1, processed);
        var operation = store.Operations.Single();
        Assert.Equal(OperationStatus.Succeeded, operation.Status);
        var observation = await store.GetLatestObservationAsync(
            Tenant, registration.EnvironmentNId, registration.ServiceKey, CancellationToken.None);
        Assert.NotNull(observation);
        Assert.Equal("9.9.9", observation!.ObservedVersion);
        Assert.Equal(2, adapter.InspectCallCount);
    }

    [Fact]
    public async Task RunOnceAsync_ApplyWithRevokedOperator_FailsBeforeTargetInspection()
    {
        var store = new FakeDatabaseOrchestrationStore();
        var adapter = new FakeTargetAdapter(
            inspections: [new DatabaseTargetInspection(true, "0.9.0", null, [])]);
        var directory = new FakeRunnerIdentityUserDirectory(Tenant, Actor, isSystemAdmin: false);
        var runner = CreateRunner(store, adapter, DevelopmentTopology, identityUserDirectory: directory);
        var registration = await SeedRegistrationAsync(store, DevelopmentTopology, version: "9.9.9");
        var plan = await SeedPlanAsync(store, DevelopmentTopology, registration, requestedVersion: "9.9.9");
        await SeedOperationAsync(store, "OP-REVOKED", OperationKind.Apply, plan.PlanNId, "9.9.9");

        Assert.Equal(1, await runner.RunOnceAsync(CancellationToken.None));

        var operation = store.Operations.Single();
        Assert.Equal(OperationStatus.Failed, operation.Status);
        Assert.Equal("SD_DB_OPERATOR_NOT_AUTHORIZED", operation.SanitizedErrorCode);
        Assert.Equal(0, adapter.InspectCallCount);
    }

    [Fact]
    public async Task RunOnceAsync_ApplyLockTimeout_FailsOperationConflict()
    {
        var store = new FakeDatabaseOrchestrationStore();
        var adapter = new FakeTargetAdapter(inspections: [new DatabaseTargetInspection(true, "0.9.0", null, [])]);
        var lockPort = new FakeAdvisoryLock(handle: null);
        var runner = CreateRunner(store, adapter, DevelopmentTopology, lockPort: lockPort);
        var registration = await SeedRegistrationAsync(store, DevelopmentTopology, version: "9.9.9");
        var plan = await SeedPlanAsync(store, DevelopmentTopology, registration, requestedVersion: "9.9.9");
        await SeedOperationAsync(store, "OP-LOCK", OperationKind.Apply, planNId: plan.PlanNId, requestedVersion: "9.9.9");

        var processed = await runner.RunOnceAsync(CancellationToken.None);

        Assert.Equal(1, processed);
        var operation = store.Operations.Single();
        Assert.Equal(OperationStatus.Failed, operation.Status);
        Assert.Equal(DatabaseOrchestrationRunnerErrors.OperationConflict, operation.SanitizedErrorCode);
    }

    [Fact]
    public async Task RunOnceAsync_MigrateFails_MarksFailedAndRegistrationNotReady()
    {
        var store = new FakeDatabaseOrchestrationStore();
        var adapter = new FakeTargetAdapter(
            inspections: [new DatabaseTargetInspection(true, "0.9.0", null, [])],
            executeFailure: new DatabaseOrchestrationRunnerException(500, DatabaseOrchestrationRunnerErrors.MigrationFailed, "迁移失败。"));
        var runner = CreateRunner(store, adapter, DevelopmentTopology);
        var registration = await SeedRegistrationAsync(store, DevelopmentTopology, version: "9.9.9");
        var plan = await SeedPlanAsync(store, DevelopmentTopology, registration, requestedVersion: "9.9.9");
        await SeedOperationAsync(store, "OP-MIG-FAIL", OperationKind.Apply, planNId: plan.PlanNId, requestedVersion: "9.9.9");

        var processed = await runner.RunOnceAsync(CancellationToken.None);

        Assert.Equal(1, processed);
        var operation = store.Operations.Single();
        Assert.Equal(OperationStatus.Failed, operation.Status);
        Assert.Equal(DatabaseOrchestrationRunnerErrors.MigrationFailed, operation.SanitizedErrorCode);
        var current = await store.GetRegistrationAsync(
            Tenant, registration.EnvironmentNId, registration.ServiceKey, CancellationToken.None);
        Assert.Equal(RegistrationStatus.NotReady, current!.Status);
    }

    [Fact]
    public async Task RunOnceAsync_PreMigrateTransientFailure_RetriesThenSucceeds()
    {
        var store = new FakeDatabaseOrchestrationStore();
        // 第 1 次 inspect 抛瞬时异常(不推进阶段);第 2 次返回门禁版本;第 3 次(Verify)返回期望版本。
        var adapter = new FakeTargetAdapter(
            inspections:
            [
                new DatabaseTargetInspection(true, "0.9.0", null, []),
                new DatabaseTargetInspection(true, "9.9.9", null, []),
            ],
            throwFirstInspect: true);
        var runner = CreateRunner(store, adapter, DevelopmentTopology);
        var registration = await SeedRegistrationAsync(store, DevelopmentTopology, version: "9.9.9");
        var plan = await SeedPlanAsync(store, DevelopmentTopology, registration, requestedVersion: "9.9.9");
        await SeedOperationAsync(store, "OP-RETRY", OperationKind.Apply, planNId: plan.PlanNId, requestedVersion: "9.9.9");

        var processed = await runner.RunOnceAsync(CancellationToken.None);

        Assert.Equal(1, processed);
        var operation = store.Operations.Single();
        Assert.Equal(OperationStatus.Succeeded, operation.Status);
        Assert.Equal(3, adapter.InspectCallCount);
    }

    // ===== 假端口 =====

    private sealed class FakeTargetAdapter : ITargetDatabaseInspector, ITargetDatabaseProvisioner, IMigrationExecutor
    {
        private readonly IReadOnlyList<DatabaseTargetInspection> _inspections;
        private readonly bool _throwFirstInspect;
        private readonly DatabaseOrchestrationException? _executeFailure;
        private int _inspectIndex;

        public FakeTargetAdapter(
            IReadOnlyList<DatabaseTargetInspection>? inspections = null,
            bool throwFirstInspect = false,
            DatabaseOrchestrationException? executeFailure = null)
        {
            _inspections = inspections ?? [new DatabaseTargetInspection(true, null, null, [])];
            _throwFirstInspect = throwFirstInspect;
            _executeFailure = executeFailure;
        }

        public int InspectCallCount { get; private set; }

        public int MigrationApplyCount { get; private set; }

        public Task<DatabaseTargetInspection> InspectAsync(
            ResolvedDatabaseTarget target,
            DatabaseTargetCredentials credentials,
            string moduleKey,
            CancellationToken cancellationToken)
        {
            InspectCallCount++;
            if (_throwFirstInspect && InspectCallCount == 1)
            {
                throw new InvalidOperationException("瞬时不可用。");
            }

            var index = Math.Min(_inspectIndex++, _inspections.Count - 1);
            return Task.FromResult(_inspections[index]);
        }

        public Task<DatabaseProvisionOutcome> EnsureDatabaseAsync(
            ResolvedDatabaseTarget target,
            TargetDatabaseConnection admin,
            CancellationToken cancellationToken) =>
            Task.FromResult(new DatabaseProvisionOutcome(DatabaseCreated: false, DatabaseExisted: true));

        public Task<ProvisionedRoles> EnsureRolesAsync(
            ResolvedDatabaseTarget target,
            TargetDatabaseConnection admin,
            CancellationToken cancellationToken)
        {
            var migrator = new TargetDatabaseConnection("Sqlite", string.Empty, 0, target.PhysicalDatabaseName, null, "sd_systemdata_migrator", "migrator-secret");
            var runtime = new TargetDatabaseConnection("Sqlite", string.Empty, 0, target.PhysicalDatabaseName, null, "sd_systemdata_runtime", "runtime-secret");
            return Task.FromResult(new ProvisionedRoles(migrator, runtime));
        }

        public Task<MigrationExecutionResult> ApplyAsync(
            ResolvedDatabaseTarget target,
            DatabaseMigrationArtifact artifact,
            TargetDatabaseConnection migrator,
            string moduleKey,
            CancellationToken cancellationToken)
        {
            MigrationApplyCount++;
            if (_executeFailure is not null)
            {
                throw _executeFailure;
            }

            return Task.FromResult(new MigrationExecutionResult(artifact.Steps.Count, "9.9.9"));
        }
    }

    private sealed class FakeArtifactStore : IMigrationArtifactStore
    {
        public Task<DatabaseMigrationArtifact> ResolveAsync(string artifactId, CancellationToken cancellationToken) =>
            Task.FromResult(new DatabaseMigrationArtifact(
                artifactId,
                "9.9.9",
                Sha("artifact"),
                "sig-ref",
                DeclaresRecoverable: false,
                [new DatabaseMigrationArtifactStep(1, "step-1", "CREATE TABLE t (id INTEGER);", null, false)]));
    }

    private sealed class FakeArtifactVerifier : IMigrationArtifactVerifier
    {
        public Task<bool> VerifyAsync(
            DatabaseMigrationArtifact artifact,
            string expectedChecksum,
            string? expectedSignatureRef,
            CancellationToken cancellationToken) =>
            Task.FromResult(true);
    }

    private sealed class FakeCredentialPort : IDatabaseCredentialResolver, IDatabaseCredentialSink
    {
        public Task<DatabaseTargetCredentials> ResolveAsync(
            ResolvedDatabaseTarget target,
            bool provisionRequired,
            CancellationToken cancellationToken)
        {
            var connection = new TargetDatabaseConnection("Sqlite", string.Empty, 0, target.PhysicalDatabaseName, null, null, null);
            return Task.FromResult(new DatabaseTargetCredentials(
                provisionRequired ? connection : null,
                connection,
                connection));
        }

        public Task WriteAsync(
            ResolvedDatabaseTarget target,
            ProvisionedRoles roles,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class FakeSeedArtifactStore : ISeedArtifactStore
    {
        public Task<SeedArtifact> ResolveAsync(string seedArtifactId, CancellationToken cancellationToken) =>
            Task.FromResult(new SeedArtifact(
                seedArtifactId,
                "1.0.0",
                Sha("seed"),
                "sig-ref",
                SeedExecutorKind.SqlBundle,
                [new SeedArtifactStep(1, "seed-1", "INSERT INTO t VALUES (1);", null)]));
    }

    private sealed class FakeSeedArtifactVerifier : ISeedArtifactVerifier
    {
        public Task<bool> VerifyAsync(
            SeedArtifact artifact,
            string checksum,
            string? signatureRef,
            CancellationToken cancellationToken) =>
            Task.FromResult(true);
    }

    private sealed class FakeSeedExecutor : ISeedExecutor
    {
        public SeedExecutorKind Kind => SeedExecutorKind.SqlBundle;

        public Task<SeedLedgerEntry?> ReadLedgerAsync(SeedLedgerQuery query, CancellationToken cancellationToken) =>
            Task.FromResult<SeedLedgerEntry?>(null);

        public Task<SeedExecutionResult> ExecuteAsync(SeedExecutionRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new SeedExecutionResult(
                true,
                SeedStatus.Applied,
                DateTimeOffset.UtcNow,
                null,
                null));
    }

    private sealed class FakeSeedSecretResolver : ISeedSecretResolver
    {
        public Task<string?> TryResolveAsync(
            string secretRef,
            string environmentNId,
            string serviceKey,
            string moduleKey,
            CancellationToken cancellationToken) =>
            Task.FromResult<string?>("resolved-secret");
    }

    private sealed class CountingServiceInitializationInvoker : IServiceInitializationInvoker
    {
        private readonly Func<int, IReadOnlyList<string>> _planSteps;
        private readonly IReadOnlyList<ServiceInitializationSeedState>? _verificationSeeds;

        public CountingServiceInitializationInvoker(
            Func<int, IReadOnlyList<string>>? planSteps = null,
            IReadOnlyList<ServiceInitializationSeedState>? verificationSeeds = null)
        {
            _planSteps = planSteps ?? (_ => ["service-owned migration"]);
            _verificationSeeds = verificationSeeds;
        }

        public int InspectCount { get; private set; }
        public int PlanCount { get; private set; }
        public int ApplyCount { get; private set; }

        public Task<ServiceInitializationState> InspectAsync(
            ServiceInitializationContext context,
            CancellationToken cancellationToken)
        {
            InspectCount++;
            return Task.FromResult(new ServiceInitializationState(
                context.ServiceKey,
                context.ModuleKey,
                "0.9.0",
                false,
                true,
                true,
                false,
                "service needs apply"));
        }

        public Task<ServiceInitializationPlan> PlanAsync(
            ServiceInitializationContext context,
            ServiceInitializationState inspection,
            CancellationToken cancellationToken)
        {
            PlanCount++;
            return Task.FromResult(new ServiceInitializationPlan(
                context.ServiceKey,
                context.ModuleKey,
                inspection.ObservedVersion,
                context.DesiredVersion,
                true,
                _planSteps(PlanCount)));
        }

        public Task<ServiceInitializationState> ApplyAsync(
            ServiceInitializationContext context,
            ServiceInitializationPlan plan,
            CancellationToken cancellationToken)
        {
            ApplyCount++;
            return Task.FromResult(new ServiceInitializationState(
                context.ServiceKey,
                context.ModuleKey,
                context.DesiredVersion,
                true,
                true,
                true,
                true,
                null,
                _verificationSeeds));
        }

        public Task<ServiceInitializationState> VerifyAsync(
            ServiceInitializationContext context,
            CancellationToken cancellationToken) =>
            Task.FromResult(new ServiceInitializationState(
                context.ServiceKey,
                context.ModuleKey,
                context.DesiredVersion,
                true,
                true,
                true,
                true,
                null,
                _verificationSeeds));
    }

    private sealed class FakeAdvisoryLock : ITargetDatabaseAdvisoryLock
    {
        private readonly IDisposable? _handle;

        public FakeAdvisoryLock()
            : this(new FakeLockHandle())
        {
        }

        public FakeAdvisoryLock(IDisposable? handle) => _handle = handle;

        public Task<IDisposable?> AcquireAsync(
            DatabaseTargetLockKey key,
            TargetDatabaseConnection connection,
            TimeSpan timeout,
            CancellationToken cancellationToken) =>
            Task.FromResult(_handle);
    }

    private sealed class FakeLockHandle : IDisposable
    {
        public bool Disposed { get; private set; }

        public void Dispose() => Disposed = true;
    }

    // ===== 构造与种子 =====

    private static DatabaseOperationRunner CreateRunner(
        FakeDatabaseOrchestrationStore store,
        FakeTargetAdapter adapter,
        DatabaseTopology topology,
        FakeAdvisoryLock? lockPort = null,
        IServiceInitializationInvoker? serviceInitializationInvoker = null,
        IIdentityUserDirectory? identityUserDirectory = null)
    {
        var options = Options.Create(new DatabaseOrchestrationOptions());
        var runnerOptions = Options.Create(new DatabaseOperationRunnerOptions
        {
            InstanceId = "runner-test",
            AdvisoryLockTimeoutSeconds = 2,
        });
        return new DatabaseOperationRunner(
            store,
            new FakeTopologyProvider(topology),
            new DatabaseApprovalService(store),
            new DatabaseBackupService(store, options),
            options,
            runnerOptions,
            adapter,
            adapter,
            new FakeArtifactStore(),
            new FakeArtifactVerifier(),
            adapter,
            new FakeCredentialPort(),
            new FakeCredentialPort(),
            lockPort ?? new FakeAdvisoryLock(),
            new FakeSeedArtifactStore(),
            new FakeSeedArtifactVerifier(),
            [new FakeSeedExecutor()],
            new FakeSeedSecretResolver(),
            serviceInitializationInvoker,
            identityUserDirectory ?? new FakeRunnerIdentityUserDirectory(Tenant, Actor, isSystemAdmin: true));
    }

    private sealed class FakeRunnerIdentityUserDirectory : IIdentityUserDirectory
    {
        private readonly string _tenantNId;
        private readonly IdentityUserDirectoryEntryV1? _entry;

        public FakeRunnerIdentityUserDirectory(string tenantNId, string userNId, bool isSystemAdmin)
        {
            _tenantNId = tenantNId;
            _entry = new IdentityUserDirectoryEntryV1
            {
                TenantNId = tenantNId,
                UserNId = userNId,
                LoginName = userNId,
                Name = userNId,
                Status = "Active",
                IsSystemAdmin = isSystemAdmin,
            };
        }

        public Task<IdentityUserDirectoryEntryV1?> GetAsync(
            string tenantNId,
            string userNId,
            CancellationToken cancellationToken) =>
            Task.FromResult(
                string.Equals(tenantNId, _tenantNId, StringComparison.Ordinal)
                    ? _entry
                    : null);
    }

    private static DatabaseOperationService CreateOperationService(FakeDatabaseOrchestrationStore store) =>
        new(
            store,
            new FakeTopologyProvider(DevelopmentTopology),
            new DatabaseApprovalService(store),
            new DatabaseBackupService(store, Options.Create(new DatabaseOrchestrationOptions())),
            Options.Create(new DatabaseOrchestrationOptions()));

    private static async Task<DatabaseRegistration> SeedServiceRegistrationAsync(
        FakeDatabaseOrchestrationStore store,
        DatabaseTopology topology,
        string serviceKey,
        string moduleKey,
        string version,
        IReadOnlyCollection<SeedSet> seedSets)
    {
        var registration = DatabaseRegistration.Register(
            Tenant,
            topology.EnvironmentName,
            serviceKey,
            "Sqlite",
            $"{serviceKey}_db",
            topology.SharedSqliteFile ?? "industrial-platform.db",
            isSharedPhysicalDatabase: true,
            DatabaseTopologyMode.Shared.ToString(),
            RegistrationNIdAsTopologyRevision(topology),
            $"{serviceKey}-initializer",
            version,
            Sha($"{serviceKey}-artifact"),
            "sig-ref",
            Actor,
            DesiredState.SourceOfTruth,
            autoProvision: true,
            autoMigrate: true,
            "1",
            Sha($"{serviceKey}-manifest"),
            seedSets,
            moduleKey,
            usesServiceInitializer: true);
        await store.AddRegistrationAsync(registration, CancellationToken.None);
        return registration;
    }

    private static async Task SeedServiceOperationAsync(
        FakeDatabaseOrchestrationStore store,
        DatabaseTopology topology,
        string operationNId,
        OperationKind kind,
        string serviceKey,
        string moduleKey,
        string? planNId,
        string requestedVersion)
    {
        var operation = DatabaseProvisionOperation.Enqueue(
            Tenant,
            operationNId,
            kind,
            topology.EnvironmentName,
            serviceKey,
            planNId,
            requestedVersion,
            $"idem-{operationNId}",
            Sha($"request-{operationNId}"),
            DateTimeOffset.UtcNow.AddMinutes(30),
            $"trace-{operationNId}",
            Actor,
            moduleKey: moduleKey);
        await store.AddOperationAsync(operation, CancellationToken.None);
    }

    private static void TryDeleteTempFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // SQLite 连接池可能短暂占用文件句柄,清理失败不影响断言。
        }
    }

    private static DatabaseRegistrationManifestV1 CreateManifest(string version = "9.9.9") => new()
    {
        ServiceKey = "systemdata",
        LogicalDatabaseName = "systemdata_db",
        RequestedVersion = version,
        ArtifactChecksum = Sha("artifact"),
        ArtifactSignature = "sig-ref",
    };

    private static Task<DatabaseRegistration> SeedRegistrationAsync(
        FakeDatabaseOrchestrationStore store,
        DatabaseTopology topology,
        string version = "9.9.9",
        string? moduleKey = null,
        bool usesServiceInitializer = false) =>
        SeedRegistrationWithSeedsAsync(store, topology, version, moduleKey, usesServiceInitializer, null);

    private static async Task<DatabaseRegistration> SeedRegistrationWithSeedsAsync(
        FakeDatabaseOrchestrationStore store,
        DatabaseTopology topology,
        string version = "9.9.9",
        string? moduleKey = null,
        bool usesServiceInitializer = false,
        IReadOnlyCollection<SeedSet>? seedSets = null)
    {
        var registration = DatabaseRegistration.Register(
            Tenant,
            topology.EnvironmentName,
            "systemdata",
            "Sqlite",
            "systemdata_db",
            topology.SharedSqliteFile ?? string.Empty,
            isSharedPhysicalDatabase: true,
            DatabaseTopologyMode.Shared.ToString(),
            RegistrationNIdAsTopologyRevision(topology),
            "artifact-1",
            version,
            Sha("artifact"),
            "sig-ref",
            "user-owner",
            DesiredState.SourceOfTruth,
            autoProvision: true,
            autoMigrate: false,
            "1",
            Sha("manifest"),
            seedSets: seedSets,
            moduleKey: moduleKey,
            usesServiceInitializer: usesServiceInitializer);
        await store.AddRegistrationAsync(registration, CancellationToken.None);
        return registration;
    }

    private static async Task<DatabaseProvisionPlan> SeedPlanAsync(
        FakeDatabaseOrchestrationStore store,
        DatabaseTopology topology,
        DatabaseRegistration registration,
        string requestedVersion = "9.9.9")
    {
        var fingerprint = DatabaseTopologyFingerprint.ComputeTargetStateFingerprint(
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
            approvalRequired: false,
            backupRequired: false);
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
            DateTimeOffset.UtcNow.AddMinutes(30),
            "user-planner",
            [
                new DatabasePlanStep(1, "validate", null, null, null, RiskLevel.Low),
                new DatabasePlanStep(2, "inspect", null, null, null, RiskLevel.Low),
                new DatabasePlanStep(3, "migrate", null, null, null, RiskLevel.High),
            ]);
        await store.AddPlanAsync(plan, CancellationToken.None);
        return plan;
    }

    private static async Task SeedOperationAsync(
        FakeDatabaseOrchestrationStore store,
        string operationNId,
        OperationKind kind,
        string? planNId,
        string requestedVersion,
        string moduleKey = "systemdata")
    {
        var operation = DatabaseProvisionOperation.Enqueue(
            Tenant,
            operationNId,
            kind,
            "Development",
            "systemdata",
            planNId,
            requestedVersion,
            $"idem-{operationNId}",
            Sha($"req-{operationNId}"),
            DateTimeOffset.UtcNow.AddMinutes(30),
            "trace-001",
            Actor,
            moduleKey: moduleKey);
        await store.AddOperationAsync(operation, CancellationToken.None);
    }

    private static string RegistrationNIdAsTopologyRevision(DatabaseTopology topology) =>
        DatabaseTopologyFingerprint.ComputeTopologyRevision(topology);

    private static SeedSet CreateServiceSeed() => new(
        "service-seed",
        "1",
        SeedClass.SystemBaseline,
        SeedScope.System,
        "service-seed-artifact",
        Sha("service-seed"),
        "sig-ref",
        requiredForReadiness: true,
        allowedEnvironments: string.Empty,
        dependsOnMigrationVersion: "9.9.9",
        dependsOnSeedKeys: null,
        BootstrapPolicy.FailClosed);

    private static string Sha(string value) => DatabaseTopologyFingerprint.Sha256Hex(value);
}
