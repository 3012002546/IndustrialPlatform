using System.Data.Common;
using System.Globalization;
using IndustrialPlatform.SharedKernel.Topology;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;

namespace IndustrialPlatform.Web.Configuration;

/// <summary>Development 服务枚举,对应各自稳定服务键与逻辑库名。</summary>
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
/// <remarks>
/// 数据库物理名不再由本地文件按服务指定:统一由 <c>DatabaseTopology</c> 配置节
/// (见 <see cref="DatabaseTopologyOptions"/>)经 <see cref="DatabaseTopologyResolver"/>
/// 唯一解析。Shared 模式下 Identity/ReferenceData/SystemData 连接同一物理库;
/// PerService 模式按 <c>ServiceDatabases[ServiceKey]</c> 解析,缺失映射直接启动失败。
/// </remarks>
public static class DevelopmentInfrastructureConfiguration
{
    private const string LocalConfigurationPathKey = "IndustrialPlatform:LocalConfigurationPath";

    private static readonly Dictionary<DevelopmentService, (string Key, string LogicalDatabase)> Services =
        new()
        {
            [DevelopmentService.Identity] = ("identity", "identity_db"),
            [DevelopmentService.ReferenceData] = ("referencedata", "referencedata_db"),
            [DevelopmentService.SystemData] = ("systemdata", "systemdata_db"),
        };

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

        // 数据库物理名唯一来源:DatabaseTopology(解析失败抛明确、脱敏的启动错误)。
        var target = ResolveTarget(configuration, service);

        var sql = new DbConnectionStringBuilder
        {
            ["Host"] = host,
            ["Port"] = PositivePort(postgres, "Port"),
            ["Database"] = target.PhysicalDatabaseName,
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

    /// <summary>
    /// 解析当前服务的 PostgreSQL 物理库目标。复用 <see cref="DatabaseTopologyResolver"/>
    /// 的领域解析规则;启动连接串路径比编排更严格:Shared 缺名、PerService 缺映射、
    /// 非法 Mode 都在启动期明确失败,不允许 PerService 静默回退逻辑名。
    /// 错误消息只含逻辑服务键与配置键,不含地址、账号、密码等敏感值。
    /// </summary>
    private static ResolvedDatabaseTarget ResolveTarget(ConfigurationManager configuration, DevelopmentService service)
    {
        var (serviceKey, logicalDatabaseName) = Services[service];

        var section = configuration.GetSection(DatabaseTopologyOptions.SectionName);
        var options = new DatabaseTopologyOptions();
        section.Bind(options);

        var topology = options.ToTopology();
        switch (topology.Mode)
        {
            case DatabaseTopologyMode.Shared:
                if (string.IsNullOrWhiteSpace(topology.SharedDatabaseName))
                {
                    throw new InvalidOperationException(
                        $"Development 数据库拓扑配置 {DatabaseTopologyOptions.SectionName}:SharedDatabaseName 缺失,无法解析共享物理库。");
                }

                break;
            case DatabaseTopologyMode.PerService:
                if (!topology.ServiceDatabases.TryGetValue(serviceKey, out var mapped)
                    || string.IsNullOrWhiteSpace(mapped))
                {
                    throw new InvalidOperationException(
                        $"Development 数据库拓扑配置 {DatabaseTopologyOptions.SectionName}:ServiceDatabases 缺少服务 {serviceKey} 的物理映射,无法解析物理库。");
                }

                break;
            default:
                throw new InvalidOperationException(
                    $"Development 数据库拓扑配置 {DatabaseTopologyOptions.SectionName}:Mode 非法:{topology.Mode}。");
        }

        return DatabaseTopologyResolver.Resolve(topology, serviceKey, DatabaseProvider.PostgreSQL, logicalDatabaseName);
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
