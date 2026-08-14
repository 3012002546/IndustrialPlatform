using System.Data.Common;
using System.Globalization;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;

namespace IndustrialPlatform.Web.Configuration;

/// <summary>Development services that use separate databases.</summary>
public enum DevelopmentService
{
    Identity,
    ReferenceData,
    SystemData,
}

/// <summary>
/// Maps an ignored local configuration file to the standard infrastructure
/// sections. If the file is missing or remote development is disabled, the
/// checked-in SQLite defaults remain active.
/// </summary>
public static class DevelopmentInfrastructureConfiguration
{
    private const string LocalConfigurationPathKey = "IndustrialPlatform:LocalConfigurationPath";

    public static bool AddOptionalLocalDevelopmentInfrastructure(
        this WebApplicationBuilder builder,
        DevelopmentService service)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var configuredPath = builder.Configuration[LocalConfigurationPathKey];
        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            return false;
        }

        var expandedPath = Environment.ExpandEnvironmentVariables(configuredPath);
        var fullPath = Path.IsPathRooted(expandedPath)
            ? expandedPath
            : Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, expandedPath));

        return Apply(builder.Configuration, fullPath, service);
    }

    public static bool Apply(
        ConfigurationManager configuration,
        string localConfigurationPath,
        DevelopmentService service)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(localConfigurationPath);

        if (!File.Exists(localConfigurationPath))
        {
            return false;
        }

        configuration.AddJsonFile(localConfigurationPath, optional: false, reloadOnChange: false);
        if (!configuration.GetValue<bool>("RemoteDevelopment:Enabled"))
        {
            return false;
        }

        var section = configuration.GetRequiredSection("RemoteDevelopment");
        var host = Required(section, "Host");
        var postgres = section.GetRequiredSection("PostgreSql");
        var redis = section.GetRequiredSection("Redis");
        var database = service switch
        {
            DevelopmentService.Identity => Required(postgres, "IdentityDatabase"),
            DevelopmentService.ReferenceData => Required(postgres, "ReferenceDataDatabase"),
            DevelopmentService.SystemData => Required(postgres, "SystemDataDatabase"),
            _ => throw new InvalidOperationException($"Unsupported development service: {service}."),
        };

        var sql = new DbConnectionStringBuilder
        {
            ["Host"] = host,
            ["Port"] = PositivePort(postgres, "Port"),
            ["Database"] = database,
            ["User ID"] = Required(postgres, "UserName"),
            ["Password"] = Required(postgres, "Password"),
        };

        var overrides = new Dictionary<string, string?>
        {
            ["SqlSugar:DbType"] = "PostgreSQL",
            ["SqlSugar:ConnectionString"] = sql.ConnectionString,
            ["Redis:ConnectionString"] = $"{host}:{PositivePort(redis, "Port")},password={Required(redis, "Password")},defaultDatabase={redis.GetValue<int>("DefaultDatabase")}",
            ["Serilog:Seq:Enabled"] = "false",
        };

        var seq = section.GetSection("Seq");
        if (seq.GetValue<bool>("Enabled"))
        {
            overrides["Serilog:Seq:Enabled"] = "true";
            overrides["Serilog:Seq:ServerUrl"] = $"{Required(seq, "Scheme")}://{host}:{PositivePort(seq, "Port")}";
            overrides["Serilog:Seq:ApiKey"] = seq["ApiKey"];
        }

        var rabbitMq = section.GetSection("RabbitMq");
        if (service == DevelopmentService.ReferenceData && rabbitMq.GetValue<bool>("Enabled"))
        {
            overrides["RabbitMQ:Host"] = host;
            overrides["RabbitMQ:Port"] = PositivePort(rabbitMq, "Port").ToString(CultureInfo.InvariantCulture);
            overrides["RabbitMQ:UserName"] = Required(rabbitMq, "UserName");
            overrides["RabbitMQ:Password"] = Required(rabbitMq, "Password");
            overrides["RabbitMQ:VirtualHost"] = Required(rabbitMq, "VirtualHost");
        }

        configuration.AddInMemoryCollection(overrides);
        return true;
    }

    private static string Required(IConfigurationSection section, string key) =>
        string.IsNullOrWhiteSpace(section[key])
            ? throw new InvalidOperationException($"Local development configuration is missing {section.Path}:{key}.")
            : section[key]!;

    private static int PositivePort(IConfigurationSection section, string key)
    {
        var port = section.GetValue<int>(key);
        return port is > 0 and <= 65535
            ? port
            : throw new InvalidOperationException($"Local development configuration has an invalid {section.Path}:{key}.");
    }
}
