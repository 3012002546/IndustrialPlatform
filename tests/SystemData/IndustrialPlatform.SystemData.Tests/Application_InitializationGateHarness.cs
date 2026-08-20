using IndustrialPlatform.SystemData.Application.DatabaseOrchestration;
using IndustrialPlatform.SystemData.Application.DatabaseOrchestration.Options;
using IndustrialPlatform.SystemData.Application.DatabaseOrchestration.Runner;
using IndustrialPlatform.SystemData.Contracts.DatabaseOrchestration;
using IndustrialPlatform.SystemData.Domain.DatabaseOrchestration;
using IndustrialPlatform.SharedKernel.Topology;
using IndustrialPlatform.SystemData.Domain.Topology;
using IndustrialPlatform.SystemData.Testing;
using Microsoft.Extensions.Options;

namespace IndustrialPlatform.SystemData.Application.Tests;

/// <summary>
/// 蓝图 §12 13 项门禁验收夹具(TASK-SD-004)。真实目标适配器(SQLite)/产物存储/校验/两类执行器 +
/// 假 Secret Provider 经 Runner 全链驱动,控制面为进程内假 store:
/// 注册(服务层)→计划(服务层入队 + Runner 生成)→Apply(Runner 全链:SchemaMigration→RequiredSeed→SecretBootstrap→Verify)。
/// 每个门禁测试独立 <see cref="TestFixtureScope"/>(临时产物根 + 临时 SQLite 库),PerService 拓扑映射
/// testservice→临时库文件;生产门禁用例以 Production 环境构造(无存储策略→Approval+Backup 回退)。
/// 绕过控制面门禁的原始路径(RegisterRaw/PlanRaw/ApplyRaw)供多副本单执行、生产门禁与 Runner 最终守卫验证。
/// </summary>
public sealed class InitializationGateHarness : IDisposable
{
    public const string Tenant = "tenant-gate";
    public const string Actor = "user-actor";

    /// <summary>初始化组合根;environment 决定环境策略与门禁(Development 无门禁/Production 审批+备份)。</summary>
    public InitializationGateHarness(string environment = "Development")
    {
        EnvironmentName = environment;
        Scope = new TestFixtureScope();
        Store = new FakeDatabaseOrchestrationStore();
        Topology = new DatabaseTopology(
            environment,
            DatabaseTopologyMode.PerService,
            SharedDatabaseName: null,
            SharedSqliteFile: null,
            ServiceDatabases: new Dictionary<string, string>
            {
                [TestInitializableService.ServiceKey] = Scope.Database.FilePath,
            });
        _topologyProvider = new FakeTopologyProvider(Topology);

        var options = Options.Create(new DatabaseOrchestrationOptions());
        var runnerOptions = Options.Create(new DatabaseOperationRunnerOptions
        {
            InstanceId = "runner-gate",
            AdvisoryLockTimeoutSeconds = 2,
        });
        ApprovalService = new DatabaseApprovalService(Store);
        BackupService = new DatabaseBackupService(Store, options);
        RegistrationService = new DatabaseRegistrationService(Store, _topologyProvider);
        PlanService = new DatabasePlanService(Store, _topologyProvider, options);
        OperationService = new DatabaseOperationService(Store, _topologyProvider, ApprovalService, BackupService, options);
        Runner = BuildRunner();
    }

    public string EnvironmentName { get; }

    public TestFixtureScope Scope { get; }

    public FakeDatabaseOrchestrationStore Store { get; }

    public DatabaseTopology Topology { get; }

    public DatabaseOperationRunner Runner { get; }

    public DatabaseRegistrationService RegistrationService { get; }

    public DatabasePlanService PlanService { get; }

    public DatabaseOperationService OperationService { get; }

    public DatabaseApprovalService ApprovalService { get; }

    public DatabaseBackupService BackupService { get; }

    private readonly IDatabaseTopologyProvider _topologyProvider;

    /// <summary>同 scope/store 的副本 Runner(多副本单执行:共享队列与物理库,只执行一次)。</summary>
    public DatabaseOperationRunner CreateReplica() => BuildRunner();

    // ===== 控制面路径(服务层门禁生效) =====

    /// <summary>注册模块(服务层;写入全部产物)。</summary>
    public Task<DatabaseRegistrationV1> RegisterAsync(
        TestModuleSpec module, TestArtifactBundle? bundle = null, CancellationToken cancellationToken = default)
    {
        bundle ??= Scope.WriteModule(module);
        return RegistrationService.RegisterModuleAsync(Tenant, Actor, TestFixtureScope.BuildManifest(module, bundle), cancellationToken);
    }

