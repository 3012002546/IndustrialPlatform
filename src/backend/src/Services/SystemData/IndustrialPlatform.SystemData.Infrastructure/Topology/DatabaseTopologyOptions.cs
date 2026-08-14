using IndustrialPlatform.SystemData.Domain.Topology;

namespace IndustrialPlatform.SystemData.Infrastructure.Topology;

/// <summary>
/// 受信任环境配置中的数据库拓扑选项,绑定 <c>DatabaseTopology</c> 配置节。
/// </summary>
public sealed class DatabaseTopologyOptions
{
    /// <summary>配置节名称。</summary>
    public const string SectionName = "DatabaseTopology";

    /// <summary>环境名(Development/Test/Staging/Production)。</summary>
    public string EnvironmentName { get; set; } = "Development";

    /// <summary>物理拓扑模式,默认 Shared(Development 默认)。</summary>
    public DatabaseTopologyMode Mode { get; set; } = DatabaseTopologyMode.Shared;

    /// <summary>Shared 模式下 PostgreSQL 物理库名。</summary>
    public string? SharedDatabaseName { get; set; }

    /// <summary>Shared 模式下 SQLite 物理库文件。</summary>
    public string? SharedSqliteFile { get; set; }

    /// <summary>PerService 模式下服务到物理库名的映射。</summary>
    public Dictionary<string, string> ServiceDatabases { get; set; } = [];

    /// <summary>转换为领域拓扑描述。</summary>
    public DatabaseTopology ToTopology() =>
        new(EnvironmentName, Mode, SharedDatabaseName, SharedSqliteFile, ServiceDatabases);
}
