using Microsoft.AspNetCore.Builder;
using Npgsql;

namespace IndustrialPlatform.Collaboration.EmbeddedHost;

/// <summary>
/// 独立协作宿主的唯一私有配置入口：宿主默认配置 → 显式独立配置 → 环境变量/命令行。
/// 独立配置缺失时不向上查找平台 appsettings.Development.local.json。
/// </summary>
public static class StandaloneConfiguration
{
    public const string ConfigurationPathKey = "Standalone:ConfigurationPath";
    public const string EnvironmentConfigurationPath = "INDUSTRIAL_PLATFORM_STANDALONE_CONFIG";

    public static string? Apply(WebApplicationBuilder builder, string[] args)
    {
        var explicitPath = builder.Configuration[ConfigurationPathKey]
            ?? Environment.GetEnvironmentVariable(EnvironmentConfigurationPath);
        var path = string.IsNullOrWhiteSpace(explicitPath)
            ? ResolveDevelopmentPath(builder.Environment.ContentRootPath)
            : Path.GetFullPath(explicitPath);
        if (!File.Exists(path))
            throw new InvalidOperationException($"独立宿主配置文件不存在: {path}。请创建该文件，不能回退到平台配置。");

        builder.Configuration.AddJsonFile(path, optional: false, reloadOnChange: false);
        // CreateBuilder 已有环境变量/命令行源；再次追加使独立文件不会覆盖部署覆盖值。
        builder.Configuration.AddEnvironmentVariables();
        builder.Configuration.AddCommandLine(args);
        ApplyDatabaseProjection(builder, path);
        // The derived projection is still a configuration source, so the
        // documented final precedence remains environment/CLI over the
        // standalone file (a mismatched physical target then fails closed in
        // the initializer's MatchesTarget checks).
        builder.Configuration.AddEnvironmentVariables();
        builder.Configuration.AddCommandLine(args);
        return path;
    }

    public static string ResolveDevelopmentPath(string hostContentRoot) =>
        Path.GetFullPath(Path.Combine(hostContentRoot, "..", "..", "..", "appsettings.Standalone.Development.local.json"));

    private static void ApplyDatabaseProjection(WebApplicationBuilder builder, string configurationPath)
    {
        var database = builder.Configuration.GetSection("Standalone:Database");
        if (!database.Exists())
            throw new InvalidOperationException($"独立宿主配置缺少 Standalone:Database: {configurationPath}");

        var provider = database["Provider"]?.Trim();
        var topologyMode = database["TopologyMode"]?.Trim() ?? "Shared";
        var environmentName = database["EnvironmentName"]?.Trim() ?? builder.Environment.EnvironmentName;
        var projection = new Dictionary<string, string?>
        {
            ["DatabaseTopology:Mode"] = topologyMode,
            ["DatabaseTopology:EnvironmentName"] = environmentName,
            ["DatabaseTopology:IsStandalone"] = "true",
        };

        if (string.Equals(provider, "Sqlite", StringComparison.OrdinalIgnoreCase))
        {
            var file = database["SqliteFile"];
            if (string.IsNullOrWhiteSpace(file))
                throw new InvalidOperationException("Standalone:Database:SqliteFile 未配置。");
            var absoluteFile = Path.IsPathRooted(file)
                ? Path.GetFullPath(file)
                : Path.GetFullPath(Path.Combine(Path.GetDirectoryName(configurationPath)!, file));
            projection["SqlSugar:DbType"] = "Sqlite";
            projection["SqlSugar:ConnectionString"] = $"Data Source={absoluteFile}";
            projection["DatabaseTopology:SharedSqliteFile"] = absoluteFile;
        }
        else if (string.Equals(provider, "PostgreSQL", StringComparison.OrdinalIgnoreCase))
        {
            var connectionString = database["ConnectionString"];
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new InvalidOperationException("Standalone:Database:ConnectionString 未配置。");
            NpgsqlConnectionStringBuilder connection;
            try
            {
                connection = new NpgsqlConnectionStringBuilder(connectionString);
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException("Standalone:Database:ConnectionString 无法解析。", exception);
            }
            if (string.IsNullOrWhiteSpace(connection.Database))
                throw new InvalidOperationException("Standalone:Database:ConnectionString 必须包含实际 Database；不能只依赖 DatabaseName。");
            var configuredDatabase = database["DatabaseName"]?.Trim();
            if (!string.IsNullOrWhiteSpace(configuredDatabase)
                && !string.Equals(configuredDatabase, connection.Database, StringComparison.Ordinal))
                throw new InvalidOperationException("Standalone:Database:DatabaseName 必须与 ConnectionString 的实际 Database 一致。");
            var schema = database["Schema"]?.Trim() ?? "public";
            if (!IsSafeIdentifier(schema))
                throw new InvalidOperationException("Standalone:Database:Schema 只能包含字母、数字和下划线。");
            connection.SearchPath = schema;
            projection["SqlSugar:DbType"] = "PostgreSQL";
            projection["SqlSugar:ConnectionString"] = connection.ConnectionString;
            projection["DatabaseTopology:SharedDatabaseName"] = connection.Database;
            projection["DatabaseTopology:SharedDatabaseSchema"] = schema;
        }
        else
        {
            throw new InvalidOperationException("Standalone:Database:Provider 仅支持 Sqlite 或 PostgreSQL。");
        }

        builder.Configuration.AddInMemoryCollection(projection);
    }

    private static bool IsSafeIdentifier(string value) =>
        value.Length > 0 && value.All(character => char.IsLetterOrDigit(character) || character == '_');
}