    /// <summary>重新注册同一模块(版本升级/清单更新路径)。</summary>
    public Task<DatabaseRegistrationV1> ReRegisterAsync(
        TestModuleSpec module, TestArtifactBundle bundle, CancellationToken cancellationToken = default) =>
        RegistrationService.RegisterModuleAsync(Tenant, Actor, TestFixtureScope.BuildManifest(module, bundle), cancellationToken);

    /// <summary>入队计划并运行 Runner,返回新生成计划。</summary>
    public async Task<DatabaseProvisionPlan> PlanAsync(TestModuleSpec module, CancellationToken cancellationToken = default)
    {
        var existing = Store.Plans.Select(plan => plan.PlanNId).ToHashSet(StringComparer.Ordinal);
        await PlanService.EnqueuePlanModuleAsync(
            Tenant,
            Actor,
            $"idem-plan-{Guid.NewGuid():N}",
            new ServiceInitializationPlanRequestV2
            {
                ServiceKey = TestInitializableService.ServiceKey,
                ModuleKey = module.ModuleKey,
                RequestedVersion = module.RequestedVersion,
            },
            "trace-gate-plan",
            cancellationToken);
        Assert.Equal(1, await Runner.RunOnceAsync(cancellationToken));
        return Store.Plans.Single(plan => !existing.Contains(plan.PlanNId));
    }

    /// <summary>入队 Apply 并运行 Runner,返回该操作终态(服务层门禁生效)。</summary>
    public async Task<DatabaseProvisionOperation> ApplyAsync(
        TestModuleSpec module, DatabaseProvisionPlan plan, CancellationToken cancellationToken = default)
    {
        var enqueue = await OperationService.EnqueueApplyModuleAsync(
            Tenant,
            Actor,
            $"idem-apply-{Guid.NewGuid():N}",
            new ServiceInitializationApplyRequestV2
            {
                PlanNId = plan.PlanNId,
                ModuleKey = module.ModuleKey,
                RequestedVersion = module.RequestedVersion,
            },
            "trace-gate-apply",
            cancellationToken);
        Assert.Equal(1, await Runner.RunOnceAsync(cancellationToken));
        return (await Store.GetOperationAsync(Tenant, enqueue.OperationNId, cancellationToken))!;
    }

    // ===== 原始路径(绕过控制面门禁,供 Runner 最终守卫/多副本/生产门禁验证) =====

    /// <summary>直接落库注册(绕过注册层环境门禁;含 SeedSets 与 ModuleKey)。</summary>
    public async Task<DatabaseRegistration> RegisterRawAsync(
        TestModuleSpec module, TestArtifactBundle bundle, CancellationToken cancellationToken = default)
    {
        var registration = DatabaseRegistration.Register(
            Tenant,
            EnvironmentName,
            TestInitializableService.ServiceKey,
            "Sqlite",
            TestInitializableService.LogicalDatabaseName,
            Scope.Database.FilePath,
            isSharedPhysicalDatabase: false,
            DatabaseTopologyMode.PerService.ToString(),
            DatabaseTopologyFingerprint.ComputeTopologyRevision(Topology),
            module.MigrationArtifactId,
            module.RequestedVersion,
            bundle.Migration.Checksum,
            TestInitializableService.SignatureRef,
            Actor,
            DesiredState.SourceOfTruth,
            autoProvision: true,
            autoMigrate: true,
            "1",
            DatabaseTopologyFingerprint.Sha256Hex("manifest"),
            TestSeedSets.Build(module, bundle),
            module.ModuleKey);
        await Store.AddRegistrationAsync(registration, cancellationToken);
        return registration;
    }

    /// <summary>直接入队计划操作并运行 Runner,返回新生成计划(绕过计划层环境门禁)。</summary>
    public async Task<DatabaseProvisionPlan> PlanRawAsync(TestModuleSpec module, CancellationToken cancellationToken = default)
    {
        var existing = Store.Plans.Select(plan => plan.PlanNId).ToHashSet(StringComparer.Ordinal);
        var operation = DatabaseProvisionOperation.Enqueue(
            Tenant,
            $"OP-{Guid.NewGuid():N}",
            OperationKind.Plan,
            EnvironmentName,
            TestInitializableService.ServiceKey,
            planNId: null,
            module.RequestedVersion,
            $"idem-plan-{Guid.NewGuid():N}",
            DatabaseTopologyFingerprint.Sha256Hex($"plan-{Guid.NewGuid():N}"),
            DateTimeOffset.UtcNow.AddMinutes(30),
            "trace-gate-plan-raw",
            Actor,
            moduleKey: module.ModuleKey);
        await Store.AddOperationAsync(operation, cancellationToken);
        Assert.Equal(1, await Runner.RunOnceAsync(cancellationToken));
        return Store.Plans.Single(plan => !existing.Contains(plan.PlanNId));
    }

