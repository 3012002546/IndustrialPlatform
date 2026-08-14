using IndustrialPlatform.SystemData.Application.DatabaseOrchestration;
using IndustrialPlatform.SystemData.Application.DatabaseOrchestration.Runner;
using IndustrialPlatform.SystemData.Contracts.DatabaseOrchestration;
using IndustrialPlatform.SystemData.Domain.DatabaseOrchestration;
using IndustrialPlatform.SystemData.Testing;

namespace IndustrialPlatform.SystemData.Application.Tests;

/// <summary>
/// 蓝图 33 V2.0 §12 门禁验收:环境门禁与 Secret 语义(TASK-SD-004)。
/// 覆盖:缺 Secret → bootstrap fail-closed + NotReady + Secret 不泄露(门禁 6)、
/// 生产审批/备份门禁(门禁 7)、EnvironmentSample 三层拒绝 + Runner 最终守卫(门禁 8)。
/// 生产门禁用例以 Production 环境构造(无存储策略 → Approval+Backup 回退),绕过入队门禁用 ApplyRawAsync。
/// </summary>
public sealed class EnvironmentGateAcceptanceTests
{
    private const string ServiceKey = TestInitializableService.ServiceKey;

    private static CancellationToken Ct => CancellationToken.None;

    /// <summary>SecretBootstrap 模块:admin 一次性敏感引导,RequiredForReadiness:false → 仅 SecretBootstrap 阶段。</summary>
    private static TestModuleSpec BootstrapModule { get; } = new(
        ModuleKey: "module-bootstrap",
        BusinessTable: "module_bootstrap_config",
        MigrationArtifactId: "testservice.module-bootstrap.schema",
        RequestedVersion: "1.0.0",
        Seeds:
        [
            new TestSeedSpec(
                SeedKey: "module-bootstrap-admin",
                SeedVersion: "1",
                SeedClass: SeedClass.SecretBootstrap,
                Scope: SeedScope.System,
                SeedArtifactId: "testservice.module-bootstrap.seed.admin",
                RequiredForReadiness: false,
                BootstrapPolicy: BootstrapPolicy.FailClosed),
        ]);

    /// <summary>EnvironmentSample 模块:仅 Development/Test 允许,Staging/Production 三层拒绝。</summary>
    private static TestModuleSpec EnvironmentSampleModule { get; } = new(
        ModuleKey: "module-envsample",
        BusinessTable: "module_envsample_data",
        MigrationArtifactId: "testservice.module-envsample.schema",
        RequestedVersion: "1.0.0",
        Seeds:
        [
            new TestSeedSpec(
                SeedKey: "module-envsample-data",
                SeedVersion: "1",
                SeedClass: SeedClass.EnvironmentSample,
                Scope: SeedScope.System,
                SeedArtifactId: "testservice.module-envsample.seed.data",
                RequiredForReadiness: true,
                BootstrapPolicy: BootstrapPolicy.FailClosed),
        ]);

    [Fact]
    public async Task Gate06a_MissingBootstrapSecret_FailsClosed_NotReady()
    {
        using var harness = new InitializationGateHarness();
        await harness.RegisterAsync(BootstrapModule, cancellationToken: Ct);
        var plan = await harness.PlanAsync(BootstrapModule, Ct);
        var operation = await harness.ApplyAsync(BootstrapModule, plan, Ct);

        Assert.Equal(OperationStatus.Failed, operation.Status);
        Assert.Equal(DatabaseOrchestrationRunnerErrors.BootstrapSecretMissing, operation.SanitizedErrorCode);
        Assert.Equal(RegistrationStatus.NotReady, (await harness.GetRegistrationAsync(BootstrapModule.ModuleKey, Ct))!.Status);

        // Secret 扫描:解析器确实被请求(secretRef = SeedKey);Secret 值未提供,走 fail-closed 稳定码。
        Assert.Contains("module-bootstrap-admin", harness.Scope.SecretProvider.ResolvedRefs);

        var readiness = await harness.OperationService.GetReadinessV2Async(
            InitializationGateHarness.Tenant, ServiceKey, BootstrapModule.ModuleKey, "trace-gate", Ct);
        Assert.False(readiness.Ready);
        Assert.False(readiness.BootstrapReady);
    }

    [Fact]
    public async Task Gate06b_BootstrapSecretSupplied_Succeeds_NoSecretLeak()
    {
        using var harness = new InitializationGateHarness();
        const string secretValue = "admin-bootstrap-secret-12345";
        harness.Scope.SecretProvider.SetSecret("module-bootstrap-admin", secretValue);
        await harness.RegisterAsync(BootstrapModule, cancellationToken: Ct);
        var plan = await harness.PlanAsync(BootstrapModule, Ct);
        var operation = await harness.ApplyAsync(BootstrapModule, plan, Ct);

        Assert.Equal(OperationStatus.Succeeded, operation.Status);
        var entry = await harness.Scope.Database.ReadSeedLedgerAsync(
            BootstrapModule.ModuleKey, "module-bootstrap-admin", "1", Ct);
        Assert.NotNull(entry);
        Assert.Equal(SeedStatus.Applied, entry!.Status);

        // Secret 值不透传:账本条目(序列化)与脱敏观察均不包含秘密值。
        Assert.DoesNotContain(secretValue, entry.ToString(), StringComparison.Ordinal);
        Assert.NotEqual(secretValue, entry.Checksum);
        var observation = await harness.Store.GetLatestSeedObservationAsync(
            InitializationGateHarness.Tenant, "Development", ServiceKey, BootstrapModule.ModuleKey, "module-bootstrap-admin", Ct);
        Assert.NotNull(observation);
        Assert.DoesNotContain(secretValue, observation!.ToString(), StringComparison.Ordinal);

        var readiness = await harness.OperationService.GetReadinessV2Async(
            InitializationGateHarness.Tenant, ServiceKey, BootstrapModule.ModuleKey, "trace-gate", Ct);
        Assert.True(readiness.Ready);
    }

