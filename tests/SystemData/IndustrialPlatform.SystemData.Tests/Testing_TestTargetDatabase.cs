using IndustrialPlatform.SystemData.Application.DatabaseOrchestration.Runner;
using IndustrialPlatform.SystemData.Domain.DatabaseOrchestration;
using IndustrialPlatform.SharedKernel.Topology;
using IndustrialPlatform.SystemData.Infrastructure.DatabaseOrchestration.Runner;
using Microsoft.Data.Sqlite;

namespace IndustrialPlatform.SystemData.Testing;

/// <summary>
/// 目标数据库双账本路径帮助器(TASK-SD-004,蓝图 §5.2/§5.3):临时 SQLite 物理库替身,
/// 提供迁移账本(<c>{moduleKey}_schema_migrations</c>)与种子账本(<c>{moduleKey}_seed_ledger</c>)直查。
/// 账本表名/列名/SQL 取自 Infrastructure 内部契约,断言与生产同源;种子账本读取复用执行器读路径,
/// 与 Runner Verify 语义一致。SQL 模板标准 SQL,可指向 PostgreSQL 连接扩展(真实验证待验收)。
/// Dispose 删除临时库(不影响仓库文件)。
/// </summary>
public sealed class TestTargetDatabase : IDisposable
{
    private readonly string _filePath;

    /// <summary>创建临时 SQLite 物理库(空文件,ReadWriteCreate)。</summary>
    public TestTargetDatabase()
    {
        _filePath = Path.Combine(Path.GetTempPath(), $"industrial-platform-fixture-{Guid.NewGuid():N}.db");
        using var conn = new SqliteConnection(ConnectionString);
        conn.Open();
    }

    /// <summary>SQLite 连接串(ReadWriteCreate)。</summary>
    public string ConnectionString => new SqliteConnectionStringBuilder
    {
        DataSource = _filePath,
        Mode = SqliteOpenMode.ReadWriteCreate,
    }.ToString();

    /// <summary>临时物理库文件路径(供 Runner 注册/拓扑映射与锁键断言)。</summary>
    public string FilePath => _filePath;

    /// <summary>本地替身目标连接(单角色)。</summary>
    public TargetDatabaseConnection Connection => new("Sqlite", string.Empty, 0, _filePath, null, null, null);

    /// <summary>SQLite 目标适配器(替身;inspect/provision/migrate/advisory lock)。</summary>
    public SqliteTargetDatabaseAdapter Adapter { get; } = new();

    /// <summary>解析出的模块级目标(Development/PerService/Sqlite)。</summary>
    public ResolvedDatabaseTarget ResolvedTarget(string moduleKey) => new(
        EnvironmentName: "Development",
        Mode: DatabaseTopologyMode.PerService,
        ServiceKey: TestInitializableService.ServiceKey,
        Provider: DatabaseProvider.Sqlite,
        LogicalDatabaseName: TestInitializableService.LogicalDatabaseName,
        PhysicalDatabaseName: _filePath,
        IsSharedPhysicalDatabase: false);

    /// <summary>在目标库应用模块迁移产物(账本幂等)。</summary>
    public async Task<MigrationExecutionResult> ApplyMigrationAsync(TestModuleSpec module, CancellationToken cancellationToken)
    {
        var artifact = TestArtifactWriter.BuildMigrationArtifact(module);
        return await Adapter.ApplyAsync(ResolvedTarget(module.ModuleKey), artifact, Connection, module.ModuleKey, cancellationToken);
    }

    /// <summary>在目标库执行单条种子(按类别选 SQL bundle / initializer 执行器)。</summary>
    public async Task<SeedExecutionResult> ExecuteSeedAsync(
        TestModuleSpec module,
        TestSeedSpec seed,
        string? secretValue,
        string operationNId,
        string traceId,
        CancellationToken cancellationToken)
    {
        var artifact = TestArtifactWriter.BuildSeedArtifact(module, seed);
        ISeedExecutor executor = seed.SeedClass == SeedClass.SecretBootstrap
            ? new ServiceInitializerExecutor()
            : new SqlSeedBundleExecutor();
        var request = BuildSeedRequest(module, seed, artifact, secretValue, operationNId, traceId);
        return await executor.ExecuteAsync(request, cancellationToken);
    }

