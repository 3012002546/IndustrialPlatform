using IndustrialPlatform.Infrastructure.Database;
using Microsoft.Extensions.Options;
using SqlSugar;
using Xunit;

namespace IndustrialPlatform.BuildingBlocks.Tests;

public sealed class SqlSugarContextScopeTests
{
    [Fact]
    public async Task Configuring_timeout_does_not_share_native_context_between_parallel_operations()
    {
        using var database = new SqlSugarDbContext(Options.Create(new SqlSugarOptions
        { DbType = DbType.Sqlite, ConnectionString = "Data Source=:memory:", CommandTimeoutSeconds = 30 }));
        var contexts = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(async () =>
        {
            await Task.Yield();
            return (database.SqlSugar.ContextID, database.SqlSugar.Ado.CommandTimeOut);
        })));
        Assert.Equal(8, contexts.Select(item => item.ContextID).Distinct().Count());
        Assert.All(contexts, item => Assert.Equal(30, item.CommandTimeOut));
    }
}