    [Fact]
    public async Task Gate07_Production_ApprovalThenVerifiedBackup_ThenApplySucceeds()
    {
        using var harness = new InitializationGateHarness("Production");
        var module = TestInitializableService.ModuleASpec;
        await harness.RegisterAsync(module, cancellationToken: Ct);
        var plan = await harness.PlanAsync(module, Ct);

        // 无审批 → Runner 门禁拒绝(SD_DB_APPROVAL_REQUIRED);Validate 阶段失败不置 NotReady。
        var noApproval = await harness.ApplyRawAsync(plan, Ct);
        Assert.Equal(OperationStatus.Failed, noApproval.Status);
        Assert.Equal(DatabaseOrchestrationRunnerErrors.ApprovalRequired, noApproval.SanitizedErrorCode);
        Assert.Equal(RegistrationStatus.Registered, (await harness.GetRegistrationAsync(module.ModuleKey, Ct))!.Status);

        // 审批但无已校验备份 → SD_DB_BACKUP_REQUIRED。
        await harness.ApproveAsync(plan, Ct);
        var noBackup = await harness.ApplyRawAsync(plan, Ct);
        Assert.Equal(OperationStatus.Failed, noBackup.Status);
        Assert.Equal(DatabaseOrchestrationRunnerErrors.BackupRequired, noBackup.SanitizedErrorCode);

        // 审批 + 已校验备份 → 全链成功。
        await harness.VerifyBackupAsync(plan, Ct);
        var applied = await harness.ApplyRawAsync(plan, Ct);
        Assert.Equal(OperationStatus.Succeeded, applied.Status);
        Assert.Equal("1.0.0", await harness.Scope.Database.LatestMigrationVersionAsync(module.ModuleKey, Ct));
        Assert.Equal(2, await harness.Scope.Database.SeedLedgerRowCountAsync(module.ModuleKey, Ct));
    }

    [Fact]
    public async Task Gate08_EnvironmentSample_RejectedAtAllLayers()
    {
        // 第一层:注册层拒绝(EnsureSampleEnvironmentAllowed)。
        using (var harness = new InitializationGateHarness("Production"))
        {
            var bundle = harness.Scope.WriteModule(EnvironmentSampleModule);
            await Assert.ThrowsAsync<SampleEnvironmentForbiddenException>(() =>
                harness.RegistrationService.RegisterModuleAsync(
                    InitializationGateHarness.Tenant,
                    InitializationGateHarness.Actor,
                    TestFixtureScope.BuildManifest(EnvironmentSampleModule, bundle),
                    Ct));
        }

        using (var harness = new InitializationGateHarness("Production"))
        {
            var bundle = harness.Scope.WriteModule(EnvironmentSampleModule);
            // 绕过注册层直接落库 → 第二层:计划层拒绝。
            await harness.RegisterRawAsync(EnvironmentSampleModule, bundle, Ct);
            await Assert.ThrowsAsync<SampleEnvironmentForbiddenException>(() =>
                harness.PlanService.EnqueuePlanModuleAsync(
                    InitializationGateHarness.Tenant,
                    InitializationGateHarness.Actor,
                    $"idem-plan-{Guid.NewGuid():N}",
                    new ServiceInitializationPlanRequestV2
                    {
                        ServiceKey = ServiceKey,
                        ModuleKey = EnvironmentSampleModule.ModuleKey,
                        RequestedVersion = EnvironmentSampleModule.RequestedVersion,
                    },
                    "trace-gate",
                    Ct));

            // 第三层:apply 入队层拒绝。
            var plan = await harness.PlanRawAsync(EnvironmentSampleModule, Ct);
            await Assert.ThrowsAsync<SampleEnvironmentForbiddenException>(() =>
                harness.OperationService.EnqueueApplyModuleAsync(
                    InitializationGateHarness.Tenant,
                    InitializationGateHarness.Actor,
                    $"idem-apply-{Guid.NewGuid():N}",
                    new ServiceInitializationApplyRequestV2
                    {
                        PlanNId = plan.PlanNId,
                        ModuleKey = EnvironmentSampleModule.ModuleKey,
                        RequestedVersion = EnvironmentSampleModule.RequestedVersion,
                    },
                    "trace-gate",
                    Ct));

            // 第四层:Runner 最终守卫(绕过入队门禁 + 审批/备份齐备 → RequiredSeed 拒绝)。
            await harness.ApproveAsync(plan, Ct);
            await harness.VerifyBackupAsync(plan, Ct);
            var operation = await harness.ApplyRawAsync(plan, Ct);
            Assert.Equal(OperationStatus.Failed, operation.Status);
            Assert.Equal(DatabaseOrchestrationRunnerErrors.SampleEnvironmentForbidden, operation.SanitizedErrorCode);
        }
    }
}
