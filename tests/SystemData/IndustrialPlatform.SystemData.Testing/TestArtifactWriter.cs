using System.Text.Json;
using System.Text.Json.Serialization;
using IndustrialPlatform.SystemData.Application.DatabaseOrchestration.Runner;
using IndustrialPlatform.SystemData.Domain.DatabaseOrchestration;
using IndustrialPlatform.SystemData.Infrastructure.DatabaseOrchestration.Runner;

namespace IndustrialPlatform.SystemData.Testing;

/// <summary>fixture 产物包:模块迁移产物 + 各种子产物(均含内容派生校验和与签名引用)。</summary>
public sealed record TestArtifactBundle(
    DatabaseMigrationArtifact Migration,
    IReadOnlyList<SeedArtifact> Seeds);

/// <summary>
/// 签名迁移/种子/initializer 产物生成器(TASK-SD-004):先构造产物再回填内容派生校验和
/// (allowlist + checksum + 签名引用,与生产 <see cref="MigrationArtifactChecksumVerifier"/> 同一规范化算法),
/// 序列化为 &lt;artifactId&gt;.json 供 <see cref="FileSystemArtifactStore"/> 读取。
/// 产物不可变;SQL 为 SQLite 方言(与生产 SqliteTargetDatabaseAdapter 同族)。
/// </summary>
public static class TestArtifactWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    /// <summary>构建模块全部产物并落盘到产物根目录,返回含校验和的产物包。</summary>
    public static TestArtifactBundle WriteModule(string artifactRootPath, TestModuleSpec module)
    {
        Directory.CreateDirectory(artifactRootPath);
        var migration = BuildMigrationArtifact(module);
        var seeds = module.Seeds.Select(seed => BuildSeedArtifact(module, seed)).ToList();
        Write(artifactRootPath, migration);
        foreach (var seed in seeds)
        {
            Write(artifactRootPath, seed);
        }

        return new TestArtifactBundle(migration, seeds);
    }

    /// <summary>构建签名迁移产物(单个 create 业务表步骤;内容派生校验和)。</summary>
    public static DatabaseMigrationArtifact BuildMigrationArtifact(TestModuleSpec module)
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
                    CreateTableSql(module.BusinessTable),
                    RollbackSql: null,
                    Destructive: false),
            ]);
        return artifact with { Checksum = MigrationArtifactChecksumVerifier.ComputeChecksum(artifact) };
    }

    /// <summary>构建签名种子产物(SQL bundle 或 initializer bundle;内容派生校验和)。</summary>
    public static SeedArtifact BuildSeedArtifact(TestModuleSpec module, TestSeedSpec seed)
    {
        var artifact = new SeedArtifact(
            seed.SeedArtifactId,
            seed.SeedVersion,
            Checksum: string.Empty,
            TestInitializableService.SignatureRef,
            ExecutorKind: seed.SeedClass == SeedClass.SecretBootstrap ? SeedExecutorKind.ServiceInitializer : SeedExecutorKind.SqlBundle,
            Steps: [BuildSeedStep(module, seed)]);
        return artifact with { Checksum = MigrationArtifactChecksumVerifier.ComputeChecksum(artifact) };
    }

    /// <summary>构建迁移升级产物:基表步骤 + 新增步骤(同 ArtifactId/Version,内容不同→校验和不同)。</summary>
    public static DatabaseMigrationArtifact BuildMigrationArtifactUpgrade(TestModuleSpec module, string extraStepId, string extraSql)
    {
        var baseArtifact = BuildMigrationArtifact(module);
        var artifact = new DatabaseMigrationArtifact(
            module.MigrationArtifactId,
            module.RequestedVersion,
            Checksum: string.Empty,
            TestInitializableService.SignatureRef,
            DeclaresRecoverable: true,
            Steps:
            [
                .. baseArtifact.Steps,
                new DatabaseMigrationArtifactStep(2, extraStepId, extraSql, RollbackSql: null, Destructive: false),
            ]);
        return artifact with { Checksum = MigrationArtifactChecksumVerifier.ComputeChecksum(artifact) };
    }

    /// <summary>构建漂移种子产物:同 ArtifactId/Version,内容不同(数据变化)→校验和不同。</summary>
    public static SeedArtifact BuildDriftedSeedArtifact(TestModuleSpec module, TestSeedSpec seed)
    {
        var artifact = new SeedArtifact(
            seed.SeedArtifactId,
            seed.SeedVersion,
            Checksum: string.Empty,
            TestInitializableService.SignatureRef,
            ExecutorKind: seed.SeedClass == SeedClass.SecretBootstrap ? SeedExecutorKind.ServiceInitializer : SeedExecutorKind.SqlBundle,
            Steps: [BuildSeedStep(module, seed) with { Sql = DriftedSeedInsertSql(module.BusinessTable, seed.SeedKey) }]);
        return artifact with { Checksum = MigrationArtifactChecksumVerifier.ComputeChecksum(artifact) };
    }

    /// <summary>构建失败种子产物:SQL 引用不存在的表,执行时抛裸 SqliteException(账本回滚)。</summary>
    public static SeedArtifact BuildFailingSeedArtifact(TestModuleSpec module, TestSeedSpec seed)
    {
        var artifact = new SeedArtifact(
            seed.SeedArtifactId,
            seed.SeedVersion,
            Checksum: string.Empty,
            TestInitializableService.SignatureRef,
            ExecutorKind: SeedExecutorKind.SqlBundle,
            Steps: [new SeedArtifactStep(1, $"insert-{seed.SeedKey}", FailingSeedSql(), RollbackSql: null)]);
        return artifact with { Checksum = MigrationArtifactChecksumVerifier.ComputeChecksum(artifact) };
    }

    /// <summary>公开写迁移产物文件(&lt;artifactId&gt;.json,供篡改/替换升级场景)。</summary>
    public static void WriteMigrationArtifact(string artifactRootPath, DatabaseMigrationArtifact artifact) =>
        Write(artifactRootPath, artifact);

    /// <summary>公开写种子产物文件(&lt;artifactId&gt;.json,供篡改/替换场景)。</summary>
    public static void WriteSeedArtifact(string artifactRootPath, SeedArtifact artifact)
    {
        Directory.CreateDirectory(artifactRootPath);
        Write(artifactRootPath, artifact);
    }

    private static SeedArtifactStep BuildSeedStep(TestModuleSpec module, TestSeedSpec seed) =>
        seed.SeedClass == SeedClass.SecretBootstrap
            ? new SeedArtifactStep(1, "bootstrap-secret", Sql: string.Empty, RollbackSql: null)
            : new SeedArtifactStep(1, $"insert-{seed.SeedKey}", SeedInsertSql(module.BusinessTable, seed.SeedKey), RollbackSql: null);

    private static string DriftedSeedInsertSql(string table, string seedKey) =>
        $"INSERT OR IGNORE INTO {table} (code, name) VALUES ('{seedKey}', 'drifted');";

    private static string FailingSeedSql() =>
        "INSERT INTO non_existent_ledger_boundary_table (code, name) VALUES ('x', 'y');";

    private static string CreateTableSql(string table) => $"""
        CREATE TABLE IF NOT EXISTS {table} (
          id INTEGER PRIMARY KEY AUTOINCREMENT,
          code TEXT NOT NULL UNIQUE,
          name TEXT NOT NULL
        );
        """;

    private static string SeedInsertSql(string table, string seedKey) =>
        $"INSERT OR IGNORE INTO {table} (code, name) VALUES ('{seedKey}', '{seedKey}');";

    private static void Write(string artifactRootPath, DatabaseMigrationArtifact artifact) =>
        WriteJson(artifactRootPath, artifact.ArtifactId, JsonSerializer.Serialize(artifact, JsonOptions));

    private static void Write(string artifactRootPath, SeedArtifact artifact) =>
        WriteJson(artifactRootPath, artifact.ArtifactId, JsonSerializer.Serialize(artifact, JsonOptions));

    private static void WriteJson(string artifactRootPath, string artifactId, string json)
    {
        var path = Path.Combine(artifactRootPath, $"{artifactId}.json");
        File.WriteAllText(path, json);
    }
}
