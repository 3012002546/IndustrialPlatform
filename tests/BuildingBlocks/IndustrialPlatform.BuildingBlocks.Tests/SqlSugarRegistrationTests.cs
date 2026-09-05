using IndustrialPlatform.Infrastructure.Database;
using IndustrialPlatform.Infrastructure.Transaction;
using IndustrialPlatform.SharedKernel.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using SqlSugar;
using Xunit;

namespace IndustrialPlatform.BuildingBlocks.Tests;

public sealed class SqlSugarRegistrationTests
{
    [Fact]
    public void AddSqlSugar_RegistersDbContextUnitOfWorkAndRepository()
    {
        var services = new ServiceCollection();
        services.AddSqlSugar(options =>
        {
            options.ConnectionString = "Host=localhost;Port=5432;Database=test;User ID=test;Password=test";
            options.DbType = DbType.PostgreSQL;
        });

        using var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<SqlSugarDbContext>());
        Assert.NotNull(provider.GetRequiredService<IUnitOfWork>());
        Assert.NotNull(provider.GetRequiredService<IRepository<TestEntity>>());
    }

    [Fact]
    public async Task SqliteDbContextForcesForeignKeysForEveryAutoClosedConnection()
    {
        var path = Path.Combine(Path.GetTempPath(), $"industrial-platform-fk-{Guid.NewGuid():N}.db");
        try
        {
            var services = new ServiceCollection();
            services.AddSqlSugar(options =>
            {
                options.ConnectionString = $"Data Source={path};Pooling=False;Foreign Keys=False";
                options.DbType = DbType.Sqlite;
                options.IsAutoCloseConnection = true;
            });
            using var provider = services.BuildServiceProvider();
            var context = provider.GetRequiredService<SqlSugarDbContext>();

            Assert.Contains("foreign keys=True", context.SqlSugar.CurrentConnectionConfig.ConnectionString,
                StringComparison.OrdinalIgnoreCase);
            Assert.Equal(1, await context.SqlSugar.Ado.GetIntAsync("PRAGMA foreign_keys"));
            await context.SqlSugar.Ado.ExecuteCommandAsync("""
                CREATE TABLE parent (id INTEGER PRIMARY KEY);
                CREATE TABLE child (id INTEGER PRIMARY KEY, parent_id INTEGER NOT NULL REFERENCES parent(id));
                """);
            Assert.Equal(1, await context.SqlSugar.Ado.GetIntAsync("PRAGMA foreign_keys"));

            var exception = await Assert.ThrowsAnyAsync<Exception>(() =>
                context.SqlSugar.Ado.ExecuteCommandAsync("INSERT INTO child(id,parent_id) VALUES(1,999)"));
            Assert.Contains("FOREIGN KEY", exception.ToString(), StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }
}
