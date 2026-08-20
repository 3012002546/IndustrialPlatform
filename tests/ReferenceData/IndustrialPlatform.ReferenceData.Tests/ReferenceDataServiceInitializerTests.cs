using IndustrialPlatform.Application.Abstractions.Initialization;
using IndustrialPlatform.Infrastructure.Database;
using IndustrialPlatform.ReferenceData.Infrastructure.Initialization;
using Microsoft.Extensions.Options;
using SqlSugar;

namespace IndustrialPlatform.ReferenceData.Tests;

public sealed class ReferenceDataServiceInitializerTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"industrial-platform-referencedata-initializer-{Guid.NewGuid():N}.db");
    private readonly SqlSugarDbContext _dbContext;
    private readonly ReferenceDataServiceInitializer _initializer;

    public ReferenceDataServiceInitializerTests()
    {
        _dbContext = new SqlSugarDbContext(Options.Create(new SqlSugarOptions
        {
            ConnectionString = $"Data Source={_dbPath}",
            DbType = DbType.Sqlite,
        }));
        _initializer = new ReferenceDataServiceInitializer(new ReferenceDataInitializationLedger(_dbContext));
    }

    [Fact]
    public async Task Inspect_does_not_create_tables_and_apply_then_verify_is_ready()
    {
        var context = CreateContext();

        var inspection = await _initializer.InspectAsync(context, CancellationToken.None);

        Assert.False(inspection.Ready);
        Assert.False(await TableExistsAsync("reference_data_schema_migrations"));
        Assert.False(await TableExistsAsync("reference_data_seed_ledger"));

        var plan = await _initializer.PlanAsync(context, inspection, CancellationToken.None);
        await _initializer.ApplyAsync(context, plan, CancellationToken.None);
        var verified = await _initializer.VerifyAsync(context, CancellationToken.None);

        Assert.True(verified.Ready);
        Assert.True(await TableExistsAsync("reference_data_schema_migrations"));
        Assert.True(await TableExistsAsync("reference_data_seed_ledger"));
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        try
        {
            if (File.Exists(_dbPath))
            {
                File.Delete(_dbPath);
            }
        }
        catch (IOException)
        {
            // SQLite 连接池可能短暂占用文件句柄，清理失败不影响断言。
        }
    }

    private async Task<bool> TableExistsAsync(string tableName) =>
        await _dbContext.SqlSugar.Ado.GetIntAsync(
            $"SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='{tableName}'") == 1;

    private static ServiceInitializationContext CreateContext() => new(
        "Test",
        "tenant-1",
        "operation-1",
        "referencedata",
        "referencedata",
        new IndustrialPlatform.SharedKernel.Topology.ResolvedDatabaseTarget(
            "Test",
            IndustrialPlatform.SharedKernel.Topology.DatabaseTopologyMode.Shared,
            "referencedata",
            IndustrialPlatform.SharedKernel.Topology.DatabaseProvider.Sqlite,
            "referencedata_db",
            "target",
            false),
        ReferenceDataServiceInitializer.BaselineVersion,
        ServiceInitializationPolicy.Standard,
        "trace-1");
}
