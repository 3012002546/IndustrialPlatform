using IndustrialPlatform.SystemData.Contracts.PlatformHealth;

namespace IndustrialPlatform.SystemData.Application.PlatformHealth;

/// <summary>仅查询该租户服务目录允许看到的最新摘要。</summary>
public interface IPlatformHealthQuery
{
    Task<PlatformHealthSummary> GetSummaryAsync(string tenantNId, string? serviceNId, CancellationToken cancellationToken);
}

/// <summary>基础设施最新检查快照，不涉及历史表或探针配置CRUD。</summary>
public interface IPlatformHealthSnapshot
{
    IReadOnlyList<PlatformHealthCheckDto> Read(string serviceNId);
}
