using IndustrialPlatform.SystemData.Application.DatabaseOrchestration;
using IndustrialPlatform.SystemData.Application.DatabaseOrchestration.Runner;
using IndustrialPlatform.SystemData.Domain.DatabaseOrchestration;
using IndustrialPlatform.SystemData.Testing;

namespace IndustrialPlatform.SystemData.Application.Tests;

/// <summary>
/// 蓝图 33 V2.0 §12 门禁验收:服务初始化握手全链(TASK-SD-004)。
/// 经 <see cref="InitializationGateHarness"/> 驱动真实 SQLite 目标适配器 + 签名产物存储 + Runner
/// RequiredSeed/SecretBootstrap 阶段。覆盖:受信服务登记与计划(门禁 1)、首次初始化全链(门禁 2)、
/// 幂等重复 apply + 多副本单执行(门禁 3)、消费者 NotReady(门禁 11)、SystemData 无循环自举(门禁 12)。
/// 待验收:PostgreSQL/Redis/RabbitMQ 真实验证项在 Infrastructure 侧标「待验收」。
/// </summary>
public sealed class ServiceInitializationAcceptanceTests
{
    private const string ServiceKey = TestInitializableService.ServiceKey;

    private static CancellationToken Ct => CancellationToken.None;

    [Fact]
    public async Task Gate01_TrustedServiceRegistersAndPlans()
    {
        using var harness = new InitializationGateHarness();
        var module = TestInitializableService.ModuleASpec;

        var registration = await harness.RegisterAsync(module, cancellationToken: Ct);
        Assert.Equal(TestInitializableService.ModuleA, registration.ModuleKey);
        Assert.Equal(ServiceKey, registration.ServiceKey);
        Assert.Equal(RegistrationStatus.Registered.ToString(), registration.Status);

        var plan = await harness.PlanAsync(module, Ct);
        Assert.NotNull(plan);
        Assert.Equal(TestInitializableService.ModuleA, plan.ModuleKey);
        Assert.Equal(module.RequestedVersion, plan.RequestedMigrationVersion);
        Assert.Single(harness.Store.Plans);
        Assert.Single(harness.Store.Operations);
        Assert.Equal(OperationKind.Plan, harness.Store.Operations[0].Kind);
        Assert.Equal(OperationStatus.Succeeded, harness.Store.Operations[0].Status);
    }

    [Fact]
    public async Task Gate02_FirstInitialization_FullChain()
    {
        using var harness = new InitializationGateHarness();
        var module = TestInitializableService.ModuleASpec;

        await harness.RegisterAsync(module, cancellationToken: Ct);
        var plan = await harness.PlanAsync(module, Ct);
        var operation = await harness.ApplyAsync(module, plan, Ct);

        Assert.Equal(OperationStatus.Succeeded, operation.Status);
        Assert.True(await harness.Scope.Database.TableExistsAsync(module.BusinessTable, Ct));
        Assert.True(await harness.Scope.Database.MigrationLedgerExistsAsync(module.ModuleKey, Ct));
        Assert.True(await harness.Scope.Database.SeedLedgerExistsAsync(module.ModuleKey, Ct));
        Assert.Equal("1.0.0", await harness.Scope.Database.LatestMigrationVersionAsync(module.ModuleKey, Ct));
        Assert.Equal(2, await harness.Scope.Database.TableRowCountAsync(module.BusinessTable, Ct));
        Assert.Equal(2, await harness.Scope.Database.SeedLedgerRowCountAsync(module.ModuleKey, Ct));

        // 每个 RequiredForReadiness 种子产生脱敏控制面观察。
        foreach (var seed in module.Seeds)
        {
            var observation = await harness.Store.GetLatestSeedObservationAsync(
                InitializationGateHarness.Tenant, "Development", ServiceKey, module.ModuleKey, seed.SeedKey, Ct);
            Assert.NotNull(observation);
            Assert.Equal(SeedStatus.Applied, observation!.Status);
        }

        var readiness = await harness.OperationService.GetReadinessV2Async(
            InitializationGateHarness.Tenant, ServiceKey, module.ModuleKey, "trace-gate", Ct);
        Assert.True(readiness.Ready);
        Assert.True(readiness.MigrationReady);
        Assert.True(readiness.RequiredSeedReady);
        Assert.True(readiness.BootstrapReady);
    }

