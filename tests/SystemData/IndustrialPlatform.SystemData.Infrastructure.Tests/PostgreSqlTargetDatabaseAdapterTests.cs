using IndustrialPlatform.SystemData.Application.DatabaseOrchestration.Runner;
using IndustrialPlatform.SystemData.Domain.Topology;
using IndustrialPlatform.SystemData.Infrastructure.DatabaseOrchestration.Runner;

namespace IndustrialPlatform.SystemData.Infrastructure.Tests;

/// <summary>
/// PostgreSQL 目标数据库适配器测试(TASK-SD-003,真库分支「待验收」)。
/// 需真实 PostgreSQL,经环境变量 <c>SYSTEMDATA_PG_E2E=1</c> 启用(早退语义,配合
/// <c>[Trait("Category","E2E")]</c> 标记);未启用时方法直接返回,由类注释说明依赖。
/// 连接信息经环境变量传入(HOST/PORT/DATABASE/SCHEMA/USER/PASSWORD,取 <c>SYSTEMDATA_PG_*</c>),
/// 不落任何配置文件。SQLite 本地替身测试见 <see cref="SqliteTargetDatabaseAdapterTests"/>。
/// </summary>
[Trait("Category", "E2E")]
public sealed class PostgreSqlTargetDatabaseAdapterTests
{
    private static readonly PostgreSqlTargetDatabaseAdapter Adapter = new();

    private const string EnvGate = "SYSTEMDATA_PG_E2E";

    private static CancellationToken Ct => CancellationToken.None;

    /// <summary>被测模块标识(账本表名按 {moduleKey}_schema_migrations 派生)。</summary>
    private const string ModuleKey = "systemdata";

    private static bool Enabled =>
        string.Equals(Environment.GetEnvironmentVariable(EnvGate), "1", StringComparison.Ordinal);

    private static string Env(string name) => Environment.GetEnvironmentVariable(name) ?? string.Empty;

    private static int EnvPort()
    {
        var port = Env("SYSTEMDATA_PG_PORT");
        return int.TryParse(port, out var parsed) && parsed > 0 ? parsed : 5432;
    }

    private static TargetDatabaseConnection AdminConnection() => new(
        "PostgreSQL",
        Env("SYSTEMDATA_PG_HOST") is { Length: > 0 } host ? host : "localhost",
        EnvPort(),
        Env("SYSTEMDATA_PG_DATABASE") is { Length: > 0 } database ? database : "postgres",
        "public",
        Env("SYSTEMDATA_PG_USER"),
        Env("SYSTEMDATA_PG_PASSWORD"));

    private static ResolvedDatabaseTarget Target(string physical) => new(
        "Development",
        DatabaseTopologyMode.PerService,
        "systemdata",
        DatabaseProvider.PostgreSQL,
        "systemdata_db",
        physical,
        IsSharedPhysicalDatabase: false);

    private static DatabaseTargetCredentials Credentials(TargetDatabaseConnection admin)
    {
        var migrator = new TargetDatabaseConnection("PostgreSQL", admin.Host, admin.Port, Env("SYSTEMDATA_PG_DATABASE"), "public", Env("SYSTEMDATA_PG_USER"), Env("SYSTEMDATA_PG_PASSWORD"));
        return new DatabaseTargetCredentials(admin, migrator, migrator);
    }

    [Fact]
    public async Task Inspect_ReachableDatabase_ReportsExists()
    {
        if (!Enabled)
        {
            return;
        }

        var admin = AdminConnection();
        var inspection = await Adapter.InspectAsync(Target(admin.Database), Credentials(admin), ModuleKey, Ct);

        Assert.True(inspection.DatabaseExists);
    }

    [Fact]
    public async Task EnsureRoles_CreatesMigratorAndRuntimeRoles()
    {
        if (!Enabled)
        {
            return;
        }

        var admin = AdminConnection();
        var roles = await Adapter.EnsureRolesAsync(Target(admin.Database), admin, Ct);

        Assert.False(string.IsNullOrWhiteSpace(roles.Migrator.Username));
        Assert.False(string.IsNullOrWhiteSpace(roles.Runtime.Username));
        Assert.Equal(admin.Database, roles.Migrator.Database);
    }

    [Fact]
    public async Task Apply_AppliesStepsAndRecordsLedger()
    {
        if (!Enabled)
        {
            return;
        }

        var admin = AdminConnection();
        var target = Target(admin.Database);
        var artifact = new DatabaseMigrationArtifact(
            "artifact-1",
            "9.9.9",
            "checksum-placeholder",
            "sig-ref",
            DeclaresRecoverable: false,
            [
                new DatabaseMigrationArtifactStep(1, "step-1", "CREATE TABLE sd3_test_step1 (id integer NOT NULL PRIMARY KEY);", null, false),
                new DatabaseMigrationArtifactStep(2, "step-2", "CREATE TABLE sd3_test_step2 (id integer NOT NULL PRIMARY KEY);", null, false),
            ]);

        var result = await Adapter.ApplyAsync(target, artifact, AdminConnection(), ModuleKey, Ct);

        Assert.Equal(2, result.AppliedStepCount);
        var inspection = await Adapter.InspectAsync(target, Credentials(admin), ModuleKey, Ct);
        Assert.Equal("9.9.9", inspection.CurrentVersion);
        Assert.Equal(["step-1", "step-2"], inspection.AppliedStepIds);
    }

    [Fact]
    public async Task Acquire_AdvisoryLock_AcquiresAndReleases()
    {
        if (!Enabled)
        {
            return;
        }

        var admin = AdminConnection();
        var key = DatabaseTargetLockKey.FromTarget("Development", ModuleKey, Target(admin.Database));

        var first = await Adapter.AcquireAsync(key, admin, TimeSpan.FromSeconds(2), Ct);
        Assert.NotNull(first);
        first!.Dispose();

        var second = await Adapter.AcquireAsync(key, admin, TimeSpan.FromSeconds(2), Ct);
        Assert.NotNull(second);
        second!.Dispose();
    }
}
