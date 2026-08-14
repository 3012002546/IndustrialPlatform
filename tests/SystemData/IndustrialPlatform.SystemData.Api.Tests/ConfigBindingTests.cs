using IndustrialPlatform.Infrastructure.Caching;
using IndustrialPlatform.Infrastructure.Database;
using IndustrialPlatform.Logging.Options;
using IndustrialPlatform.SystemData.Domain.Topology;
using IndustrialPlatform.SystemData.Infrastructure.Topology;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace IndustrialPlatform.SystemData.Api.Tests;

/// <summary>
/// 验证 appsettings.Development.json 的开发配置可正确绑定到组件与 SystemData 拓扑选项。
/// </summary>
public sealed class ConfigBindingTests
{
    [Fact]
    public void DevelopmentConfigurationBindsSqlSugarOptions()
    {
        using var factory = new WebApplicationFactory<Program>();

        var options = factory.Services.GetRequiredService<IOptions<SqlSugarOptions>>().Value;

        Assert.Equal("Data Source=industrial-platform.systemdata.db", options.ConnectionString);
        Assert.Equal(SqlSugar.DbType.Sqlite, options.DbType);
    }

    [Fact]
    public void DevelopmentConfigurationBindsRedisOptions()
    {
        using var factory = new WebApplicationFactory<Program>();

        var options = factory.Services.GetRequiredService<IOptions<RedisOptions>>().Value;

        Assert.Equal("localhost:6379", options.ConnectionString);
    }

    [Fact]
    public void DevelopmentConfigurationBindsDatabaseTopologyOptions()
    {
        using var factory = new WebApplicationFactory<Program>();

        var options = factory.Services.GetRequiredService<IOptions<DatabaseTopologyOptions>>().Value;

        Assert.Equal("Development", options.EnvironmentName);
        Assert.Equal(DatabaseTopologyMode.Shared, options.Mode);
        Assert.Equal("industrial_platform_dev", options.SharedDatabaseName);
        Assert.Equal("industrial-platform.db", options.SharedSqliteFile);
    }

    [Fact]
    public void DatabaseTopologyOptionsMapsToDomainTopology()
    {
        var options = new DatabaseTopologyOptions
        {
            EnvironmentName = "Production",
            Mode = DatabaseTopologyMode.PerService,
            ServiceDatabases = new Dictionary<string, string> { ["systemdata"] = "systemdata_prod" },
        };

        var topology = options.ToTopology();

        Assert.Equal("Production", topology.EnvironmentName);
        Assert.Equal(DatabaseTopologyMode.PerService, topology.Mode);
        Assert.Equal("systemdata_prod", topology.ServiceDatabases["systemdata"]);
    }

    [Fact]
    public void DevelopmentConfigurationBindsSerilogOptions()
    {
        using var factory = new WebApplicationFactory<Program>();

        var configuration = factory.Services.GetRequiredService<IConfiguration>();
        var options = configuration.GetSection("Serilog").Get<SerilogOptions>();

        Assert.NotNull(options);
        Assert.Equal("SystemData", options.ServiceName);
        Assert.NotNull(options.Seq);
        Assert.False(options.Seq.Enabled);
    }
}
