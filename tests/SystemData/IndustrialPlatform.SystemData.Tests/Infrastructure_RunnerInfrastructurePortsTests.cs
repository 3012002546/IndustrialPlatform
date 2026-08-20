using System.Text;
using System.Text.Json;
using IndustrialPlatform.SystemData.Application.DatabaseOrchestration;
using IndustrialPlatform.SystemData.Application.DatabaseOrchestration.Options;
using IndustrialPlatform.SystemData.Application.DatabaseOrchestration.Runner;
using IndustrialPlatform.SharedKernel.Topology;
using IndustrialPlatform.SystemData.Domain.Topology;
using IndustrialPlatform.SystemData.Infrastructure.DatabaseOrchestration.Runner;
using Microsoft.Extensions.Options;

namespace IndustrialPlatform.SystemData.Infrastructure.Tests;

/// <summary>
/// Runner 基础设施端口测试(TASK-SD-003):产物文件存储、checksum 校验器、凭据落盘器与凭据解析器。
/// 全部为本地替身(不触真实数据库),SQLite 分支可运行;PG 分支在 <see cref="PostgreSqlTargetDatabaseAdapterTests"/> 待验收。
/// </summary>
public sealed class RunnerInfrastructurePortsTests : IDisposable
{
    private readonly string _directory;

