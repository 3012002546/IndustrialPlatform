using IndustrialPlatform.SystemData.Application.DatabaseOrchestration;
using IndustrialPlatform.SystemData.Application.DatabaseOrchestration.Runner;
using IndustrialPlatform.SystemData.Domain.DatabaseOrchestration;
using IndustrialPlatform.SystemData.Testing;

namespace IndustrialPlatform.SystemData.Application.Tests;

/// <summary>
/// 蓝图 33 V2.0 §12 门禁验收:种子账本边界与 DataPatch 保护(TASK-SD-004)。
/// 覆盖:版本升级追加 + checksum drift 拒绝(门禁 4)、部分失败账本边界重试(门禁 5)、
/// DataPatch 保护/注册层 drift guard(门禁 10)。账本断言均 sqlite_master/直查(规避 SqlSugar 陈旧缓存)。
/// </summary>
public sealed class SeedLedgerAcceptanceTests
{
    private static CancellationToken Ct => CancellationToken.None;

    /// <summary>module-a 升级规格:v2 迁移(追加列)+ system-directory 升级到 v2 + tenant-defaults 保持 v1。</summary>
    private static TestModuleSpec ModuleAV2 { get; } = new(
        ModuleKey: TestInitializableService.ModuleA,
        BusinessTable: "module_a_widget",
        MigrationArtifactId: "testservice.module-a.schema.v2",
        RequestedVersion: "2.0.0",
        Seeds:
        [
            new TestSeedSpec(
                SeedKey: "module-a-system-directory",
                SeedVersion: "2",
                SeedClass: SeedClass.SystemBaseline,
                Scope: SeedScope.System,
                SeedArtifactId: "testservice.module-a.seed.system-directory.v2",
                RequiredForReadiness: true,
                BootstrapPolicy: BootstrapPolicy.FailClosed),
            new TestSeedSpec(
                SeedKey: "module-a-tenant-defaults",
                SeedVersion: "1",
                SeedClass: SeedClass.TenantBaseline,
                Scope: SeedScope.Tenant,
                SeedArtifactId: "testservice.module-a.seed.tenant-defaults",
                RequiredForReadiness: true,
                BootstrapPolicy: BootstrapPolicy.FailClosed),
        ]);

    [Fact]
    public async Task Gate04a_VersionUpgradeAppendsLedger()
    {
        using var harness = new InitializationGateHarness();
        var v1 = TestInitializableService.ModuleASpec;
        await harness.RegisterAsync(v1, cancellationToken: Ct);
        var op1 = await harness.ApplyAsync(v1, await harness.PlanAsync(v1, Ct), Ct);
        Assert.Equal(OperationStatus.Succeeded, op1.Status);
        Assert.Equal(2, await harness.Scope.Database.SeedLedgerRowCountAsync(v1.ModuleKey, Ct));

        // v2 重新注册(迁移升级产物:基表步骤 + 追加列步骤,内容不同 → 校验和不同)。
        var written = harness.Scope.WriteModule(ModuleAV2);
        var upgrade = TestArtifactWriter.BuildMigrationArtifactUpgrade(
            ModuleAV2, "add-column-gate04", "ALTER TABLE module_a_widget ADD COLUMN gate04_extra TEXT;");
        TestArtifactWriter.WriteMigrationArtifact(harness.Scope.ArtifactRoot, upgrade);
        var v2Bundle = new TestArtifactBundle(upgrade, written.Seeds);
        await harness.ReRegisterAsync(ModuleAV2, v2Bundle, Ct);

        var op2 = await harness.ApplyAsync(ModuleAV2, await harness.PlanAsync(ModuleAV2, Ct), Ct);
        Assert.Equal(OperationStatus.Succeeded, op2.Status);
        Assert.Equal("2.0.0", await harness.Scope.Database.LatestMigrationVersionAsync(ModuleAV2.ModuleKey, Ct));
        var appliedSteps = await harness.Scope.Database.AppliedMigrationStepIdsAsync(ModuleAV2.ModuleKey, Ct);
        Assert.Contains("add-column-gate04", appliedSteps);
        // 业务数据不静默覆盖:同 code 的种子幂等忽略 → 仍 2 行;账本追加 system-directory v2 → 3 行。
        Assert.Equal(2, await harness.Scope.Database.TableRowCountAsync(ModuleAV2.BusinessTable, Ct));
        Assert.Equal(3, await harness.Scope.Database.SeedLedgerRowCountAsync(ModuleAV2.ModuleKey, Ct));
        Assert.NotNull(await harness.Scope.Database.ReadSeedLedgerAsync(
            ModuleAV2.ModuleKey, "module-a-system-directory", "2", Ct));
    }

