using IndustrialPlatform.SystemData.Application.DatabaseOrchestration;
using IndustrialPlatform.SharedKernel.Topology;
using IndustrialPlatform.SystemData.Infrastructure.Topology;
using Microsoft.Extensions.Options;

namespace IndustrialPlatform.SystemData.Infrastructure.Topology;

/// <summary>
/// 从受信任配置(<c>DatabaseTopologyOptions</c>)提供当前数据库拓扑。
/// 环境与拓扑身份只来自该可信源,请求不含环境标识(防客户端伪造);
/// 解析规则由 <see cref="DatabaseTopologyResolver"/> 在应用层应用。
/// </summary>
internal sealed class ConfigurationDatabaseTopologyProvider : IDatabaseTopologyProvider
{
    private readonly DatabaseTopologyOptions _options;

    public ConfigurationDatabaseTopologyProvider(IOptions<DatabaseTopologyOptions> options)
    {
        _options = options.Value;
    }

    /// <inheritdoc />
    public DatabaseTopology GetTopology() => _options.ToTopology();
}
