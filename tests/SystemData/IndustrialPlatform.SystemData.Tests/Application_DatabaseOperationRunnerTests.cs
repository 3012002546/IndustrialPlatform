using IndustrialPlatform.SystemData.Application.DatabaseOrchestration;
using IndustrialPlatform.SystemData.Application.DatabaseOrchestration.Options;
using IndustrialPlatform.SystemData.Application.DatabaseOrchestration.Runner;
using IndustrialPlatform.SystemData.Contracts.DatabaseOrchestration;
using IndustrialPlatform.SystemData.Domain.DatabaseOrchestration;
using IndustrialPlatform.SharedKernel.Topology;
using IndustrialPlatform.SystemData.Domain.Topology;
using Microsoft.Extensions.Options;
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
        FakeAdvisoryLock? lockPort = null)
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
            new FakeSeedSecretResolver());
    }

    private static DatabaseRegistrationManifestV1 CreateManifest(string version = "9.9.9") => new()
    {
        ServiceKey = "systemdata",
        LogicalDatabaseName = "systemdata_db",
        RequestedVersion = version,
        ArtifactChecksum = Sha("artifact"),
        ArtifactSignature = "sig-ref",
    };

    private static async Task<DatabaseRegistration> SeedRegistrationAsync(
        FakeDatabaseOrchestrationStore store,
        DatabaseTopology topology,
        string version = "9.9.9")
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
            Sha("manifest"));
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
        string requestedVersion)
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
            Actor);
        await store.AddOperationAsync(operation, CancellationToken.None);
    }

    private static string RegistrationNIdAsTopologyRevision(DatabaseTopology topology) =>
        DatabaseTopologyFingerprint.ComputeTopologyRevision(topology);

    private static string Sha(string value) => DatabaseTopologyFingerprint.Sha256Hex(value);
}
