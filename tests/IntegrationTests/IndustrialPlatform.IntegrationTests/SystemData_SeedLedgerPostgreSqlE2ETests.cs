using System.Globalization;
using IndustrialPlatform.SystemData.Application.DatabaseOrchestration.Runner;
using IndustrialPlatform.SystemData.Domain.DatabaseOrchestration;
using IndustrialPlatform.SharedKernel.Topology;
using IndustrialPlatform.SystemData.Infrastructure.DatabaseOrchestration.Runner;
using IndustrialPlatform.SystemData.Testing;
using Npgsql;

namespace IndustrialPlatform.IntegrationTests.SystemData;

/// <summary>
/// PostgreSQL 种子账本执行语义测试(TASK-SD-004,蓝图 §12「SQLite/PG 等价」;真库分支「待验收」)。
/// 需真实 PostgreSQL,经环境变量 <c>SYSTEMDATA_PG_E2E=1</c> 启用(早退语义,配合
/// <c>[Trait("Category","Integration")]</c> 标记);未启用时方法直接返回。连接信息经 <c>SYSTEMDATA_PG_*</c>
/// 环境变量传入,不落任何配置文件。SQLite 等价本地替身见 <see cref="SeedLedgerExecutorTests"/>。
/// </summary>
[Trait("Category", "Integration")]
public sealed class SeedLedgerPostgreSqlE2ETests
{
    private const string EnvGate = "SYSTEMDATA_PG_E2E";

    private static readonly PostgreSqlTargetDatabaseAdapter Adapter = new();

    private static CancellationToken Ct => CancellationToken.None;

    private static bool Enabled =>
        string.Equals(Environment.GetEnvironmentVariable(EnvGate), "1", StringComparison.Ordinal);

    private static string Env(string name) => Environment.GetEnvironmentVariable(name) ?? string.Empty;

    private static int EnvPort()
    {
        var port = Env("SYSTEMDATA_PG_PORT");
        return int.TryParse(port, out var parsed) && parsed > 0 ? parsed : 5432;
    }