    [Fact]
    public async Task Gate03_IdempotentReapply_And_ReplicaSingleExecution()
    {
        using var harness = new InitializationGateHarness();
        var module = TestInitializableService.ModuleASpec;

        await harness.RegisterAsync(module, cancellationToken: Ct);
        var firstPlan = await harness.PlanAsync(module, Ct);
        var first = await harness.ApplyAsync(module, firstPlan, Ct);
        Assert.Equal(OperationStatus.Succeeded, first.Status);
        Assert.Equal(2, await harness.Scope.Database.TableRowCountAsync(module.BusinessTable, Ct));
        Assert.Equal(2, await harness.Scope.Database.SeedLedgerRowCountAsync(module.ModuleKey, Ct));
        var appliedOnAfterFirst = (await harness.Scope.Database.ReadSeedLedgerAsync(
            module.ModuleKey, module.Seeds[0].SeedKey, module.Seeds[0].SeedVersion, Ct))!.AppliedOn;

        // 幂等:FRESH 计划重复 apply → 业务行数与账本行数不变,已应用时间戳不变(本地账本权威)。
        var freshPlan = await harness.PlanAsync(module, Ct);
        var second = await harness.ApplyAsync(module, freshPlan, Ct);
        Assert.Equal(OperationStatus.Succeeded, second.Status);
        Assert.Equal(2, await harness.Scope.Database.TableRowCountAsync(module.BusinessTable, Ct));
        Assert.Equal(2, await harness.Scope.Database.SeedLedgerRowCountAsync(module.ModuleKey, Ct));
        Assert.Equal(
            appliedOnAfterFirst,
            (await harness.Scope.Database.ReadSeedLedgerAsync(
                module.ModuleKey, module.Seeds[0].SeedKey, module.Seeds[0].SeedVersion, Ct))!.AppliedOn);

        // 同计划重复 apply → ExecuteInspect 目标版本已达成,SD_DB_TARGET_MISMATCH。
        var replay = await harness.ApplyAsync(module, firstPlan, Ct);
        Assert.Equal(OperationStatus.Failed, replay.Status);
        Assert.Equal(DatabaseOrchestrationRunnerErrors.TargetMismatch, replay.SanitizedErrorCode);

        // 多副本单执行:队列为空时副本 Runner 返回 0,无重复操作。
        var replica = harness.CreateReplica();
        Assert.Equal(0, await replica.RunOnceAsync(Ct));
        Assert.Equal(5, harness.Store.Operations.Count);
    }

    [Fact]
    public async Task Gate11_ConsumerNotReady_OnArtifactTamper()
    {
        using var harness = new InitializationGateHarness();
        var module = TestInitializableService.ModuleASpec;
        var bundle = harness.Scope.WriteModule(module);
        await harness.RegisterAsync(module, bundle, Ct);
        var plan = await harness.PlanAsync(module, Ct);

        // 篡改迁移产物文件(同 ArtifactId/Version,内容不同 → 校验和不同)→ Apply SchemaMigration 前校验失败。
        var tampered = TestArtifactWriter.BuildMigrationArtifactUpgrade(
            module, "tamper-gate11", "CREATE TABLE IF NOT EXISTS gate11_extra (id INTEGER);");
        TestArtifactWriter.WriteMigrationArtifact(harness.Scope.ArtifactRoot, tampered);
        var operation = await harness.ApplyAsync(module, plan, Ct);

        Assert.Equal(OperationStatus.Failed, operation.Status);
        Assert.Equal(DatabaseOrchestrationRunnerErrors.ArtifactInvalid, operation.SanitizedErrorCode);
        Assert.Equal(RegistrationStatus.NotReady, (await harness.GetRegistrationAsync(module.ModuleKey, Ct))!.Status);

        var readiness = await harness.OperationService.GetReadinessV2Async(
            InitializationGateHarness.Tenant, ServiceKey, module.ModuleKey, "trace-gate", Ct);
        Assert.False(readiness.Ready);
    }

    [Fact]
    public async Task Gate12_SystemDataNoCyclicBootstrap()
    {
        // SystemData 自身是唯一自举例外:基础设施最小引导创建库/角色后本地执行自身 Schema 迁移,
        // 不得调用自身 API。空环境(无任何注册/操作/计划)下 Runner 无事可做,直接返回 0。
        using var harness = new InitializationGateHarness();
        Assert.Empty(harness.Store.Operations);
        Assert.Equal(0, await harness.Runner.RunOnceAsync(Ct));
        var page = await harness.Store.QueryRegistrationsAsync(
            new RegistrationListFilter(InitializationGateHarness.Tenant, null, null, 1, 50), Ct);
        Assert.Equal(0, page.Total);
    }
}
