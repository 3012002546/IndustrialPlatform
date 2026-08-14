using IndustrialPlatform.SystemData.Application.DatabaseOrchestration.Runner;
using IndustrialPlatform.SystemData.Domain.Topology;

namespace IndustrialPlatform.SystemData.Infrastructure.DatabaseOrchestration.Runner;

/// <summary>
/// 目标凭据解析(05 方案 §7.1.5):provision admin / migrator / runtime 均从进程环境变量解析,
/// 控制面绝不保存凭据值。SQLite 替身无需账号,直接以物理文件路径作为连接。
/// 未配置的连接返回 <c>null</c>,由 Runner 在需要时映射 SD_DB_SECRET_UNAVAILABLE(fail-closed)。
/// </summary>
public sealed class EnvironmentCredentialResolver : IDatabaseCredentialResolver
{
    /// <summary>provision admin 环境变量前缀。</summary>
    private const string ProvisionAdminPrefix = "DB_PROVISION_ADMIN";

    /// <summary>migrator 环境变量前缀。</summary>
    private const string MigratorPrefix = "DB_TARGET_MIGRATOR";

    /// <summary>runtime 环境变量前缀。</summary>
    private const string RuntimePrefix = "DB_TARGET_RUNTIME";

    /// <inheritdoc />
    public Task<DatabaseTargetCredentials> ResolveAsync(
        ResolvedDatabaseTarget target,
        bool provisionRequired,
        CancellationToken cancellationToken)
    {
        var admin = provisionRequired ? ReadConnection(target, ProvisionAdminPrefix) : null;
        var migrator = ReadConnection(target, MigratorPrefix);
        var runtime = ReadConnection(target, RuntimePrefix);
        return Task.FromResult(new DatabaseTargetCredentials(admin, migrator, runtime));
    }

    private static TargetDatabaseConnection? ReadConnection(ResolvedDatabaseTarget target, string prefix)
    {
        if (target.Provider == DatabaseProvider.Sqlite)
        {
            // 本地 SQLite 替身:无账号密码,物理库文件即连接目标。
            return new TargetDatabaseConnection(
                "Sqlite",
                string.Empty,
                0,
                target.PhysicalDatabaseName,
                null,
                null,
                null);
        }

        var host = ReadEnv(prefix, "HOST");
        if (host is null)
        {
            return null;
        }

        var port = int.TryParse(ReadEnv(prefix, "PORT"), out var parsed) && parsed > 0 ? parsed : 5432;
        return new TargetDatabaseConnection(
            "PostgreSQL",
            host,
            port,
            ReadEnv(prefix, "DATABASE") ?? target.PhysicalDatabaseName,
            ReadEnv(prefix, "SCHEMA"),
            ReadEnv(prefix, "USER"),
            ReadEnv(prefix, "PASSWORD"));
    }

    private static string? ReadEnv(string prefix, string name) =>
        Environment.GetEnvironmentVariable($"{prefix}_{name}");
}
