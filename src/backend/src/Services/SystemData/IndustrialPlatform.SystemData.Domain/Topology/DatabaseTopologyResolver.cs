using IndustrialPlatform.SharedKernel.Exceptions;

namespace IndustrialPlatform.SystemData.Domain.Topology;

/// <summary>
/// 将受信任的数据库拓扑与稳定逻辑身份解析为具体物理目标。
/// 规则(见 05 方案 §2.3/§7.1):Shared 仅允许 Development;Shared 必须提供目标名;
/// PerService 优先取显式物理映射,缺失时回退逻辑名(供 SystemData 自身引导)。
/// </summary>
public static class DatabaseTopologyResolver
{
    /// <summary>允许使用 Shared 拓扑的环境。</summary>
    private const string SharedAllowedEnvironment = "Development";

    /// <summary>SystemData 自身的稳定服务键。</summary>
    public const string SystemDataServiceKey = "systemdata";

    /// <summary>SystemData 自身的稳定逻辑库名。</summary>
    public const string SystemDataLogicalDatabaseName = "systemdata_db";

    /// <summary>
    /// 解析指定服务、提供程序与逻辑库名对应的物理目标。
    /// </summary>
    public static ResolvedDatabaseTarget Resolve(
        DatabaseTopology topology,
        string serviceKey,
        DatabaseProvider provider,
        string logicalDatabaseName)
    {
        ArgumentNullException.ThrowIfNull(topology);
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(logicalDatabaseName);

        return topology.Mode switch
        {
            DatabaseTopologyMode.Shared => ResolveShared(topology, serviceKey, provider, logicalDatabaseName),
            DatabaseTopologyMode.PerService => ResolvePerService(topology, serviceKey, provider, logicalDatabaseName),
            _ => throw new ValidationException($"不支持的数据库拓扑模式:{topology.Mode}。"),
        };
    }

    private static ResolvedDatabaseTarget ResolveShared(
        DatabaseTopology topology,
        string serviceKey,
        DatabaseProvider provider,
        string logicalDatabaseName)
    {
        if (!string.Equals(topology.EnvironmentName, SharedAllowedEnvironment, StringComparison.Ordinal))
        {
            throw new BusinessException($"Shared 拓扑仅允许 {SharedAllowedEnvironment} 环境,当前为 {topology.EnvironmentName}。");
        }

        var physicalName = provider switch
        {
            DatabaseProvider.Sqlite => topology.SharedSqliteFile,
            DatabaseProvider.PostgreSQL => topology.SharedDatabaseName,
            _ => throw new ValidationException($"不支持的数据库提供程序:{provider}。"),
        };

        if (string.IsNullOrWhiteSpace(physicalName))
        {
            throw new ValidationException($"Shared 拓扑缺少 {provider} 目标库名。");
        }

        return new ResolvedDatabaseTarget(
            topology.EnvironmentName,
            DatabaseTopologyMode.Shared,
            serviceKey,
            provider,
            logicalDatabaseName,
            physicalName,
            IsSharedPhysicalDatabase: true);
    }

    private static ResolvedDatabaseTarget ResolvePerService(
        DatabaseTopology topology,
        string serviceKey,
        DatabaseProvider provider,
        string logicalDatabaseName)
    {
        var physicalName = topology.ServiceDatabases.TryGetValue(serviceKey, out var mapped)
            ? mapped
            : logicalDatabaseName;

        if (string.IsNullOrWhiteSpace(physicalName))
        {
            throw new ValidationException($"PerService 拓扑缺少服务 {serviceKey} 的物理映射。");
        }

        return new ResolvedDatabaseTarget(
            topology.EnvironmentName,
            DatabaseTopologyMode.PerService,
            serviceKey,
            provider,
            logicalDatabaseName,
            physicalName,
            IsSharedPhysicalDatabase: false);
    }
}
