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
}
