using IndustrialPlatform.SystemData.Application.DatabaseOrchestration;
using IndustrialPlatform.SystemData.Application.DatabaseOrchestration.Runner;
using IndustrialPlatform.SystemData.Domain.Topology;

namespace IndustrialPlatform.SystemData.Infrastructure.DatabaseOrchestration.Runner;

/// <summary>
/// 目标数据库适配器路由:按目标提供程序(PostgreSQL/Sqlite)把 Runner 端口调用分派到具体适配器。
/// 编排核心只依赖端口,不感知驱动;provider 不受支持映射 SD_DB_PROVIDER_UNSUPPORTED。
/// </summary>
public sealed class DatabaseTargetAdapterRouter : ITargetDatabaseInspector, ITargetDatabaseProvisioner, IMigrationExecutor, ITargetDatabaseAdvisoryLock
{
    private readonly PostgreSqlTargetDatabaseAdapter _postgreSql;
    private readonly SqliteTargetDatabaseAdapter _sqlite;

    /// <summary>初始化适配器路由。</summary>
    public DatabaseTargetAdapterRouter(
        PostgreSqlTargetDatabaseAdapter postgreSql,
        SqliteTargetDatabaseAdapter sqlite)
    {
        ArgumentNullException.ThrowIfNull(postgreSql);
        ArgumentNullException.ThrowIfNull(sqlite);
        _postgreSql = postgreSql;
        _sqlite = sqlite;
    }

    /// <inheritdoc />
    public Task<DatabaseTargetInspection> InspectAsync(
        ResolvedDatabaseTarget target,
        DatabaseTargetCredentials credentials,
        string moduleKey,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        return AdapterFor<ITargetDatabaseInspector>(target.Provider.ToString())
            .InspectAsync(target, credentials, moduleKey, cancellationToken);
    }

    /// <inheritdoc />
    public Task<DatabaseProvisionOutcome> EnsureDatabaseAsync(
        ResolvedDatabaseTarget target,
        TargetDatabaseConnection admin,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        return AdapterFor<ITargetDatabaseProvisioner>(target.Provider.ToString())
            .EnsureDatabaseAsync(target, admin, cancellationToken);
    }

    /// <inheritdoc />
    public Task<ProvisionedRoles> EnsureRolesAsync(
        ResolvedDatabaseTarget target,
        TargetDatabaseConnection admin,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        return AdapterFor<ITargetDatabaseProvisioner>(target.Provider.ToString())
            .EnsureRolesAsync(target, admin, cancellationToken);
    }

    /// <inheritdoc />
    public Task<MigrationExecutionResult> ApplyAsync(
        ResolvedDatabaseTarget target,
        DatabaseMigrationArtifact artifact,
        TargetDatabaseConnection migrator,
        string moduleKey,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        return AdapterFor<IMigrationExecutor>(target.Provider.ToString())
            .ApplyAsync(target, artifact, migrator, moduleKey, cancellationToken);
    }

    /// <inheritdoc />
    public Task<IDisposable?> AcquireAsync(
        DatabaseTargetLockKey key,
        TargetDatabaseConnection connection,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(key);
        return AdapterFor<ITargetDatabaseAdvisoryLock>(key.Provider)
            .AcquireAsync(key, connection, timeout, cancellationToken);
    }

    private T AdapterFor<T>(string provider) => provider switch
    {
        "PostgreSQL" => (T)(object)_postgreSql,
        "Sqlite" => (T)(object)_sqlite,
        _ => throw new ProviderUnsupportedException(provider),
    };
}
