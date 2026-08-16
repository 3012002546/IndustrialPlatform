namespace IndustrialPlatform.SharedKernel.Topology;

/// <summary>
/// 解析后的数据库物理目标。逻辑身份稳定,物理身份随拓扑变化。
/// </summary>
/// <param name="EnvironmentName">环境名。</param>
/// <param name="Mode">物理拓扑模式。</param>
/// <param name="ServiceKey">目标服务的稳定服务键,如 <c>systemdata</c>。</param>
/// <param name="Provider">目标提供程序。</param>
/// <param name="LogicalDatabaseName">稳定逻辑库名,如 <c>systemdata_db</c>。</param>
/// <param name="PhysicalDatabaseName">解析出的物理库名或文件。</param>
/// <param name="IsSharedPhysicalDatabase">是否与其他服务共享同一物理库。</param>
public sealed record ResolvedDatabaseTarget(
    string EnvironmentName,
    DatabaseTopologyMode Mode,
    string ServiceKey,
    DatabaseProvider Provider,
    string LogicalDatabaseName,
    string PhysicalDatabaseName,
    bool IsSharedPhysicalDatabase);
