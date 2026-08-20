using IndustrialPlatform.SystemData.Application.DatabaseOrchestration;
using IndustrialPlatform.SystemData.Application.DatabaseOrchestration.Runner;
using IndustrialPlatform.SystemData.Domain.DatabaseOrchestration;
using IndustrialPlatform.SystemData.Testing;

namespace IndustrialPlatform.SystemData.Application.Tests;

/// <summary>
/// 蓝图 33 V2.0 §12 门禁验收:共享物理库多 ModuleKey 隔离(门禁 9,TASK-SD-004)。
/// module-a(SQL seed bundle)与 module-b(SecretBootstrap initializer)映射到同一临时 SQLite 物理文件,
/// 断言:业务表前缀独立、双账本各自独立({moduleKey}_schema_migrations / {moduleKey}_seed_ledger)、
/// checksum 隔离、advisory lock 键隔离(同物理库不同模块可并行)。
/// </summary>
public sealed class ModuleIsolationAcceptanceTests
{
    private static CancellationToken Ct => CancellationToken.None;

    [Fact]
    public async Task Gate09_SharedPhysicalDatabase_ModuleKeyIsolation()
    {
        using var harness = new InitializationGateHarness();
        harness.Scope.SecretProvider.SetSecret("module-b-bootstrap-admin", "admin-bootstrap-secret");
        var moduleA = TestInitializableService.ModuleASpec;
        var moduleB = TestInitializableService.ModuleBSpec;

        await harness.RegisterAsync(moduleA, cancellationToken: Ct);
        var planA = await harness.PlanAsync(moduleA, Ct);
        var opA = await harness.ApplyAsync(moduleA, planA, Ct);
        Assert.Equal(OperationStatus.Succeeded, opA.Status);

        await harness.RegisterAsync(moduleB, cancellationToken: Ct);
        var opB = await harness.ApplyAsync(moduleB, await harness.PlanAsync(moduleB, Ct), Ct);
        Assert.Equal(OperationStatus.Succeeded, opB.Status);

        // 同一物理库内:业务表前缀独立。
        Assert.True(await harness.Scope.Database.TableExistsAsync(moduleA.BusinessTable, Ct));
        Assert.True(await harness.Scope.Database.TableExistsAsync(moduleB.BusinessTable, Ct));

        // 双账本各自独立(表前缀 module-a_ / module-b_)。
        Assert.True(await harness.Scope.Database.MigrationLedgerExistsAsync(moduleA.ModuleKey, Ct));
        Assert.True(await harness.Scope.Database.SeedLedgerExistsAsync(moduleA.ModuleKey, Ct));
        Assert.True(await harness.Scope.Database.MigrationLedgerExistsAsync(moduleB.ModuleKey, Ct));
        Assert.True(await harness.Scope.Database.SeedLedgerExistsAsync(moduleB.ModuleKey, Ct));

        // 账本隔离:module-a 账本只含 module-a 种子,module-b 账本只含 module-b 种子。
        Assert.Equal(2, await harness.Scope.Database.SeedLedgerRowCountAsync(moduleA.ModuleKey, Ct));
        Assert.Equal(1, await harness.Scope.Database.SeedLedgerRowCountAsync(moduleB.ModuleKey, Ct));
        Assert.Null(await harness.Scope.Database.ReadSeedLedgerAsync(moduleA.ModuleKey, "module-b-bootstrap-admin", "1", Ct));
        Assert.Null(await harness.Scope.Database.ReadSeedLedgerAsync(moduleB.ModuleKey, "module-a-system-directory", "1", Ct));

        // checksum 隔离:各模块记录自己产物的内容派生校验和(内容不同 → 校验和不同)。
        var checksumA = (await harness.Scope.Database.ReadSeedLedgerAsync(moduleA.ModuleKey, "module-a-system-directory", "1", Ct))!.Checksum;
        var checksumB = (await harness.Scope.Database.ReadSeedLedgerAsync(moduleB.ModuleKey, "module-b-bootstrap-admin", "1", Ct))!.Checksum;
        Assert.False(string.Equals(checksumA, checksumB, StringComparison.Ordinal));

        // 锁键隔离:同物理库不同模块锁键不同(advisory lock 可并行,锁文件按规范化键派生)。
        var lockA = DatabaseTargetLockKey.FromTarget(
            harness.EnvironmentName, moduleA.ModuleKey, harness.Scope.Database.ResolvedTarget(moduleA.ModuleKey));
        var lockB = DatabaseTargetLockKey.FromTarget(
            harness.EnvironmentName, moduleB.ModuleKey, harness.Scope.Database.ResolvedTarget(moduleB.ModuleKey));
        Assert.NotEqual(lockA.ToCanonical(), lockB.ToCanonical());

        var handleA = await harness.Scope.Database.Adapter.AcquireAsync(lockA, harness.Scope.Database.Connection, TimeSpan.FromSeconds(1), Ct);
        Assert.NotNull(handleA);
        var handleB = await harness.Scope.Database.Adapter.AcquireAsync(lockB, harness.Scope.Database.Connection, TimeSpan.FromSeconds(1), Ct);
        Assert.NotNull(handleB);
        handleA?.Dispose();
        handleB?.Dispose();
    }
}