    /// <summary>PG E2E 专用模块(固定键/表/种子,产物 SQL 为 PostgreSQL 方言,避免与其他测试冲突)。</summary>
    private static TestModuleSpec Module { get; } = new(
        ModuleKey: "module-pg-e2e",
        BusinessTable: "module_pg_e2e_data",
        MigrationArtifactId: "testservice.module-pg-e2e.schema",
        RequestedVersion: "1.0.0",
        Seeds:
        [
            new TestSeedSpec(
                SeedKey: "module-pg-e2e-seed",
                SeedVersion: "1",
                SeedClass: SeedClass.SystemBaseline,
                Scope: SeedScope.System,
                SeedArtifactId: "testservice.module-pg-e2e.seed",
                RequiredForReadiness: true,
                BootstrapPolicy: BootstrapPolicy.FailClosed),
        ]);

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
        TestInitializableService.ServiceKey,
        DatabaseProvider.PostgreSQL,
        TestInitializableService.LogicalDatabaseName,
        physical,
        IsSharedPhysicalDatabase: false);

    private static string ConnectionString() => new NpgsqlConnectionStringBuilder
    {
        Host = Env("SYSTEMDATA_PG_HOST") is { Length: > 0 } host ? host : "localhost",
        Port = EnvPort(),
        Database = Env("SYSTEMDATA_PG_DATABASE") is { Length: > 0 } database ? database : "postgres",
        Username = Env("SYSTEMDATA_PG_USER"),
        Password = Env("SYSTEMDATA_PG_PASSWORD"),
        SearchPath = "public",
    }.ConnectionString;

    private static async Task<long> CountRowsAsync(string table, CancellationToken cancellationToken)
    {
        await using var pg = new NpgsqlConnection(ConnectionString());
        await pg.OpenAsync(cancellationToken);
        await using var cmd = pg.CreateCommand();
        cmd.CommandText = $"SELECT COUNT(1) FROM {table}";
        return Convert.ToInt64(await cmd.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
    }

    /// <summary>清理测试自产目标表(迁移账本 + 种子账本 + 业务表),云端验证后不留痕。</summary>
    /// <remarks>表名必须与产品派生规则一致(<c>SanitizeModuleKey</c> 转安全 SQL 标识,
    /// 不能直接拼 <c>{ModuleKey}_seed_ledger</c>,连字符会使 PostgreSQL 语法错误)。</remarks>
    private static async Task DropTestTablesAsync(CancellationToken cancellationToken)
    {
        await using var pg = new NpgsqlConnection(ConnectionString());
        await pg.OpenAsync(cancellationToken);
        foreach (var table in new[]
        {
            MigrationLedgerContracts.TableName(Module.ModuleKey),
            SeedLedgerContracts.TableName(Module.ModuleKey),
            Module.BusinessTable,
        })
        {
            await using var cmd = pg.CreateCommand();
            cmd.CommandText = $"DROP TABLE IF EXISTS {table};";
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static DatabaseMigrationArtifact PgMigrationArtifact(TestModuleSpec module)
    {
        var artifact = new DatabaseMigrationArtifact(
            module.MigrationArtifactId,
            module.RequestedVersion,
            Checksum: string.Empty,
            TestInitializableService.SignatureRef,
            DeclaresRecoverable: true,
            Steps:
            [
                new DatabaseMigrationArtifactStep(
                    1,
                    $"create-{module.BusinessTable}",
                    PgCreateTableSql(module.BusinessTable),
                    RollbackSql: null,
                    Destructive: false),
            ]);
        return artifact with { Checksum = MigrationArtifactChecksumVerifier.ComputeChecksum(artifact) };
    }

    private static SeedArtifact PgSeedArtifact(TestModuleSpec module, TestSeedSpec seed)
    {
        var artifact = new SeedArtifact(
            seed.SeedArtifactId,
            seed.SeedVersion,
            Checksum: string.Empty,
            TestInitializableService.SignatureRef,
            SeedExecutorKind.SqlBundle,
            Steps: [new SeedArtifactStep(1, $"insert-{seed.SeedKey}", PgInsertSql(module.BusinessTable, seed.SeedKey), RollbackSql: null)]);
        return artifact with { Checksum = MigrationArtifactChecksumVerifier.ComputeChecksum(artifact) };
    }

    private static SeedExecutionRequest PgRequest(TestModuleSpec module, TestSeedSpec seed, SeedArtifact artifact, string? secretValue, string operationNId)
    {
        var connection = AdminConnection();
        var domainSeed = new SeedSet(
            seed.SeedKey,
            seed.SeedVersion,
            seed.SeedClass,
            seed.Scope,
            seed.SeedArtifactId,
            artifact.Checksum,
            TestInitializableService.SignatureRef,
            seed.RequiredForReadiness,
            allowedEnvironments: string.Empty,
            dependsOnMigrationVersion: module.RequestedVersion,
            dependsOnSeedKeys: null,
            seed.BootstrapPolicy);
        return new SeedExecutionRequest(
            domainSeed,
            artifact,
            Target(connection.Database),
            connection,
            module.ModuleKey,
            TenantNId: "tenant-pg-e2e",
            EnvironmentNId: "Development",
            secretValue,
            operationNId,
            "trace-pg-e2e");
    }

    [Fact]
    public async Task SeedBundle_ApplyThenIdempotentReapply_OnPostgreSql()
    {
        if (!Enabled)
        {
            return;
        }

        var connection = AdminConnection();
        await Adapter.ApplyAsync(Target(connection.Database), PgMigrationArtifact(Module), connection, Module.ModuleKey, Ct);
        var seed = Module.Seeds[0];
        var artifact = PgSeedArtifact(Module, seed);
        var executor = new SqlSeedBundleExecutor();

        var first = await executor.ExecuteAsync(PgRequest(Module, seed, artifact, secretValue: null, "OP-1"), Ct);
        Assert.True(first.Succeeded);
        Assert.Equal(SeedStatus.Applied, first.Status);

        var again = await executor.ExecuteAsync(PgRequest(Module, seed, artifact, secretValue: null, "OP-2"), Ct);
        Assert.True(again.Succeeded);
        // 幂等重放不得产生新时间戳:比较瞬间而非偏移,并容忍 PG timestamptz 微秒精度截断
        // (Npgsql 读回比内存 UtcNow 少末位)。真正的新时间戳会相差秒级,容差 1ms 足以区分。
        var appliedDelta = (first.AppliedOn!.Value - again.AppliedOn!.Value).Duration();
        Assert.True(appliedDelta < TimeSpan.FromMilliseconds(1),
            $"幂等重放不应产生新时间戳:{first.AppliedOn} vs {again.AppliedOn}");
        Assert.Equal(1, await CountRowsAsync(SeedLedgerContracts.TableName(Module.ModuleKey), Ct));
        await DropTestTablesAsync(Ct);
    }

    [Fact]
    public async Task SeedBundle_VersionUpgradeAppends_OnPostgreSql()
    {
        if (!Enabled)
        {
            return;
        }

        var connection = AdminConnection();
        await Adapter.ApplyAsync(Target(connection.Database), PgMigrationArtifact(Module), connection, Module.ModuleKey, Ct);
        var executor = new SqlSeedBundleExecutor();

        var v1 = Module.Seeds[0];
        await executor.ExecuteAsync(PgRequest(Module, v1, PgSeedArtifact(Module, v1), secretValue: null, "OP-1"), Ct);

        // 同键不同版本 → 追加新账本行。
        var v2Seed = new TestSeedSpec(
            v1.SeedKey, "2", v1.SeedClass, v1.Scope, $"{v1.SeedArtifactId}.v2", true, BootstrapPolicy.FailClosed);
        var v2 = await executor.ExecuteAsync(PgRequest(Module, v2Seed, PgSeedArtifact(Module, v2Seed), secretValue: null, "OP-2"), Ct);
        Assert.True(v2.Succeeded);
        Assert.Equal(2, await CountRowsAsync(SeedLedgerContracts.TableName(Module.ModuleKey), Ct));
        await DropTestTablesAsync(Ct);
    }

    [Fact]
    public async Task Initializer_MissingSecret_FailsClosed_OnPostgreSql()
    {
        if (!Enabled)
        {
            return;
        }

        var bootstrapSeed = new TestSeedSpec(
            SeedKey: "module-pg-e2e-admin",
            SeedVersion: "1",
            SeedClass: SeedClass.SecretBootstrap,
            Scope: SeedScope.System,
            SeedArtifactId: "testservice.module-pg-e2e.seed.admin",
            RequiredForReadiness: false,
            BootstrapPolicy: BootstrapPolicy.FailClosed);
        var artifact = TestArtifactWriter.BuildSeedArtifact(Module, bootstrapSeed);

        var result = await new ServiceInitializerExecutor().ExecuteAsync(
            PgRequest(Module, bootstrapSeed, artifact, secretValue: null, "OP-1"), Ct);

        Assert.False(result.Succeeded);
        Assert.Equal(DatabaseOrchestrationRunnerErrors.BootstrapSecretMissing, result.ErrorCode);
        Assert.Equal(0, await CountRowsAsync(SeedLedgerContracts.TableName(Module.ModuleKey), Ct));
        await DropTestTablesAsync(Ct);
    }

    private static string PgCreateTableSql(string table) => $"""
        CREATE TABLE IF NOT EXISTS {table} (
          id SERIAL PRIMARY KEY,
          code TEXT NOT NULL UNIQUE,
          name TEXT NOT NULL
        );
        """;

    private static string PgInsertSql(string table, string seedKey) =>
        $"INSERT INTO {table} (code, name) VALUES ('{seedKey}', '{seedKey}') ON CONFLICT (code) DO NOTHING;";
}
