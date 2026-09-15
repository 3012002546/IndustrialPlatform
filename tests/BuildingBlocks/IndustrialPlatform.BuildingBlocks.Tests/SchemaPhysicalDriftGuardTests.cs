using IndustrialPlatform.Infrastructure.Database;
using Microsoft.Extensions.Options;
using SqlSugar;
using SQLitePCL;
using Xunit;

namespace IndustrialPlatform.BuildingBlocks.Tests;

public sealed class SchemaPhysicalDriftGuardTests : IDisposable
{
    private const string TableName = "schema_drift_guard_probe";
    private const string IndexName = "ix_collaboration_remote_assistance_voice_call_conversation_created";
    private static readonly string[] RequiredColumns = ["id", "value"];
    private static readonly string[] RequiredIndexes = [IndexName];
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"industrial-platform-schema-drift-{Guid.NewGuid():N}.db");
    private readonly SqlSugarDbContext _dbContext;

    public SchemaPhysicalDriftGuardTests()
    {
        Batteries_V2.Init();
        _dbContext = new SqlSugarDbContext(Options.Create(new SqlSugarOptions
        {
            ConnectionString = $"Data Source={_databasePath};Pooling=False",
            DbType = DbType.Sqlite,
            IsAutoCloseConnection = true,
        }));
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        if (File.Exists(_databasePath))
            File.Delete(_databasePath);
    }

    [Fact]
    public void Validate_keeps_exact_sqlite_index_names_for_long_names()
    {
        CreateTable(withIndex: true);

        SchemaPhysicalDriftGuard.Validate(
            _dbContext.SqlSugar,
            TableName,
            RequiredColumns,
            RequiredIndexes);
    }

    [Fact]
    public void Validate_rejects_a_truly_missing_sqlite_index()
    {
        CreateTable(withIndex: false);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            SchemaPhysicalDriftGuard.Validate(
                _dbContext.SqlSugar,
                TableName,
                RequiredColumns,
                RequiredIndexes));

        Assert.Contains(IndexName, exception.Message, StringComparison.Ordinal);
    }

    private void CreateTable(bool withIndex)
    {
        _dbContext.SqlSugar.Ado.ExecuteCommand(
            $"CREATE TABLE \"{TableName}\" (\"id\" INTEGER PRIMARY KEY, \"value\" TEXT NOT NULL);");
        if (withIndex)
            _dbContext.SqlSugar.Ado.ExecuteCommand($"CREATE INDEX \"{IndexName}\" ON \"{TableName}\" (\"value\");");
    }
}
