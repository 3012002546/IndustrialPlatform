using IndustrialPlatform.Web.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace IndustrialPlatform.BuildingBlocks.Tests;

public sealed class DevelopmentInfrastructureConfigurationTests
{
    private const string SharedDatabase = "industrial_platform_dev";

    [Fact]
    public void ResolveLocalConfigurationPath_FindsUnifiedBackendConfigurationFromNestedProject()
    {
        var root = Path.Combine(Path.GetTempPath(), $"industrial-platform-root-{Guid.NewGuid():N}");
        var backend = Path.Combine(root, "src", "backend");
        var project = Path.Combine(backend, "src", "Hosts", "IndustrialPlatform.UnifiedHost");
        var expected = Path.Combine(backend, "appsettings.Development.local.json");
        Directory.CreateDirectory(project);
        File.WriteAllText(expected, "{}");

        try
        {
            var actual = DevelopmentInfrastructureConfiguration.ResolveLocalConfigurationPath(project, null);

            Assert.Equal(expected, actual);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ResolveLocalConfigurationPath_ExplicitPathTakesPrecedence()
    {
        var contentRoot = Path.Combine(Path.GetTempPath(), $"industrial-platform-project-{Guid.NewGuid():N}");
        var explicitPath = Path.Combine(contentRoot, "custom.local.json");
        Directory.CreateDirectory(contentRoot);

        try
        {
            var actual = DevelopmentInfrastructureConfiguration.ResolveLocalConfigurationPath(
                contentRoot,
                "custom.local.json");

            Assert.Equal(explicitPath, actual);
        }
        finally
        {
            Directory.Delete(contentRoot, recursive: true);
        }
    }

    [Fact]
    public void AddDevelopmentInfrastructure_MissingUnifiedConfigurationFails()
    {
        var contentRoot = Path.Combine(Path.GetTempPath(), $"industrial-platform-project-{Guid.NewGuid():N}");
        Directory.CreateDirectory(contentRoot);

        try
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                EnvironmentName = "Development",
                ContentRootPath = contentRoot,
            });
            builder.Configuration["IndustrialPlatform:DevelopmentInfrastructureMode"] = "Unified";

            var exception = Assert.Throws<InvalidOperationException>(() =>
                builder.AddOptionalLocalDevelopmentInfrastructure(DevelopmentService.Identity));

            Assert.Contains("appsettings.Development.local.json", exception.Message);
        }
        finally
        {
            Directory.Delete(contentRoot, recursive: true);
        }
    }

    [Fact]
    public void AddDevelopmentInfrastructure_ExplicitSqliteModeDoesNotRequireUnifiedConfiguration()
    {
        var root = Path.Combine(Path.GetTempPath(), $"industrial-platform-root-{Guid.NewGuid():N}");
        var backend = Path.Combine(root, "src", "backend");
        var contentRoot = Path.Combine(backend, "src", "Hosts", "IndustrialPlatform.UnifiedHost");
        Directory.CreateDirectory(contentRoot);
        var source = WriteLocalConfiguration(enabled: true);
        File.Copy(source, Path.Combine(backend, "appsettings.Development.local.json"));

        try
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                EnvironmentName = "Development",
                ContentRootPath = contentRoot,
            });
            builder.Configuration["IndustrialPlatform:DevelopmentInfrastructureMode"] = "Sqlite";

            var loaded = builder.AddOptionalLocalDevelopmentInfrastructure(DevelopmentService.Identity);

            Assert.False(loaded);
        }
        finally
        {
            File.Delete(source);
            Directory.Delete(root, recursive: true);
        }
    }

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
    public void EnabledLocalFileMapsIdentityInfrastructureToSharedDatabase()
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
            Assert.Contains($"Database={SharedDatabase}", configuration["SqlSugar:ConnectionString"]);
            Assert.Equal("100.64.0.10:6379,password=redis-secret,defaultDatabase=2", configuration["Redis:ConnectionString"]);
            Assert.Equal("http://100.64.0.10:5341", configuration["Serilog:Seq:ServerUrl"]);
            Assert.Equal("true", configuration["Serilog:Seq:Enabled"]);
            Assert.Equal("100.64.0.10", configuration["RabbitMQ:Host"]);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void EnabledLocalFileMapsReferenceDataInfrastructureToSharedDatabase()
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
            Assert.Contains($"Database={SharedDatabase}", configuration["SqlSugar:ConnectionString"]);
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

    [Fact]
    public void EnabledLocalFileMapsUnifiedHostRabbitMqInfrastructure()
    {
        var configuration = CreateDefaults();
        var path = WriteLocalConfiguration(enabled: true);

        try
        {
            var loaded = DevelopmentInfrastructureConfiguration.Apply(
                configuration,
                path,
                DevelopmentService.UnifiedHost);

            Assert.True(loaded);
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

    [Fact]
    public void EnabledLocalFileMapsSystemDataRabbitMqInfrastructure()
    {
        var configuration = CreateDefaults();
        var path = WriteLocalConfiguration(enabled: true);

        try
        {
            var loaded = DevelopmentInfrastructureConfiguration.Apply(
                configuration,
                path,
                DevelopmentService.SystemData);

            Assert.True(loaded);
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

    [Theory]
    [InlineData(DevelopmentService.Identity)]
    [InlineData(DevelopmentService.ReferenceData)]
    [InlineData(DevelopmentService.SystemData)]
    public void SharedModeResolvesSamePhysicalDatabaseForAllServices(DevelopmentService service)
    {
        var configuration = CreateDefaults();
        var path = WriteLocalConfiguration(enabled: true);

        try
        {
            var loaded = DevelopmentInfrastructureConfiguration.Apply(configuration, path, service);

            Assert.True(loaded);
            Assert.Contains($"Database={SharedDatabase}", configuration["SqlSugar:ConnectionString"]);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void PerServiceModeResolvesMappedPhysicalDatabasePerService()
    {
        var configuration = CreateDefaults();
        var path = WriteLocalConfiguration(
            enabled: true,
            topology: PerServiceTopology());

        try
        {
            var loaded = DevelopmentInfrastructureConfiguration.Apply(
                configuration,
                path,
                DevelopmentService.Identity);

            Assert.True(loaded);
            Assert.Contains("Database=identity_prod", configuration["SqlSugar:ConnectionString"]);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void SharedModeMissingDatabaseName_Fails()
    {
        var configuration = CreateDefaults();
        var path = WriteLocalConfiguration(enabled: true, topology: MissingSharedNameTopology());

        try
        {
            var exception = Assert.Throws<InvalidOperationException>(() =>
                DevelopmentInfrastructureConfiguration.Apply(configuration, path, DevelopmentService.Identity));

            Assert.Contains("SharedDatabaseName", exception.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void PerServiceModeMissingServiceMapping_Fails()
    {
        var configuration = CreateDefaults();
        var path = WriteLocalConfiguration(enabled: true, topology: MissingMappingTopology());

        try
        {
            var exception = Assert.Throws<InvalidOperationException>(() =>
                DevelopmentInfrastructureConfiguration.Apply(configuration, path, DevelopmentService.Identity));

            Assert.Contains("ServiceDatabases", exception.Message);
            Assert.Contains("identity", exception.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void InvalidMode_Fails()
    {
        var configuration = CreateDefaults();
        var path = WriteLocalConfiguration(enabled: true, topology: InvalidModeTopology());

        try
        {
            var exception = Assert.Throws<InvalidOperationException>(() =>
                DevelopmentInfrastructureConfiguration.Apply(configuration, path, DevelopmentService.Identity));

            Assert.Contains("Mode", exception.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string SharedTopology() => $$"""
        {
          "Mode": "Shared",
          "SharedDatabaseName": "{{SharedDatabase}}",
          "SharedSqliteFile": "industrial-platform.db",
          "ServiceDatabases": {}
        }
        """;

    private static string PerServiceTopology() => """
        {
          "Mode": "PerService",
          "ServiceDatabases": {
            "identity": "identity_prod",
            "referencedata": "reference_prod",
            "systemdata": "systemdata_prod"
          }
        }
        """;

    private static string MissingSharedNameTopology() => """
        {
          "Mode": "Shared",
          "SharedDatabaseName": "",
          "ServiceDatabases": {}
        }
        """;

    private static string MissingMappingTopology() => """
        {
          "Mode": "PerService",
          "ServiceDatabases": {
            "referencedata": "reference_prod"
          }
        }
        """;

    private static string InvalidModeTopology() => """
        {
          "Mode": 99,
          "SharedDatabaseName": "x",
          "ServiceDatabases": {}
        }
        """;

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

    private static string WriteLocalConfiguration(bool enabled, string? topology = null)
    {
        topology ??= SharedTopology();
        var path = Path.Combine(Path.GetTempPath(), $"industrial-platform-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, $$"""
            {
              "RemoteDevelopment": {
                "Enabled": {{enabled.ToString().ToLowerInvariant()}},
                "Host": "100.64.0.10",
                "PostgreSql": {
                  "Port": 5432,
                  "UserName": "db-user",
                  "Password": "db-secret"
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
              },
              "DatabaseTopology": {{topology}}
            }
            """);
        return path;
    }
}
