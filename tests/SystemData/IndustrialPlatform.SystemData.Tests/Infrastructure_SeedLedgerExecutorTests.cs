using IndustrialPlatform.Application.Abstractions.Initialization;
using IndustrialPlatform.SystemData.Application.DatabaseOrchestration.Initialization;
using IndustrialPlatform.SystemData.Application.DatabaseOrchestration.Runner;
using IndustrialPlatform.SystemData.Domain.DatabaseOrchestration;
using IndustrialPlatform.SystemData.Infrastructure.DatabaseOrchestration.Runner;
using IndustrialPlatform.SystemData.Testing;
using Microsoft.Data.Sqlite;

namespace IndustrialPlatform.SystemData.Infrastructure.Tests;

/// <summary>
/// 种子账本执行语义测试(TASK-SD-004,蓝图 §5.2/§5.3):SQLite 本地替身下两类执行器的账本边界。
/// 同版本同校验和幂等、版本升级追加、同版本不同校验和 drift 拒绝、SQL 失败事务回滚不记账;
/// initializer 白名单 + Secret fail-closed + 不泄露。PostgreSQL 等价语义见
/// <see cref="SeedLedgerPostgreSqlE2ETests"/>([[Trait]] E2E 待验收)。
/// </summary>
public sealed class SeedLedgerExecutorTests
{
    private static CancellationToken Ct => CancellationToken.None;

    [Fact]
    public async Task SeedBundle_ReapplySameChecksum_IdempotentApplied()
    {
        using var scope = new TestFixtureScope();
        var module = TestInitializableService.ModuleASpec;
        await scope.Database.ApplyMigrationAsync(module, Ct);
        var seed = module.Seeds[0];

        var first = await scope.Database.ExecuteSeedAsync(module, seed, secretValue: null, "OP-1", "trace-1", Ct);
        Assert.True(first.Succeeded);
        Assert.Equal(SeedStatus.Applied, first.Status);
        var firstAppliedOn = first.AppliedOn;

        var again = await scope.Database.ExecuteSeedAsync(module, seed, secretValue: null, "OP-2", "trace-2", Ct);
        Assert.True(again.Succeeded);
        Assert.Equal(firstAppliedOn, again.AppliedOn);
        Assert.Equal(1, await scope.Database.SeedLedgerRowCountAsync(module.ModuleKey, Ct));
    }

    [Fact]
    public async Task SeedBundle_VersionUpgrade_AppendsLedgerRow()
    {
        using var scope = new TestFixtureScope();
        var module = TestInitializableService.ModuleASpec;
        await scope.Database.ApplyMigrationAsync(module, Ct);
        var seed = module.Seeds[0];
        await scope.Database.ExecuteSeedAsync(module, seed, secretValue: null, "OP-1", "trace-1", Ct);

        // 同键不同版本 → 追加新账本行(内容不变校验和相同,版本升级仍追加,不原位覆盖)。
        var v2Seed = new TestSeedSpec(
            seed.SeedKey, "2", seed.SeedClass, seed.Scope, $"{seed.SeedArtifactId}.v2", true, BootstrapPolicy.FailClosed);
        var artifact = TestArtifactWriter.BuildSeedArtifact(module, v2Seed);
        var request = scope.Database.BuildSeedRequest(module, v2Seed, artifact, secretValue: null, "OP-2", "trace-2");
        var result = await new SqlSeedBundleExecutor().ExecuteAsync(request, Ct);

        Assert.True(result.Succeeded);
        Assert.Equal(SeedStatus.Applied, result.Status);
        Assert.Equal(2, await scope.Database.SeedLedgerRowCountAsync(module.ModuleKey, Ct));
        Assert.NotNull(await scope.Database.ReadSeedLedgerAsync(module.ModuleKey, seed.SeedKey, "2", Ct));
    }

