using IndustrialPlatform.SystemData.Domain.Topology;

namespace IndustrialPlatform.SystemData.Application.DatabaseOrchestration;

/// <summary>
/// 可信数据库拓扑提供端口。Application 不引用 Infrastructure;
/// 由基础设施实现(<c>ConfigurationDatabaseTopologyProvider</c> 读 <c>DatabaseTopologyOptions</c>)。
/// 环境与拓扑身份只来自该可信源,请求不含环境标识(防客户端伪造)。
/// </summary>
public interface IDatabaseTopologyProvider
{
    /// <summary>获取当前受信任的数据库拓扑。</summary>
    DatabaseTopology GetTopology();
}