    /// <summary>直接入队 Apply 操作并运行 Runner,返回该操作终态(绕过入队层审批/备份/环境门禁)。</summary>
    public async Task<DatabaseProvisionOperation> ApplyRawAsync(
        DatabaseProvisionPlan plan, CancellationToken cancellationToken = default)
    {
        var operation = DatabaseProvisionOperation.Enqueue(
            Tenant,
            $"OP-{Guid.NewGuid():N}",
            OperationKind.Apply,
            plan.EnvironmentNId,
            plan.ServiceKey,
            plan.PlanNId,
            plan.RequestedMigrationVersion,
            $"idem-apply-{Guid.NewGuid():N}",
            DatabaseTopologyFingerprint.Sha256Hex($"apply-{Guid.NewGuid():N}"),
            DateTimeOffset.UtcNow.AddMinutes(30),
            "trace-gate-apply-raw",
            Actor,
            moduleKey: plan.ModuleKey);
        await Store.AddOperationAsync(operation, cancellationToken);
        Assert.Equal(1, await Runner.RunOnceAsync(cancellationToken));
        return (await Store.GetOperationAsync(Tenant, operation.OperationNId, cancellationToken))!;
    }

    // ===== 生产门禁 =====

    /// <summary>批准计划(审批绑定 PlanChecksum/TargetStateFingerprint)。</summary>
    public Task<DatabaseApprovalV1> ApproveAsync(DatabaseProvisionPlan plan, CancellationToken cancellationToken = default) =>
        ApprovalService.CreateAsync(Tenant, Actor, plan.PlanNId, new DatabaseApprovalRequestV1 { Reason = "门禁验收" }, cancellationToken);

    /// <summary>登记并校验备份证据(证据绑定 PlanChecksum/TargetStateFingerprint)。</summary>
    public async Task<DatabaseBackupEvidenceV1> VerifyBackupAsync(DatabaseProvisionPlan plan, CancellationToken cancellationToken = default)
    {
        var evidence = await BackupService.CreateAsync(
            Tenant,
            Actor,
            plan.PlanNId,
            new DatabaseBackupEvidenceRequestV1
            {
                BackupProvider = "fixture",
                BackupReference = $"snapshot-{Guid.NewGuid():N}",
            },
            cancellationToken);
        return await BackupService.VerifyAsync(Tenant, Actor, evidence.EvidenceNId, cancellationToken);
    }

    /// <summary>读取模块注册终态(领域聚合)。</summary>
    public Task<DatabaseRegistration?> GetRegistrationAsync(string moduleKey, CancellationToken cancellationToken = default) =>
        Store.GetRegistrationAsync(Tenant, EnvironmentName, TestInitializableService.ServiceKey, moduleKey, cancellationToken);

    /// <inheritdoc />
    public void Dispose() => Scope.Dispose();

    private DatabaseOperationRunner BuildRunner()
    {
        var options = Options.Create(new DatabaseOrchestrationOptions());
        var runnerOptions = Options.Create(new DatabaseOperationRunnerOptions
        {
            InstanceId = "runner-gate",
            AdvisoryLockTimeoutSeconds = 2,
        });
        var adapter = Scope.Database.Adapter;
        var credentialPort = new FixtureCredentialPort(Scope.Database.Connection);
        return new DatabaseOperationRunner(
            Store,
            _topologyProvider,
            ApprovalService,
            BackupService,
            options,
            runnerOptions,
            adapter,
            adapter,
            Scope.ArtifactStore,
            Scope.Verifier,
            adapter,
            credentialPort,
            credentialPort,
            adapter,
            Scope.ArtifactStore,
            Scope.Verifier,
            [Scope.SqlBundleExecutor, Scope.InitializerExecutor],
            Scope.SecretProvider);
    }

    /// <summary>返回本地替身连接的三角色凭据解析/写入假端口(SQLite 单连接)。</summary>
    private sealed class FixtureCredentialPort : IDatabaseCredentialResolver, IDatabaseCredentialSink
    {
        private readonly TargetDatabaseConnection _connection;

        public FixtureCredentialPort(TargetDatabaseConnection connection) => _connection = connection;

        public Task<DatabaseTargetCredentials> ResolveAsync(
            ResolvedDatabaseTarget target, bool provisionRequired, CancellationToken cancellationToken) =>
            Task.FromResult(new DatabaseTargetCredentials(_connection, _connection, _connection));

        public Task WriteAsync(ResolvedDatabaseTarget target, ProvisionedRoles roles, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }
}
