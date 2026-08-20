using IndustrialPlatform.SystemData.Domain.DatabaseOrchestration;

namespace IndustrialPlatform.SystemData.Testing;

/// <summary>fixture 模块规格:模块标识、业务表与迁移/种子产物标识。</summary>
public sealed record TestModuleSpec(
    string ModuleKey,
    string BusinessTable,
    string MigrationArtifactId,
    string RequestedVersion,
    IReadOnlyList<TestSeedSpec> Seeds);

/// <summary>fixture 种子规格:稳定键/版本/类别/作用域/产物标识与 readiness、bootstrap 语义。</summary>
public sealed record TestSeedSpec(
    string SeedKey,
    string SeedVersion,
    SeedClass SeedClass,
    SeedScope Scope,
    string SeedArtifactId,
    bool RequiredForReadiness,
    BootstrapPolicy BootstrapPolicy);

/// <summary>
/// 示例宿主(TASK-SD-004 共享 fixture):模拟一个多模块共享宿主服务,暴露两个 ModuleKey。
/// <c>module-a</c>:签名 SQL seed bundle(SystemBaseline + TenantBaseline 种子);
/// <c>module-b</c>:服务 initializer bundle(SecretBootstrap 场景)→ 同时覆盖两类适配器/执行器路径。
/// 每个模块各自独立 Schema/迁移/种子产物与账本范围(禁止宿主级模糊种子包)。
/// </summary>
public static class TestInitializableService
{
    /// <summary>宿主服务稳定键。</summary>
    public const string ServiceKey = "testservice";

    /// <summary>宿主稳定逻辑库名。</summary>
    public const string LogicalDatabaseName = "testservice_db";

    /// <summary>基线迁移版本。</summary>
    public const string MigrationVersion = "1.0.0";

    /// <summary>fixture 签名引用(产物不可变 + allowlist + 内容派生校验和 + 签名引用)。</summary>
    public const string SignatureRef = "fixture-signer";

    /// <summary>module-a(签名 SQL seed bundle 模块)。</summary>
    public const string ModuleA = "module-a";

    /// <summary>module-b(服务 initializer / SecretBootstrap 模块)。</summary>
    public const string ModuleB = "module-b";

    /// <summary>module-a 规格:SystemBaseline(系统目录)+ TenantBaseline(租户默认数据)。</summary>
    public static TestModuleSpec ModuleASpec { get; } = new(
        ModuleKey: ModuleA,
        BusinessTable: "module_a_widget",
        MigrationArtifactId: "testservice.module-a.schema",
        RequestedVersion: MigrationVersion,
        Seeds:
        [
            new TestSeedSpec(
                SeedKey: "module-a-system-directory",
                SeedVersion: "1",
                SeedClass: SeedClass.SystemBaseline,
                Scope: SeedScope.System,
                SeedArtifactId: "testservice.module-a.seed.system-directory",
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

    /// <summary>module-b 规格:SecretBootstrap(admin 一次性敏感引导,缺 Secret fail-closed)。</summary>
    public static TestModuleSpec ModuleBSpec { get; } = new(
        ModuleKey: ModuleB,
        BusinessTable: "module_b_config",
        MigrationArtifactId: "testservice.module-b.schema",
        RequestedVersion: MigrationVersion,
        Seeds:
        [
            // RequiredForReadiness:false → 仅在 SecretBootstrap 阶段执行(该阶段才解析 Secret;
            // RequiredSeed 阶段对 SecretBootstrap 种子传 SecretValue:null,缺 Secret 会 fail-closed)。
            new TestSeedSpec(
                SeedKey: "module-b-bootstrap-admin",
                SeedVersion: "1",
                SeedClass: SeedClass.SecretBootstrap,
                Scope: SeedScope.System,
                SeedArtifactId: "testservice.module-b.seed.bootstrap-admin",
                RequiredForReadiness: false,
                BootstrapPolicy: BootstrapPolicy.FailClosed),
        ]);

    /// <summary>按模块标识取规格;未知模块抛异常。</summary>
    public static TestModuleSpec GetSpec(string moduleKey) => moduleKey switch
    {
        ModuleA => ModuleASpec,
        ModuleB => ModuleBSpec,
        _ => throw new ArgumentOutOfRangeException(nameof(moduleKey), moduleKey, "未知模块。"),
    };
}
