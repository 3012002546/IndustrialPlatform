using IndustrialPlatform.Web.Configuration;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace IndustrialPlatform.BuildingBlocks.Tests;

public sealed class DevelopmentInfrastructureConfigurationTests
{
    [Fact]
    public void MissingLocalFilePreservesSqliteDefaults()
    {
        var configuration = CreateDefaults();

        var loaded = DevelopmentInfrastructureConfiguration.Apply(
            configuration,
            Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.json"),
            DevelopmentService.Identity);

        Assert.False(loaded);
        Assert.Equal("Sqlite", configuration["SqlSugar:DbType"]);
        Assert.Equal("Data Source=industrial-platform.identity.db", configuration["SqlSugar:ConnectionString"]);
    }

    [Fact]
    public void DisabledLocalFilePreservesSqliteDefaults()
    {
        var configuration = CreateDefaults();
        var path = WriteLocalConfiguration(enabled: false);

        try
        {
            var loaded = DevelopmentInfrastructureConfiguration.Apply(
                configuration,
                path,
                DevelopmentService.Identity);

            Assert.False(loaded);
            Assert.Equal("Sqlite", configuration["SqlSugar:DbType"]);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void EnabledLocalFileMapsIdentityInfrastructure()
    {
        var configuration = CreateDefaults();
        var path = WriteLocalConfiguration(enabled: true);

        try
        {
            var loaded = DevelopmentInfrastructureConfiguration.Apply(
                configuration,
                path,
                DevelopmentService.Identity);

            Assert.True(loaded);
            Assert.Equal("PostgreSQL", configuration["SqlSugar:DbType"]);
            Assert.Contains("Host=100.64.0.10", configuration["SqlSugar:ConnectionString"]);
            Assert.Contains("Database=identity_db", configuration["SqlSugar:ConnectionString"]);
            Assert.Equal("100.64.0.10:6379,password=redis-secret,defaultDatabase=2", configuration["Redis:ConnectionString"]);
            Assert.Equal("http://100.64.0.10:5341", configuration["Serilog:Seq:ServerUrl"]);
            Assert.Equal("true", configuration["Serilog:Seq:Enabled"]);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void EnabledLocalFileMapsReferenceDataInfrastructure()
    {
        var configuration = CreateDefaults();
        var path = WriteLocalConfiguration(enabled: true);

        try
        {
            var loaded = DevelopmentInfrastructureConfiguration.Apply(
                configuration,
                path,
                DevelopmentService.ReferenceData);

            Assert.True(loaded);
            Assert.Contains("Database=reference_db", configuration["SqlSugar:ConnectionString"]);
            Assert.Equal("100.64.0.10", configuration["RabbitMQ:Host"]);
            Assert.Equal("5672", configuration["RabbitMQ:Port"]);
            Assert.Equal("rabbit-user", configuration["RabbitMQ:UserName"]);
            Assert.Equal("rabbit-secret", configuration["RabbitMQ:Password"]);
            Assert.Equal("/development", configuration["RabbitMQ:VirtualHost"]);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static ConfigurationManager CreateDefaults()
    {
        var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["SqlSugar:DbType"] = "Sqlite",
            ["SqlSugar:ConnectionString"] = "Data Source=industrial-platform.identity.db",
            ["Serilog:Seq:Enabled"] = "false",
        });
        return configuration;
    }

    private static string WriteLocalConfiguration(bool enabled)
    {
        var path = Path.Combine(Path.GetTempPath(), $"industrial-platform-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, $$"""
            {
              "RemoteDevelopment": {
                "Enabled": {{enabled.ToString().ToLowerInvariant()}},
                "Host": "100.64.0.10",
                "PostgreSql": {
                  "Port": 5432,
                  "UserName": "db-user",
                  "Password": "db-secret",
                  "IdentityDatabase": "identity_db",
                  "ReferenceDataDatabase": "reference_db"
                },
                "Redis": {
                  "Port": 6379,
                  "Password": "redis-secret",
                  "DefaultDatabase": 2
                },
                "RabbitMq": {
                  "Enabled": true,
                  "Port": 5672,
                  "UserName": "rabbit-user",
                  "Password": "rabbit-secret",
                  "VirtualHost": "/development"
                },
                "Seq": {
                  "Enabled": true,
                  "Scheme": "http",
                  "Port": 5341,
                  "ApiKey": "seq-key"
                }
              }
            }
            """);
        return path;
    }
}