    public RunnerInfrastructurePortsTests()
    {
        _directory = Directory.CreateTempSubdirectory("industrial-platform-sd3-ports-").FullName;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // 文件句柄可能被短暂占用,忽略清理失败。
        }
    }

    private static CancellationToken Ct => CancellationToken.None;

    private static string Sha(string value) => DatabaseTopologyFingerprint.Sha256Hex(value);

    private static ResolvedDatabaseTarget SqliteTarget(string file) => new(
        "Development",
        DatabaseTopologyMode.Shared,
        "systemdata",
        DatabaseProvider.Sqlite,
        "systemdata_db",
        file,
        IsSharedPhysicalDatabase: false);

    // ===== FileSystemArtifactStore =====

    [Fact]
    public async Task ArtifactStore_ExistingArtifact_ReturnsArtifact()
    {
        var artifact = SampleArtifact();
        var json = JsonSerializer.Serialize(artifact);
        var path = Path.Combine(_directory, $"{artifact.ArtifactId}.json");
        await File.WriteAllTextAsync(path, json, Encoding.UTF8, Ct);

        var store = new FileSystemArtifactStore(Options.Create(new DatabaseOperationRunnerOptions
        {
            ArtifactRootPath = _directory,
        }));

        var resolved = await store.ResolveAsync(artifact.ArtifactId, Ct);

        Assert.Equal(artifact.ArtifactId, resolved.ArtifactId);
        Assert.Equal(artifact.Version, resolved.Version);
        Assert.Equal(2, resolved.Steps.Count);
        Assert.Equal("step-2", resolved.Steps[1].StepId);
    }

    [Fact]
    public async Task ArtifactStore_MissingFile_ThrowsArtifactInvalid()
    {
        var store = new FileSystemArtifactStore(Options.Create(new DatabaseOperationRunnerOptions
        {
            ArtifactRootPath = _directory,
        }));

        var ex = await Assert.ThrowsAsync<DatabaseOrchestrationRunnerException>(
            () => store.ResolveAsync("missing", Ct));

        Assert.Equal(DatabaseOrchestrationRunnerErrors.ArtifactInvalid, ex.Code);
    }

    [Fact]
    public async Task ArtifactStore_InvalidJson_ThrowsArtifactInvalid()
    {
        var path = Path.Combine(_directory, "broken.json");
        await File.WriteAllTextAsync(path, "{ not json", Encoding.UTF8, Ct);
        var store = new FileSystemArtifactStore(Options.Create(new DatabaseOperationRunnerOptions
        {
            ArtifactRootPath = _directory,
        }));

        var ex = await Assert.ThrowsAsync<DatabaseOrchestrationRunnerException>(
            () => store.ResolveAsync("broken", Ct));

        Assert.Equal(DatabaseOrchestrationRunnerErrors.ArtifactInvalid, ex.Code);
    }

    [Fact]
    public async Task ArtifactStore_NoRootPath_ThrowsArtifactInvalid()
    {
        var store = new FileSystemArtifactStore(Options.Create(new DatabaseOperationRunnerOptions()));

        var ex = await Assert.ThrowsAsync<DatabaseOrchestrationRunnerException>(
            () => store.ResolveAsync("any", Ct));

        Assert.Equal(DatabaseOrchestrationRunnerErrors.ArtifactInvalid, ex.Code);
    }

    // ===== MigrationArtifactChecksumVerifier =====

    [Fact]
    public async Task ChecksumVerifier_MatchingChecksum_ReturnsTrue()
    {
        var artifact = SampleArtifact();
        var checksum = MigrationArtifactChecksumVerifier.ComputeChecksum(artifact);
        var verifier = new MigrationArtifactChecksumVerifier();

        var valid = await verifier.VerifyAsync(artifact, checksum, "sig-ref", Ct);

        Assert.True(valid);
    }

    [Fact]
    public async Task ChecksumVerifier_TamperedSql_ReturnsFalse()
    {
        var original = SampleArtifact();
        var checksum = MigrationArtifactChecksumVerifier.ComputeChecksum(original);
        var tampered = original with { Steps = [new DatabaseMigrationArtifactStep(1, "step-1", "DROP TABLE t1;", null, true)] };
        var verifier = new MigrationArtifactChecksumVerifier();

        var valid = await verifier.VerifyAsync(tampered, checksum, "sig-ref", Ct);

        Assert.False(valid);
    }

    [Fact]
    public async Task ChecksumVerifier_SignatureRefMismatch_ReturnsFalse()
    {
        var artifact = SampleArtifact();
        var checksum = MigrationArtifactChecksumVerifier.ComputeChecksum(artifact);
        var verifier = new MigrationArtifactChecksumVerifier();

        var valid = await verifier.VerifyAsync(artifact, checksum, "different-sig", Ct);

        Assert.False(valid);
    }

    [Fact]
    public void ChecksumVerifier_ComputeChecksum_IsOrderIndependent()
    {
        var steps1 = new List<DatabaseMigrationArtifactStep>
        {
            new(1, "step-1", "CREATE TABLE t1 (id INTEGER);", null, false),
            new(2, "step-2", "CREATE TABLE t2 (id INTEGER);", null, false),
        };
        var steps2 = new List<DatabaseMigrationArtifactStep>
        {
            new(2, "step-2", "CREATE TABLE t2 (id INTEGER);", null, false),
            new(1, "step-1", "CREATE TABLE t1 (id INTEGER);", null, false),
        };
        var first = SampleArtifact() with { Steps = steps1 };
        var second = SampleArtifact() with { Steps = steps2 };

        Assert.Equal(
            MigrationArtifactChecksumVerifier.ComputeChecksum(first),
            MigrationArtifactChecksumVerifier.ComputeChecksum(second));
    }

    // ===== FileCredentialSink =====

    [Fact]
    public async Task CredentialSink_ConfiguredPath_WritesFileWithRoles()
    {
        var sink = new FileCredentialSink(Options.Create(new DatabaseOperationRunnerOptions
        {
            SecretSinkPath = _directory,
        }));
        var target = SqliteTarget("target.db");
        var roles = new ProvisionedRoles(
            new TargetDatabaseConnection("Sqlite", string.Empty, 0, "target.db", null, "sd_systemdata_migrator", "migrator-secret"),
            new TargetDatabaseConnection("Sqlite", string.Empty, 0, "target.db", null, "sd_systemdata_runtime", "runtime-secret"));

        await sink.WriteAsync(target, roles, Ct);

        var files = Directory.GetFiles(_directory, "credentials-*.json");
        Assert.Single(files);
        var json = await File.ReadAllTextAsync(files[0], Encoding.UTF8, Ct);
        Assert.Contains("sd_systemdata_migrator", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CredentialSink_NoSinkPath_Skips()
    {
        var sink = new FileCredentialSink(Options.Create(new DatabaseOperationRunnerOptions()));
        var target = SqliteTarget("target.db");
        var roles = new ProvisionedRoles(
            new TargetDatabaseConnection("Sqlite", string.Empty, 0, "target.db", null, null, null),
            new TargetDatabaseConnection("Sqlite", string.Empty, 0, "target.db", null, null, null));

        await sink.WriteAsync(target, roles, Ct);

        Assert.Empty(Directory.GetFiles(_directory));
    }

    // ===== EnvironmentCredentialResolver =====

    [Fact]
    public async Task CredentialResolver_Sqlite_ReturnsPhysicalFileConnection()
    {
        var resolver = new EnvironmentCredentialResolver();

        var credentials = await resolver.ResolveAsync(SqliteTarget("industrial-platform.db"), provisionRequired: false, Ct);

        Assert.Null(credentials.Admin);
        Assert.NotNull(credentials.Migrator);
        Assert.Equal("industrial-platform.db", credentials.Migrator!.Database);
        Assert.Equal("Sqlite", credentials.Migrator.Provider);
    }

    [Fact]
    public async Task CredentialResolver_Sqlite_ProvisionRequired_ReturnsAdmin()
    {
        var resolver = new EnvironmentCredentialResolver();

        var credentials = await resolver.ResolveAsync(SqliteTarget("industrial-platform.db"), provisionRequired: true, Ct);

        Assert.NotNull(credentials.Admin);
        Assert.Equal("industrial-platform.db", credentials.Admin!.Database);
    }

    // ===== 样例产物 =====

    private static DatabaseMigrationArtifact SampleArtifact()
    {
        // 验证器要求产物自带 Checksum == ComputeChecksum(产物内容);先建对象再回填内容派生校验和,种子自洽。
        var artifact = new DatabaseMigrationArtifact(
            "artifact-1",
            "9.9.9",
            Sha("checksum-placeholder"),
            "sig-ref",
            DeclaresRecoverable: false,
            [
                new DatabaseMigrationArtifactStep(1, "step-1", "CREATE TABLE t1 (id INTEGER NOT NULL PRIMARY KEY);", null, false),
                new DatabaseMigrationArtifactStep(2, "step-2", "CREATE TABLE t2 (id INTEGER NOT NULL PRIMARY KEY);", null, false),
            ]);
        return artifact with { Checksum = MigrationArtifactChecksumVerifier.ComputeChecksum(artifact) };
    }
}
