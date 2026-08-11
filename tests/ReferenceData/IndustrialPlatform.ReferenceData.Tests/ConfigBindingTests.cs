using IndustrialPlatform.EventBus.Options;
using IndustrialPlatform.Infrastructure.Caching;
using IndustrialPlatform.Infrastructure.Database;
using IndustrialPlatform.Logging.Options;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace IndustrialPlatform.ReferenceData.Tests;

/// <summary>
/// 验证 appsettings.Development.json 的开发配置可正确绑定到 BuildingBlocks 组件选项,
/// 以及缺失配置时给出清晰失败信息。
/// </summary>
public sealed class ConfigBindingTests
{
    [Fact]
    public void DevelopmentConfigurationBindsSqlSugarOptions()
    {
        using var factory = new WebApplicationFactory<Program>();

        var options = factory.Services.GetRequiredService<IOptions<SqlSugarOptions>>().Value;

        Assert.Equal("Data Source=industrial-platform.referencedata.db", options.ConnectionString);
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
    public void DevelopmentConfigurationBindsRabbitMqOptions()
    {
        using var factory = new WebApplicationFactory<Program>();

        var options = factory.Services.GetRequiredService<IOptions<RabbitMqOptions>>().Value;

        Assert.Equal("localhost", options.Host);
        Assert.Equal(5672, options.Port);
        Assert.Equal("industrial", options.UserName);
        Assert.Equal("sample-dev-password", options.Password);
        Assert.Equal("/", options.VirtualHost);
    }

    [Fact]
    public void DevelopmentConfigurationBindsSerilogOptions()
    {
        using var factory = new WebApplicationFactory<Program>();

        var configuration = factory.Services.GetRequiredService<IConfiguration>();
        var options = configuration.GetSection("Serilog").Get<SerilogOptions>();

        Assert.NotNull(options);
        Assert.Equal("ReferenceData", options.ServiceName);
        Assert.NotNull(options.Seq);
        Assert.False(options.Seq.Enabled);
        Assert.Equal("http://localhost:5341", options.Seq.ServerUrl);
    }

    [Fact]
    public void SqlSugarDbContextMissingConnectionStringThrowsClearArgumentException()
    {
        var options = Options.Create(new SqlSugarOptions());

        var exception = Assert.Throws<ArgumentException>(() => new SqlSugarDbContext(options));

        Assert.Contains("未配置 SqlSugar 连接字符串", exception.Message);
    }
}
