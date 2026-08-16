namespace IndustrialPlatform.SharedKernel.Topology;

/// <summary>数据库物理拓扑模式。</summary>
public enum DatabaseTopologyMode
{
    /// <summary>共享物理数据库:多个服务共用同一物理库,以账本/前缀隔离。</summary>
    Shared,

    /// <summary>每服务独立物理数据库。</summary>
    PerService,
}