    [Fact]
    public async Task Gate04b_ChecksumDriftRejectedAtApply()
    {
        using var harness = new InitializationGateHarness();
        var written = harness.Scope.WriteModule(ModuleAV2);
        var upgrade = TestArtifactWriter.BuildMigrationArtifactUpgrade(
            ModuleAV2, "add-column-gate04", "ALTER TABLE module_a_widget ADD COLUMN gate04_extra TEXT;");
        TestArtifactWriter.WriteMigrationArtifact(harness.Scope.ArtifactRoot, upgrade);
        var v2Bundle = new TestArtifactBundle(upgrade, written.Seeds);
        await harness.ReRegisterAsync(ModuleAV2, v2Bundle, Ct);
        var plan = await harness.PlanAsync(ModuleAV2, Ct);

        // 篡改种子产物文件(同 ArtifactId/Version,内容变化 → 校验和不同)→ RequiredSeed 校验拒绝。
        var drifted = TestArtifactWriter.BuildDriftedSeedArtifact(ModuleAV2, ModuleAV2.Seeds[0]);
        TestArtifactWriter.WriteSeedArtifact(harness.Scope.ArtifactRoot, drifted);
        var operation = await harness.ApplyAsync(ModuleAV2, plan, Ct);

        Assert.Equal(OperationStatus.Failed, operation.Status);
        Assert.Equal(DatabaseOrchestrationRunnerErrors.SeedChecksumDrift, operation.SanitizedErrorCode);
        Assert.Equal(RegistrationStatus.NotReady, (await harness.GetRegistrationAsync(ModuleAV2.ModuleKey, Ct))!.Status);
        // 账本边界:迁移已应用(2.0.0),种子账本未追加(drift 种子拒绝执行)。
        Assert.Equal("2.0.0", await harness.Scope.Database.LatestMigrationVersionAsync(ModuleAV2.ModuleKey, Ct));
        Assert.Equal(0, await harness.Scope.Database.SeedLedgerRowCountAsync(ModuleAV2.ModuleKey, Ct));
    }

    [Fact]
    public async Task Gate05_PartialFailureLedgerBoundaryRetry()
    {
        using var harness = new InitializationGateHarness();
        var module = new TestModuleSpec(
            ModuleKey: "module-c",
            BusinessTable: "module_c_data",
            MigrationArtifactId: "testservice.module-c.schema",
            RequestedVersion: "1.0.0",
            Seeds:
            [
                // Runner 按 SeedKey 字母序执行 RequiredSeed;命名使 valid 先于 failing,
                // 以验证"成功种子先记账→失败种子不记账→从账本边界重试不盲重"的部分失败语义。
                new TestSeedSpec("module-c-01-valid", "1", SeedClass.SystemBaseline, SeedScope.System, "testservice.module-c.seed.valid", true, BootstrapPolicy.FailClosed),
                new TestSeedSpec("module-c-02-failing", "1", SeedClass.SystemBaseline, SeedScope.System, "testservice.module-c.seed.failing", true, BootstrapPolicy.FailClosed),
            ]);
        var written = harness.Scope.WriteModule(module);
        var failing = TestArtifactWriter.BuildFailingSeedArtifact(module, module.Seeds[1]);
        TestArtifactWriter.WriteSeedArtifact(harness.Scope.ArtifactRoot, failing);
        var bundle = new TestArtifactBundle(written.Migration, [written.Seeds[0], failing]);
        await harness.RegisterAsync(module, bundle, Ct);

        var first = await harness.ApplyAsync(module, await harness.PlanAsync(module, Ct), Ct);
        Assert.Equal(OperationStatus.Failed, first.Status);
        Assert.Equal(DatabaseOrchestrationRunnerErrors.InternalFailure, first.SanitizedErrorCode);

        // 账本边界:valid 种子已记账,失败种子未记账(从可验证边界重试,不盲重)。
        var validEntry = await harness.Scope.Database.ReadSeedLedgerAsync(module.ModuleKey, "module-c-01-valid", "1", Ct);
        Assert.NotNull(validEntry);
        Assert.Equal(SeedStatus.Applied, validEntry!.Status);
        Assert.Null(await harness.Scope.Database.ReadSeedLedgerAsync(module.ModuleKey, "module-c-02-failing", "1", Ct));
        Assert.Equal(1, await harness.Scope.Database.SeedLedgerRowCountAsync(module.ModuleKey, Ct));
        Assert.Equal(RegistrationStatus.NotReady, (await harness.GetRegistrationAsync(module.ModuleKey, Ct))!.Status);

        // 重试:同一注册新计划 → 仍失败(failing 持续失败),valid 幂等跳过、账本保持 1 行。
        var retry = await harness.ApplyAsync(module, await harness.PlanAsync(module, Ct), Ct);
        Assert.Equal(OperationStatus.Failed, retry.Status);
        Assert.Equal(1, await harness.Scope.Database.SeedLedgerRowCountAsync(module.ModuleKey, Ct));
        Assert.Equal(SeedStatus.Applied, (await harness.Scope.Database.ReadSeedLedgerAsync(module.ModuleKey, "module-c-01-valid", "1", Ct))!.Status);
    }

    [Fact]
    public async Task Gate10_RegistrationLayerDriftGuard_DataPatchProtection()
    {
        using var harness = new InitializationGateHarness();
        var module = TestInitializableService.ModuleASpec;
        var bundle = harness.Scope.WriteModule(module);
        await harness.RegisterAsync(module, bundle, Ct);

        // 同 ArtifactId/Version 内容变化 → 重新注册拒绝(注册层 drift guard 抛 SeedChecksumDriftException)。
        var drifted = TestArtifactWriter.BuildDriftedSeedArtifact(module, module.Seeds[0]);
        TestArtifactWriter.WriteSeedArtifact(harness.Scope.ArtifactRoot, drifted);
        var driftedBundle = new TestArtifactBundle(bundle.Migration, [drifted, bundle.Seeds[1]]);
        await Assert.ThrowsAsync<SeedChecksumDriftException>(() => harness.ReRegisterAsync(module, driftedBundle, Ct));

        // DataPatch 保护:拒绝发生在写入前,原注册保持 Registered,种子集/校验和不变。
        var registration = await harness.GetRegistrationAsync(module.ModuleKey, Ct);
        Assert.NotNull(registration);
        Assert.Equal(RegistrationStatus.Registered, registration!.Status);
        Assert.Equal(bundle.Migration.Checksum, registration.ArtifactChecksum);
        Assert.Equal(2, registration.SeedSets.Count);
    }
}
