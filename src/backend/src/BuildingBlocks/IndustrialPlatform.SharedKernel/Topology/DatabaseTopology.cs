namespace IndustrialPlatform.SharedKernel.Topology;

/// <summary>
/// 受信任环境的数据库拓扑描述,由 <see cref="DatabaseTopologyResolver"/> 解析为具体物理目标。
/// 属于跨服务共享语义:Identity/ReferenceData/SystemData 与部署控制面统一使用,
/// 避免每服务重复定义第二套拓扑规则。
/// </summary>
/// <param name="EnvironmentName">环境名(Development/Test/Staging/Production)。</param>
/// <param name="Mode">物理拓扑模式。</param>
/// <param name="SharedDatabaseName">Shared 模式下 PostgreSQL 物理库名。</param>
/// <param name="SharedSqliteFile">Shared 模式下 SQLite 物理库文件。</param>
/// <param name="ServiceDatabases">PerService 模式下服务到物理库名的映射。</param>
public sealed record DatabaseTopology(
    string EnvironmentName,
    DatabaseTopologyMode Mode,
    string? SharedDatabaseName,
    string? SharedSqliteFile,
    IReadOnlyDictionary<string, string> ServiceDatabases);
