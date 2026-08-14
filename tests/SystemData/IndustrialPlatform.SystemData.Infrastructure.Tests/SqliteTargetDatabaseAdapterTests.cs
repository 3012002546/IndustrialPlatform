using IndustrialPlatform.SystemData.Application.DatabaseOrchestration.Runner;
using IndustrialPlatform.SystemData.Domain.Topology;
using IndustrialPlatform.SystemData.Infrastructure.DatabaseOrchestration.Runner;
using SQLitePCL;

namespace IndustrialPlatform.SystemData.Infrastructure.Tests;

/// <summary>
/// SQLite 目标数据库适配器测试(TASK-SD-003 本地替身,05 方案 §7.1.5)。真跑 Microsoft.Data.Sqlite:
/// inspect 存在性/版本/已应用步骤、provision 建文件、apply 事务内记账与幂等重放、文件锁互斥。
/// PostgreSQL 真库分支在 <see cref="PostgreSqlTargetDatabaseAdapterTests"/> 标记「待验收」。
/// </summary>
public sealed class SqliteTargetDatabaseAdapterTests : IDisposable
{
    static SqliteTargetDatabaseAdapterTests()
    {
        Batteries_V2.Init();
    }

    private readonly string _file;
    private readonly string _directory;
    private readonly SqliteTargetDatabaseAdapter _adapter = new();

