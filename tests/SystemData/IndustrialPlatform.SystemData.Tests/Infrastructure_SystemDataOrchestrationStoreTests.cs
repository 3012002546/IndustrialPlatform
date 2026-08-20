using IndustrialPlatform.Infrastructure.Database;
using IndustrialPlatform.SharedKernel.Exceptions;
using IndustrialPlatform.SystemData.Application.DatabaseOrchestration;
using IndustrialPlatform.SystemData.Domain.DatabaseOrchestration;
using IndustrialPlatform.SharedKernel.Topology;
using IndustrialPlatform.SystemData.Domain.Topology;
using IndustrialPlatform.SystemData.Infrastructure.DatabaseOrchestration;
using IndustrialPlatform.SystemData.Infrastructure.Persistence.Migrations;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SqlSugar;
using SQLitePCL;

namespace IndustrialPlatform.SystemData.Infrastructure.Tests;

/// <summary>
/// 数据库编排持久化集成测试(TASK-SD-002)。基于独立 SQLite 文件库(连接串 <c>Foreign Keys=True</c>
/// 开启复合外键)验证 SDM-001~003 九张控制面表、<see cref="DatabaseOrchestrationStore"/> 全聚合往返、
/// 双版本并发、软删除过滤与父子复合外键 ON UPDATE CASCADE 同步。
/// (PostgreSQL 真实验证标记「待验收」。)
/// </summary>
public sealed class SystemDataOrchestrationStoreTests : IDisposable
{
    static SystemDataOrchestrationStoreTests()
    {
        SQLitePCL.Batteries_V2.Init();
    }

    private const string Tenant = "tenant-001";

    private readonly string _dbPath;
    private readonly SqlSugarDbContext _dbContext;
    private readonly DatabaseOrchestrationStore _store;

    public SystemDataOrchestrationStoreTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"industrial-platform-systemdata-orch-test-{Guid.NewGuid():N}.db");
        _dbContext = new SqlSugarDbContext(Options.Create(new SqlSugarOptions
        {
            ConnectionString = $"Data Source={_dbPath};Foreign Keys=True",
            DbType = DbType.Sqlite,
        }));

        var runner = new SchemaMigrationRunner(_dbContext, SystemDataSchemaMigrations.All, NullLogger<SchemaMigrationRunner>.Instance);
        runner.ApplyPendingAsync().GetAwaiter().GetResult();
        _store = new DatabaseOrchestrationStore(_dbContext);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        try
        {
            if (File.Exists(_dbPath))
            {
                File.Delete(_dbPath);
            }
        }
        catch (IOException)
        {
            // SqlSugarScope 连接池可能短暂占用文件句柄,忽略清理失败。
        }
    }

    private static CancellationToken Ct => CancellationToken.None;

    private static string Sha(string value) => DatabaseTopologyFingerprint.Sha256Hex(value);

    private static async Task AssertTableExistsAsync(ISqlSugarClient sugar, string tableName)
    {
        var count = await sugar.Ado.GetIntAsync(
            $"SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='{tableName}'");
        Assert.True(count == 1, $"表 {tableName} 应存在,实际 sqlite_master 命中 {count} 条。");
    }

    private static async Task AssertIndexExistsAsync(ISqlSugarClient sugar, string indexName)
    {
        var count = await sugar.Ado.GetIntAsync(
            $"SELECT COUNT(*) FROM sqlite_master WHERE type='index' AND name='{indexName}'");
        Assert.True(count == 1, $"索引 {indexName} 应存在,实际 sqlite_master 命中 {count} 条。");
    }

    /// <summary>按业务标识读回数据库实际存储的主键(避免 Guid 参数与 SqlSugar 存储格式差异)。</summary>
    private static async Task<string> GetStoredIdAsync(ISqlSugarClient sugar, string tableName, string nidColumn, string nid)
    {
        var id = await sugar.Ado.GetStringAsync(
            $"SELECT id FROM {tableName} WHERE {nidColumn} = @nid",
            new SugarParameter("@nid", nid));
        Assert.False(string.IsNullOrWhiteSpace(id), $"表 {tableName} 未找到 {nidColumn}={nid} 的记录。");
        return id;
    }

    // =====================================================================
    // SDM-001~003+004 迁移:10 张控制面表(SDM-004-02 种子观察) + 部分唯一索引
    // =====================================================================

    [Fact]
    public async Task ApplyAllMigrations_CreatesAllFourteenTables()
    {
        foreach (var tableName in AllTableNames)
        {
            await AssertTableExistsAsync(_dbContext.SqlSugar, tableName);
        }

        // SDM-001~003(9 步)+ SDM-004-01/02(2 步)+ SDM-004-03/005-01/006-01(TASK-SD-005 三张表)
        Assert.Equal(14, await _dbContext.SqlSugar.Queryable<SchemaMigrationRecord>().CountAsync());
    }

    [Fact]
    public async Task ApplyAllMigrations_CreatesPartialUniqueIndexesForSoftDeleteReuse()
    {
        await AssertIndexExistsAsync(_dbContext.SqlSugar, "ux_env_policy_active");
        await AssertIndexExistsAsync(_dbContext.SqlSugar, "ux_registration_active");
        await AssertIndexExistsAsync(_dbContext.SqlSugar, "ux_plan_active_nid");
        await AssertIndexExistsAsync(_dbContext.SqlSugar, "ux_plan_active_checksum");
        await AssertIndexExistsAsync(_dbContext.SqlSugar, "ux_approval_active_nid");
        await AssertIndexExistsAsync(_dbContext.SqlSugar, "ux_backup_evidence_active_nid");
        await AssertIndexExistsAsync(_dbContext.SqlSugar, "ux_operation_active_nid");
        await AssertIndexExistsAsync(_dbContext.SqlSugar, "ux_operation_active_idempotency");
    }

    // =====================================================================
    // 全聚合往返
    // =====================================================================

    [Fact]
    public async Task Registration_RoundTrips()
    {
        var registration = CreateRegistration();

        await _store.AddRegistrationAsync(registration, Ct);
        var loaded = await _store.GetRegistrationAsync(Tenant, "Development", "systemdata", Ct);

        Assert.NotNull(loaded);
        Assert.Equal(registration.TenantNId, loaded.TenantNId);
        Assert.Equal(registration.ServiceKey, loaded.ServiceKey);
        Assert.Equal(registration.Provider, loaded.Provider);
        Assert.Equal(registration.PhysicalDatabaseName, loaded.PhysicalDatabaseName);
        Assert.Equal(registration.TopologyRevision, loaded.TopologyRevision);
        Assert.Equal(registration.MigrationVersion, loaded.MigrationVersion);
        Assert.Equal(registration.ArtifactChecksum, loaded.ArtifactChecksum);
        Assert.Equal(registration.DesiredState, loaded.DesiredState);
        Assert.Equal(registration.Status, loaded.Status);
    }

    [Fact]
    public async Task Registration_Query_FiltersByServiceKeyAndPages()
    {
        await _store.AddRegistrationAsync(CreateRegistration(serviceKey: "systemdata"), Ct);
        await _store.AddRegistrationAsync(CreateRegistration(serviceKey: "referencedata"), Ct);

        var page = await _store.QueryRegistrationsAsync(
            new RegistrationListFilter(Tenant, "system", ModuleKey: null, 1, 20), Ct);

        Assert.Equal(1, page.Total);
        Assert.Single(page.Items);
        Assert.Equal("systemdata", page.Items[0].ServiceKey);
    }

    [Fact]
    public async Task SeedObservation_RoundTrips_ByModuleScope()
    {
        var observation = DatabaseSeedObservation.Record(
            Tenant,
            "Development",
            "systemdata",
            "module-a",
            "seed-catalog",
            "1.0.0",
            Sha("seed-catalog"),
            SeedScope.System,
            SeedStatus.Applied,
            DateTimeOffset.UtcNow,
            "OP-001",
            VerificationStatus.Verified);

        await _store.AddSeedObservationAsync(observation, Ct);
        var loaded = await _store.GetLatestSeedObservationAsync(
            Tenant, "Development", "systemdata", "module-a", "seed-catalog", Ct);

        Assert.NotNull(loaded);
        Assert.Equal(observation.ModuleKey, loaded.ModuleKey);
        Assert.Equal(observation.SeedKey, loaded.SeedKey);
        Assert.Equal(observation.SeedVersion, loaded.SeedVersion);
        Assert.Equal(observation.Checksum, loaded.Checksum);
        Assert.Equal(observation.Scope, loaded.Scope);
        Assert.Equal(observation.Status, loaded.Status);
        Assert.Equal(observation.OperationNId, loaded.OperationNId);
        Assert.Equal(observation.VerificationStatus, loaded.VerificationStatus);

        // 同 (tenant, env, service, module, seed) 的后续观察按 CreatedOn 取最新;
        // CreatedOn 由 Entity 构造取墙钟,间隔 >0 保证排序确定(避免同一时钟节拍并列)。
        await Task.Delay(10, Ct);
        var newer = DatabaseSeedObservation.Record(
            Tenant,
            "Development",
            "systemdata",
            "module-a",
            "seed-catalog",
            "1.1.0",
            Sha("seed-catalog-v2"),
            SeedScope.System,
            SeedStatus.Applied,
            DateTimeOffset.UtcNow,
            "OP-002",
            VerificationStatus.Verified);
        await _store.AddSeedObservationAsync(newer, Ct);
        var latest = await _store.GetLatestSeedObservationAsync(
            Tenant, "Development", "systemdata", "module-a", "seed-catalog", Ct);

        Assert.NotNull(latest);
        Assert.Equal("1.1.0", latest.SeedVersion);
        Assert.Equal("OP-002", latest.OperationNId);
    }

    [Fact]
    public async Task EnvironmentPolicy_RoundTrips()
    {
        var policy = CreatePolicy();

        await _store.AddEnvironmentPolicyAsync(policy, Ct);
        var loaded = await _store.GetEnvironmentPolicyAsync(Tenant, "Production", Ct);

        Assert.NotNull(loaded);
        Assert.Equal(policy.EnvironmentNId, loaded.EnvironmentNId);
        Assert.Equal(policy.EnvironmentKind, loaded.EnvironmentKind);
        Assert.True(loaded.ApprovalRequired);
        Assert.True(loaded.BackupRequired);
        Assert.Equal(policy.PolicyRevision, loaded.PolicyRevision);
    }

    [Fact]
    public async Task Plan_WithSteps_RoundTrips()
    {
        var plan = CreatePlan();

        await _store.AddPlanAsync(plan, Ct);
        var loaded = await _store.GetPlanAsync(Tenant, plan.PlanNId, Ct);

        Assert.NotNull(loaded);
        Assert.Equal(plan.PlanNId, loaded.PlanNId);
        Assert.Equal(plan.ServiceKey, loaded.ServiceKey);
        Assert.Equal(plan.TargetStateFingerprint, loaded.TargetStateFingerprint);
        Assert.Equal(plan.PlanChecksum, loaded.PlanChecksum);
        Assert.Equal(plan.RiskLevel, loaded.RiskLevel);
        Assert.Equal(3, loaded.Steps.Count);
        Assert.Equal([1, 2, 3], loaded.Steps.Select(step => step.Sequence).ToArray());
        Assert.Equal("migrate", loaded.Steps.Last().StepKind);
    }

    [Fact]
    public async Task Operation_WithSteps_RoundTrips()
    {
        var operation = CreateOperation();

        await _store.AddOperationAsync(operation, Ct);
        var loaded = await _store.GetOperationAsync(Tenant, operation.OperationNId, Ct);

        Assert.NotNull(loaded);
        Assert.Equal(operation.OperationNId, loaded.OperationNId);
        Assert.Equal(operation.Kind, loaded.Kind);
        Assert.Equal(operation.Status, loaded.Status);
        Assert.Equal(operation.Phase, loaded.Phase);
        Assert.Equal(operation.IdempotencyKey, loaded.IdempotencyKey);
        Assert.Equal(operation.RequestHash, loaded.RequestHash);
        Assert.Equal(DatabaseProvisionOperation.AllPhases.Length, loaded.Steps.Count);
        Assert.Equal(OperationPhase.Validate, loaded.Steps.First().Phase);
    }

    [Fact]
    public async Task Operation_FindByIdempotencyKey_ReturnsMatching()
    {
        var operation = CreateOperation();
        await _store.AddOperationAsync(operation, Ct);

        var loaded = await _store.FindOperationByIdempotencyKeyAsync(Tenant, "idem-op-1", Ct);

        Assert.NotNull(loaded);
        Assert.Equal(operation.OperationNId, loaded.OperationNId);
        Assert.Equal(DatabaseProvisionOperation.AllPhases.Length, loaded.Steps.Count);
    }

    [Fact]
    public async Task Operation_Query_FiltersByKindAndStatus()
    {
        await _store.AddOperationAsync(CreateOperation(operationNId: "OP-001", kind: OperationKind.Apply, status: OperationStatus.Queued, idempotencyKey: "idem-op-1"), Ct);
        await _store.AddOperationAsync(CreateOperation(operationNId: "OP-002", kind: OperationKind.Plan, status: OperationStatus.Queued, idempotencyKey: "idem-op-2"), Ct);

        var page = await _store.QueryOperationsAsync(
            new OperationListFilter(Tenant, OperationKind.Apply, OperationStatus.Queued, 1, 20), Ct);

        Assert.Equal(1, page.Total);
        Assert.Equal(OperationKind.Apply, page.Items[0].Kind);
    }

    [Fact]
    public async Task Approval_RoundTrips()
    {
        var plan = CreatePlan();
        await _store.AddPlanAsync(plan, Ct);
        var approval = CreateApproval(plan);

        await _store.AddApprovalAsync(approval, Ct);

        var loaded = await _store.GetApprovalAsync(Tenant, approval.ApprovalNId, Ct);
        Assert.NotNull(loaded);
        Assert.Equal(approval.ApprovalNId, loaded.ApprovalNId);
        Assert.Equal(approval.PlanNId, loaded.PlanNId);
        Assert.Equal(approval.PlanChecksum, loaded.PlanChecksum);
        Assert.Equal(approval.TargetStateFingerprint, loaded.TargetStateFingerprint);
        Assert.Equal(approval.Status, loaded.Status);

        var forPlan = await _store.GetApprovalsForPlanAsync(Tenant, plan.PlanNId, Ct);
        Assert.Single(forPlan);
    }

    [Fact]
    public async Task BackupEvidence_RoundTrips_AndVerifyUpdatePersists()
    {
        var plan = CreatePlan();
        await _store.AddPlanAsync(plan, Ct);
        var evidence = CreateEvidence(plan);

        await _store.AddBackupEvidenceAsync(evidence, Ct);
        var loaded = await _store.GetBackupEvidenceAsync(Tenant, evidence.EvidenceNId, Ct);
        Assert.NotNull(loaded);
        Assert.Equal(BackupEvidenceStatus.Captured, loaded.Status);

        var expectedOptimistic = loaded.OptimisticVersion;
        var expectedConcurrency = loaded.ConcurrencyVersion;
        loaded.Verify("user-verifier", DateTimeOffset.UtcNow);
        await _store.UpdateBackupEvidenceAsync(loaded, expectedOptimistic, expectedConcurrency, Ct);

        var verified = await _store.GetBackupEvidenceAsync(Tenant, evidence.EvidenceNId, Ct);
        Assert.Equal(BackupEvidenceStatus.Verified, verified!.Status);
        Assert.Equal("user-verifier", verified.VerifiedByUserNId);

        Assert.Single(await _store.GetBackupEvidenceForPlanAsync(Tenant, plan.PlanNId, Ct));
    }

    [Fact]
    public async Task Observation_LatestObservationWins()
    {
        await _store.AddObservationAsync(CreateObservation(observedVersion: "0.9.0", observedOn: DateTimeOffset.UtcNow.AddMinutes(-10)), Ct);
        await _store.AddObservationAsync(CreateObservation(observedVersion: "1.0.0", observedOn: DateTimeOffset.UtcNow.AddMinutes(-1)), Ct);

        var latest = await _store.GetLatestObservationAsync(Tenant, "Development", "systemdata", Ct);

        Assert.NotNull(latest);
        Assert.Equal("1.0.0", latest.ObservedVersion);
    }

    // =====================================================================
    // 双版本并发
    // =====================================================================

    [Fact]
    public async Task UpdateRegistration_StaleVersion_ThrowsConcurrencyException()
    {
        var registration = CreateRegistration();
        await _store.AddRegistrationAsync(registration, Ct);
        var staleOptimistic = registration.OptimisticVersion;
        var staleConcurrency = registration.ConcurrencyVersion;

        registration.UpdateDesiredState(DesiredState.Operational);
        await _store.UpdateRegistrationAsync(registration, staleOptimistic, staleConcurrency, Ct);

        registration.UpdateDesiredState(DesiredState.Retiring);
        await Assert.ThrowsAsync<ConcurrencyException>(() =>
            _store.UpdateRegistrationAsync(registration, staleOptimistic, staleConcurrency, Ct));
    }

    [Fact]
    public async Task AddRegistration_DuplicateActiveKey_ThrowsConcurrencyException()
    {
        await _store.AddRegistrationAsync(CreateRegistration(), Ct);

        var duplicate = CreateRegistration();
        await Assert.ThrowsAsync<ConcurrencyException>(() => _store.AddRegistrationAsync(duplicate, Ct));
    }

    [Fact]
    public async Task AddOperation_DuplicateIdempotencyKey_ThrowsConcurrencyException()
    {
        await _store.AddOperationAsync(CreateOperation(operationNId: "OP-001"), Ct);

        var duplicate = CreateOperation(operationNId: "OP-002", idempotencyKey: "idem-op-1");
        await Assert.ThrowsAsync<ConcurrencyException>(() => _store.AddOperationAsync(duplicate, Ct));
    }

    [Fact]
    public async Task AddPlan_DuplicatePlanNId_ThrowsConcurrencyException()
    {
        await _store.AddPlanAsync(CreatePlan(), Ct);

        await Assert.ThrowsAsync<ConcurrencyException>(() => _store.AddPlanAsync(CreatePlan(), Ct));
    }

    // =====================================================================
    // 软删除过滤与父删除复合外键同步
    // =====================================================================

    [Fact]
    public async Task Registration_SoftDeleted_HiddenFromQueryAndGet()
    {
        var registration = CreateRegistration();
        await _store.AddRegistrationAsync(registration, Ct);
        var storedId = await GetStoredIdAsync(
            _dbContext.SqlSugar, "system_data_database_registration", "service_key", registration.ServiceKey);
        await _dbContext.SqlSugar.Ado.ExecuteCommandAsync(
            "UPDATE system_data_database_registration SET is_deleted = 1 WHERE id = @id",
            new SugarParameter("@id", storedId));

        Assert.Null(await _store.GetRegistrationAsync(Tenant, "Development", "systemdata", Ct));
        var page = await _store.QueryRegistrationsAsync(new RegistrationListFilter(Tenant, null, ModuleKey: null, 1, 20), Ct);
        Assert.Equal(0, page.Total);
    }

    [Fact]
    public async Task PlanParentSoftDelete_CascadesStepShadowColumn()
    {
        var plan = CreatePlan();
        await _store.AddPlanAsync(plan, Ct);
        var planId = await GetStoredIdAsync(
            _dbContext.SqlSugar, "system_data_database_plan", "plan_n_id", plan.PlanNId);

        // 父软删除 → 复合外键 (id, is_deleted) ON UPDATE CASCADE 同步子表 plan_is_deleted
        await _dbContext.SqlSugar.Ado.ExecuteCommandAsync(
            "UPDATE system_data_database_plan SET is_deleted = 1 WHERE id = @id",
            new SugarParameter("@id", planId));

        var childShadow = await _dbContext.SqlSugar.Ado.GetIntAsync(
            "SELECT plan_is_deleted FROM system_data_database_plan_step WHERE plan_id = @planId",
            new SugarParameter("@planId", planId));
        Assert.Equal(1, childShadow);

        Assert.Null(await _store.GetPlanAsync(Tenant, plan.PlanNId, Ct));
    }

    [Fact]
    public async Task OperationParentSoftDelete_CascadesStepShadowColumn()
    {
        var operation = CreateOperation();
        await _store.AddOperationAsync(operation, Ct);
        var operationId = await GetStoredIdAsync(
            _dbContext.SqlSugar, "system_data_database_operation", "operation_n_id", operation.OperationNId);

        await _dbContext.SqlSugar.Ado.ExecuteCommandAsync(
            "UPDATE system_data_database_operation SET is_deleted = 1 WHERE id = @id",
            new SugarParameter("@id", operationId));

        var childShadow = await _dbContext.SqlSugar.Ado.GetIntAsync(
            "SELECT operation_is_deleted FROM system_data_database_operation_step WHERE operation_id = @operationId",
            new SugarParameter("@operationId", operationId));
        Assert.Equal(1, childShadow);

        Assert.Null(await _store.GetOperationAsync(Tenant, operation.OperationNId, Ct));
    }

    // =====================================================================
    // 聚合构建助手
    // =====================================================================

    private static readonly string[] AllTableNames =
    [
        "system_data_database_environment_policy",
        "system_data_database_registration",
        "system_data_database_plan",
        "system_data_database_plan_step",
        "system_data_database_approval",
        "system_data_database_backup_evidence",
        "system_data_database_operation",
        "system_data_database_operation_step",
        "system_data_database_migration_observation",
        "system_data_seed_observation",
        "system_data_organization",
        "system_data_position",
        "system_data_user_assignment",
    ];

    private static DatabaseRegistration CreateRegistration(string serviceKey = "systemdata", string version = "1.0.0") =>
        DatabaseRegistration.Register(
            Tenant,
            "Development",
            serviceKey,
            "Sqlite",
            "systemdata_db",
            "industrial-platform.db",
            isSharedPhysicalDatabase: true,
            "Shared",
            Sha("topology-revision"),
            "artifact-1",
            version,
            Sha("artifact"),
            "sig-ref",
            "user-owner",
            DesiredState.SourceOfTruth,
            autoProvision: false,
            autoMigrate: false,
            "1",
            Sha("manifest"));

    private static DatabaseEnvironmentPolicy CreatePolicy() =>
        DatabaseEnvironmentPolicy.Create(
            Tenant,
            "Production",
            DatabaseEnvironmentKind.Production,
            approvalRequired: true,
            backupRequired: true,
            planTtlSeconds: 1800,
            planTimeoutSeconds: 120,
            applyTimeoutSeconds: 1800,
            maxPreMigrationRetries: 3);

    private static DatabaseProvisionPlan CreatePlan() =>
        DatabaseProvisionPlan.Create(
            Tenant,
            "PLAN-001",
            "Development",
            "systemdata",
            "1.0.0",
            "0.9.0",
            Sha("target-fingerprint"),
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

    private static DatabaseProvisionOperation CreateOperation(
        string operationNId = "OP-001",
        OperationKind kind = OperationKind.Apply,
        OperationStatus status = OperationStatus.Queued,
        string idempotencyKey = "idem-op-1") =>
        DatabaseProvisionOperation.Enqueue(
            Tenant,
            operationNId,
            kind,
            "Development",
            "systemdata",
            kind == OperationKind.Apply ? "PLAN-001" : null,
            "1.0.0",
            idempotencyKey,
            Sha($"request-{operationNId}"),
            DateTimeOffset.UtcNow.AddMinutes(30),
            "trace-001",
            "user-actor");

    private static DatabaseApproval CreateApproval(DatabaseProvisionPlan plan) =>
        DatabaseApproval.Create(
            Tenant,
            "APR-001",
            plan.PlanNId,
            plan.PlanChecksum,
            plan.TargetStateFingerprint,
            "user-approver",
            "approved",
            DateTimeOffset.UtcNow.AddMinutes(-5),
            DateTimeOffset.UtcNow.AddMinutes(30));

    private static DatabaseBackupEvidence CreateEvidence(DatabaseProvisionPlan plan) =>
        DatabaseBackupEvidence.Create(
            Tenant,
            "EVD-001",
            plan.PlanNId,
            plan.PlanChecksum,
            plan.TargetStateFingerprint,
            "aws-s3",
            "snapshot-001",
            DateTimeOffset.UtcNow.AddMinutes(-10),
            DateTimeOffset.UtcNow.AddDays(7));

    private static DatabaseMigrationObservation CreateObservation(
        string observedVersion = "1.0.0",
        DateTimeOffset? observedOn = null) =>
        DatabaseMigrationObservation.Record(
            Tenant,
            "Development",
            "systemdata",
            Sha("identity"),
            observedVersion,
            Sha("artifact"),
            observedOn ?? DateTimeOffset.UtcNow.AddMinutes(-1),
            "OP-001",
            VerificationStatus.Verified);
}