    /// <summary>构造种子执行请求(领域 SeedSet 校验和由产物内容派生)。</summary>
    public SeedExecutionRequest BuildSeedRequest(
        TestModuleSpec module,
        TestSeedSpec seed,
        SeedArtifact artifact,
        string? secretValue,
        string operationNId,
        string traceId,
        string tenantNId = "tenant-fixture")
    {
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
            ResolvedTarget(module.ModuleKey),
            Connection,
            module.ModuleKey,
            TenantNId: tenantNId,
            EnvironmentNId: "Development",
            secretValue,
            operationNId,
            traceId);
    }

    // ===== 账本直查帮助器(双账本路径) =====

    /// <summary>业务表/账本表是否存在(sqlite_master 直查,规避 SqlSugar 进程级缓存)。</summary>
    public async Task<bool> TableExistsAsync(string tableName, CancellationToken cancellationToken)
    {
        await using var conn = new SqliteConnection(ConnectionString);
        await conn.OpenAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(1) FROM sqlite_master WHERE type = 'table' AND name = $name";
        cmd.Parameters.AddWithValue("name", tableName);
        var count = Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken), System.Globalization.CultureInfo.InvariantCulture);
        return count > 0;
    }

    /// <summary>模块迁移账本是否存在。</summary>
    public Task<bool> MigrationLedgerExistsAsync(string moduleKey, CancellationToken cancellationToken) =>
        TableExistsAsync(MigrationLedgerContracts.TableName(moduleKey), cancellationToken);

    /// <summary>模块种子账本是否存在。</summary>
    public Task<bool> SeedLedgerExistsAsync(string moduleKey, CancellationToken cancellationToken) =>
        TableExistsAsync(SeedLedgerContracts.TableName(moduleKey), cancellationToken);

    /// <summary>任意表行数(业务表/账本表直查,幂等与账本边界断言)。</summary>
    public async Task<long> TableRowCountAsync(string tableName, CancellationToken cancellationToken)
    {
        await using var conn = new SqliteConnection(ConnectionString);
        await conn.OpenAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT COUNT(1) FROM {tableName}";
        return Convert.ToInt64(await cmd.ExecuteScalarAsync(cancellationToken), System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>模块种子账本行数(不存在返回 0)。</summary>
    public async Task<long> SeedLedgerRowCountAsync(string moduleKey, CancellationToken cancellationToken)
    {
        if (!await SeedLedgerExistsAsync(moduleKey, cancellationToken))
        {
            return 0;
        }

        return await TableRowCountAsync(SeedLedgerContracts.TableName(moduleKey), cancellationToken);
    }

    /// <summary>已应用的迁移步骤标识(按应用顺序)。</summary>
    public async Task<IReadOnlyList<string>> AppliedMigrationStepIdsAsync(string moduleKey, CancellationToken cancellationToken)
    {
        var ids = new List<string>();
        await using var conn = new SqliteConnection(ConnectionString);
        await conn.OpenAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = MigrationLedgerContracts.AppliedIdsSql(moduleKey);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            ids.Add(reader.GetString(0));
        }

        return ids;
    }

    /// <summary>目标库当前迁移版本(账本不存在/无记录返回 null)。</summary>
    public async Task<string?> LatestMigrationVersionAsync(string moduleKey, CancellationToken cancellationToken)
    {
        if (!await MigrationLedgerExistsAsync(moduleKey, cancellationToken))
        {
            return null;
        }

        await using var conn = new SqliteConnection(ConnectionString);
        await conn.OpenAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = MigrationLedgerContracts.LatestVersionSql(moduleKey);
        var raw = await cmd.ExecuteScalarAsync(cancellationToken);
        return raw is null or DBNull ? null : Convert.ToString(raw, System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>读取目标库种子账本条目(复用 SQL bundle 执行器读路径,与生产 Verify 同源;不存在返回 null)。</summary>
    public async Task<SeedLedgerEntry?> ReadSeedLedgerAsync(string moduleKey, string seedKey, string seedVersion, CancellationToken cancellationToken)
    {
        if (!await SeedLedgerExistsAsync(moduleKey, cancellationToken))
        {
            return null;
        }

        var query = new SeedLedgerQuery(
            TenantNId: "tenant-fixture",
            EnvironmentNId: "Development",
            ServiceKey: TestInitializableService.ServiceKey,
            moduleKey,
            seedKey,
            seedVersion,
            OperationNId: "fixture",
            TraceId: "fixture",
            ResolvedTarget(moduleKey),
            Connection);
        var executor = new SqlSeedBundleExecutor();
        return await executor.ReadLedgerAsync(query, cancellationToken);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try
        {
            if (File.Exists(_filePath))
            {
                File.Delete(_filePath);
            }
        }
        catch (IOException)
        {
            // 文件被占用时保留,由测试后清理兜底。
        }
    }
}