    public SqliteTargetDatabaseAdapterTests()
    {
        _directory = Directory.CreateTempSubdirectory("industrial-platform-sd3-").FullName;
        _file = Path.Combine(_directory, "target.db");
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // 文件句柄可能被连接池短暂占用,忽略清理失败。
        }
    }

    private static CancellationToken Ct => CancellationToken.None;

    private ResolvedDatabaseTarget Target => new(
        "Development",
        DatabaseTopologyMode.Shared,
        "systemdata",
        DatabaseProvider.Sqlite,
        "systemdata_db",
        _file,
        IsSharedPhysicalDatabase: false);

    private static TargetDatabaseConnection FileConnection(string file) =>
        new("Sqlite", string.Empty, 0, file, null, null, null);

    private static DatabaseTargetCredentials Credentials(string file) =>
        new(null, FileConnection(file), FileConnection(file));

    private static DatabaseMigrationArtifact Artifact(params DatabaseMigrationArtifactStep[] steps) => new(
        "artifact-1",
        "9.9.9",
        "checksum-placeholder",
        "sig-ref",
        DeclaresRecoverable: false,
        steps);

    // ===== inspect =====

    [Fact]
    public async Task Inspect_MissingFile_ReportsNotExists()
    {
        var inspection = await _adapter.InspectAsync(Target, Credentials(_file), Ct);

        Assert.False(inspection.DatabaseExists);
        Assert.Null(inspection.CurrentVersion);
        Assert.Empty(inspection.AppliedStepIds);
    }

    [Fact]
    public async Task Inspect_FileWithoutLedger_ReportsExistsNoVersion()
    {
        await _adapter.EnsureDatabaseAsync(Target, FileConnection(_file), Ct);

        var inspection = await _adapter.InspectAsync(Target, Credentials(_file), Ct);

        Assert.True(inspection.DatabaseExists);
        Assert.Null(inspection.CurrentVersion);
        Assert.Empty(inspection.AppliedStepIds);
    }

    // ===== provision =====

    [Fact]
    public async Task EnsureDatabase_CreatesTargetFile()
    {
        var outcome = await _adapter.EnsureDatabaseAsync(Target, FileConnection(_file), Ct);

        Assert.True(outcome.DatabaseCreated);
        Assert.False(outcome.DatabaseExisted);
        Assert.True(File.Exists(_file));
    }

    [Fact]
    public async Task EnsureDatabase_ExistingFile_ReportsExisted()
    {
        await _adapter.EnsureDatabaseAsync(Target, FileConnection(_file), Ct);

        var outcome = await _adapter.EnsureDatabaseAsync(Target, FileConnection(_file), Ct);

        Assert.False(outcome.DatabaseCreated);
        Assert.True(outcome.DatabaseExisted);
    }

    [Fact]
    public async Task EnsureRoles_ReturnsBothRolesForSameFile()
    {
        var roles = await _adapter.EnsureRolesAsync(Target, FileConnection(_file), Ct);

        Assert.Equal(_file, roles.Migrator.Database);
        Assert.Equal(_file, roles.Runtime.Database);
    }

    // ===== apply + 账本 =====

    [Fact]
    public async Task Apply_AppliesStepsAndRecordsLedger()
    {
        var artifact = Artifact(
            new DatabaseMigrationArtifactStep(1, "step-1", "CREATE TABLE t1 (id INTEGER NOT NULL PRIMARY KEY);", null, false),
            new DatabaseMigrationArtifactStep(2, "step-2", "CREATE TABLE t2 (id INTEGER NOT NULL PRIMARY KEY);", null, false));

        var result = await _adapter.ApplyAsync(Target, artifact, FileConnection(_file), Ct);

        Assert.Equal(2, result.AppliedStepCount);
        Assert.Equal("9.9.9", result.ResultingVersion);

        var inspection = await _adapter.InspectAsync(Target, Credentials(_file), Ct);
        Assert.True(inspection.DatabaseExists);
        Assert.Equal("9.9.9", inspection.CurrentVersion);
        Assert.Equal(["step-1", "step-2"], inspection.AppliedStepIds);
    }

    [Fact]
    public async Task Apply_SecondInvocation_SkipsAlreadyAppliedSteps()
    {
        var first = Artifact(
            new DatabaseMigrationArtifactStep(1, "step-1", "CREATE TABLE t1 (id INTEGER NOT NULL PRIMARY KEY);", null, false),
            new DatabaseMigrationArtifactStep(2, "step-2", "CREATE TABLE t2 (id INTEGER NOT NULL PRIMARY KEY);", null, false));
        await _adapter.ApplyAsync(Target, first, FileConnection(_file), Ct);

        var second = Artifact(
            new DatabaseMigrationArtifactStep(1, "step-1", "CREATE TABLE t1 (id INTEGER NOT NULL PRIMARY KEY);", null, false),
            new DatabaseMigrationArtifactStep(2, "step-2", "CREATE TABLE t2 (id INTEGER NOT NULL PRIMARY KEY);", null, false),
            new DatabaseMigrationArtifactStep(3, "step-3", "CREATE TABLE t3 (id INTEGER NOT NULL PRIMARY KEY);", null, false));

        var result = await _adapter.ApplyAsync(Target, second, FileConnection(_file), Ct);

        Assert.Equal(1, result.AppliedStepCount);
        var inspection = await _adapter.InspectAsync(Target, Credentials(_file), Ct);
        Assert.Equal(["step-1", "step-2", "step-3"], inspection.AppliedStepIds);
    }

    // ===== advisory lock(文件锁互斥)=====

    [Fact]
    public async Task Acquire_FileLock_AcquiresThenReleases()
    {
        var key = DatabaseTargetLockKey.FromTarget("Development", Target);

        var first = await _adapter.AcquireAsync(key, FileConnection(_file), TimeSpan.FromSeconds(1), Ct);
        Assert.NotNull(first);
        first!.Dispose();

        var second = await _adapter.AcquireAsync(key, FileConnection(_file), TimeSpan.FromSeconds(1), Ct);
        Assert.NotNull(second);
        second!.Dispose();
    }

    [Fact]
    public async Task Acquire_FileLockHeld_ReturnsNullOnTimeout()
    {
        var key = DatabaseTargetLockKey.FromTarget("Development", Target);

        var held = await _adapter.AcquireAsync(key, FileConnection(_file), TimeSpan.FromSeconds(1), Ct);
        Assert.NotNull(held);

        var timeout = await _adapter.AcquireAsync(key, FileConnection(_file), TimeSpan.FromMilliseconds(100), Ct);
        Assert.Null(timeout);
        held!.Dispose();
    }
}