    [Fact]
    public async Task SeedBundle_SameVersionDifferentChecksum_DriftRejected()
    {
        using var scope = new TestFixtureScope();
        var module = TestInitializableService.ModuleASpec;
        await scope.Database.ApplyMigrationAsync(module, Ct);
        var seed = module.Seeds[0];
        await scope.Database.ExecuteSeedAsync(module, seed, secretValue: null, "OP-1", "trace-1", Ct);

        // 同 ArtifactId/Version 内容变化 → 校验和漂移拒绝,账本不追加。
        var drifted = TestArtifactWriter.BuildDriftedSeedArtifact(module, seed);
        var request = scope.Database.BuildSeedRequest(module, seed, drifted, secretValue: null, "OP-2", "trace-2");
        var result = await new SqlSeedBundleExecutor().ExecuteAsync(request, Ct);

        Assert.False(result.Succeeded);
        Assert.Equal(DatabaseOrchestrationRunnerErrors.SeedChecksumDrift, result.ErrorCode);
        Assert.Equal(1, await scope.Database.SeedLedgerRowCountAsync(module.ModuleKey, Ct));
    }

    [Fact]
    public async Task SeedBundle_SqlFailure_RollsBack_NoLedgerEntry()
    {
        using var scope = new TestFixtureScope();
        var module = new TestModuleSpec(
            ModuleKey: "module-seed-fail",
            BusinessTable: "module_seed_fail_data",
            MigrationArtifactId: "testservice.module-seed-fail.schema",
            RequestedVersion: "1.0.0",
            Seeds:
            [
                new TestSeedSpec("module-seed-fail-k", "1", SeedClass.SystemBaseline, SeedScope.System, "testservice.module-seed-fail.seed.k", true, BootstrapPolicy.FailClosed),
            ]);
        var failing = TestArtifactWriter.BuildFailingSeedArtifact(module, module.Seeds[0]);
        var request = scope.Database.BuildSeedRequest(module, module.Seeds[0], failing, secretValue: null, "OP-1", "trace-1");

        // 裸 SqliteException 传播(目标表不存在),事务整体回滚,不记账。
        await Assert.ThrowsAnyAsync<SqliteException>(() => new SqlSeedBundleExecutor().ExecuteAsync(request, Ct));
        Assert.Equal(0, await scope.Database.SeedLedgerRowCountAsync(module.ModuleKey, Ct));
    }

    [Fact]
    public async Task Initializer_MissingSecret_FailsClosed()
    {
        using var scope = new TestFixtureScope();
        var module = TestInitializableService.ModuleBSpec;
        var seed = module.Seeds[0];

        var result = await scope.Database.ExecuteSeedAsync(module, seed, secretValue: null, "OP-1", "trace-1", Ct);

        Assert.False(result.Succeeded);
        Assert.Equal(DatabaseOrchestrationRunnerErrors.BootstrapSecretMissing, result.ErrorCode);
        Assert.Equal(0, await scope.Database.SeedLedgerRowCountAsync(module.ModuleKey, Ct));
    }

    [Fact]
    public async Task Initializer_WithSecret_AppliesAndLedgers_NoSecretLeak()
    {
        using var scope = new TestFixtureScope();
        var module = TestInitializableService.ModuleBSpec;
        var seed = module.Seeds[0];
        const string secretValue = "admin-bootstrap-secret-12345";

        var result = await scope.Database.ExecuteSeedAsync(module, seed, secretValue, "OP-1", "trace-1", Ct);

        Assert.True(result.Succeeded);
        Assert.Equal(SeedStatus.Applied, result.Status);
        var entry = await scope.Database.ReadSeedLedgerAsync(module.ModuleKey, seed.SeedKey, seed.SeedVersion, Ct);
        Assert.NotNull(entry);
        Assert.DoesNotContain(secretValue, entry!.ToString(), StringComparison.Ordinal);
        Assert.NotEqual(secretValue, entry.Checksum);
        Assert.Equal(1, await scope.Database.SeedLedgerRowCountAsync(module.ModuleKey, Ct));
    }

    [Fact]
    public async Task Initializer_NonAllowlistedStep_Rejected()
    {
        using var scope = new TestFixtureScope();
        var module = TestInitializableService.ModuleBSpec;
        var seed = module.Seeds[0];
        var badArtifact = new SeedArtifact(
            seed.SeedArtifactId,
            seed.SeedVersion,
            Checksum: string.Empty,
            TestInitializableService.SignatureRef,
            SeedExecutorKind.ServiceInitializer,
            Steps: [new SeedArtifactStep(1, "arbitrary-script", Sql: string.Empty, RollbackSql: null)]);
        badArtifact = badArtifact with { Checksum = MigrationArtifactChecksumVerifier.ComputeChecksum(badArtifact) };
        var request = scope.Database.BuildSeedRequest(module, seed, badArtifact, "any-secret", "OP-1", "trace-1");

        var result = await new ServiceInitializerExecutor().ExecuteAsync(request, Ct);

        Assert.False(result.Succeeded);
        Assert.Equal(DatabaseOrchestrationRunnerErrors.SeedFailed, result.ErrorCode);
        Assert.Equal(0, await scope.Database.SeedLedgerRowCountAsync(module.ModuleKey, Ct));
    }

    [Fact]
    public async Task Initializer_forwards_real_tenant_to_service_invoker()
    {
        using var scope = new TestFixtureScope();
        var module = TestInitializableService.ModuleBSpec;
        var seed = module.Seeds[0];
        var artifact = TestArtifactWriter.BuildSeedArtifact(module, seed);
        var request = scope.Database.BuildSeedRequest(
            module,
            seed,
            artifact,
            secretValue: "in-memory-secret",
            operationNId: "OP-tenant",
            traceId: "trace-tenant",
            tenantNId: "tenant-real");
        var invoker = new CapturingInitializationInvoker();

        var result = await new ServiceInitializerExecutor(invoker).ExecuteAsync(request, Ct);

        Assert.True(result.Succeeded);
        Assert.NotNull(invoker.Context);
        Assert.Equal("tenant-real", invoker.Context!.TenantNId);
        Assert.Equal("Development", invoker.Context.EnvironmentName);
        Assert.NotEqual(invoker.Context.EnvironmentName, invoker.Context.TenantNId);
    }

    [Fact]
    public async Task Initializer_production_path_uses_advanced_policy_without_target_ledger()
    {
        using var scope = new TestFixtureScope();
        var module = TestInitializableService.ModuleBSpec;
        var seed = module.Seeds[0];
        var artifact = TestArtifactWriter.BuildSeedArtifact(module, seed);
        var request = scope.Database.BuildSeedRequest(
            module,
            seed,
            artifact,
            secretValue: "must-not-cross-port",
            operationNId: "OP-production",
            traceId: "trace-production") with
        {
            EnvironmentNId = "Production",
        };
        var invoker = new CapturingInitializationInvoker();

        var result = await new ServiceInitializerExecutor(invoker).ExecuteAsync(request, Ct);

        Assert.True(result.Succeeded);
        Assert.NotNull(invoker.Context);
        Assert.Equal(ServiceInitializationPolicy.Advanced, invoker.Context!.Policy);
        Assert.Equal(0, await scope.Database.SeedLedgerRowCountAsync(module.ModuleKey, Ct));
    }

    private sealed class CapturingInitializationInvoker : IServiceInitializationInvoker
    {
        public ServiceInitializationContext? Context { get; private set; }

        public Task<ServiceInitializationState> InspectAsync(ServiceInitializationContext context, CancellationToken cancellationToken)
        {
            Context = context;
            return Task.FromResult(new ServiceInitializationState(
                context.ServiceKey,
                context.ModuleKey,
                null,
                false,
                false,
                false,
                false,
                "not initialized"));
        }

        public Task<ServiceInitializationPlan> PlanAsync(
            ServiceInitializationContext context,
            ServiceInitializationState inspection,
            CancellationToken cancellationToken) =>
            Task.FromResult(new ServiceInitializationPlan(
                context.ServiceKey,
                context.ModuleKey,
                inspection.ObservedVersion,
                context.DesiredVersion,
                true,
                ["initialize"]));

        public Task<ServiceInitializationState> ApplyAsync(
            ServiceInitializationContext context,
            ServiceInitializationPlan plan,
            CancellationToken cancellationToken) =>
            Task.FromResult(new ServiceInitializationState(
                context.ServiceKey,
                context.ModuleKey,
                context.DesiredVersion,
                true,
                true,
                true,
                true,
                null));

        public Task<ServiceInitializationState> VerifyAsync(ServiceInitializationContext context, CancellationToken cancellationToken) =>
            Task.FromResult(new ServiceInitializationState(
                context.ServiceKey,
                context.ModuleKey,
                context.DesiredVersion,
                true,
                true,
                true,
                true,
                null));
    }
}
