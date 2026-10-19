namespace IndustrialPlatform.SystemData.Contracts.PlatformHealth;

/// <summary>单项真实健康检查；来源时间和来源TraceId缺失保持空值。</summary>
public sealed record PlatformHealthCheckDto(string ServiceNId, string CapabilityKey, string Name,
    string Criticality, string ReportedState, string EffectiveState, DateTimeOffset? CheckedOn,
    DateTimeOffset? ReceivedOn, string? ReasonCode, string? ReasonSummary, string? TraceId, string? SourceTraceId);

/// <summary>租户可见服务的最新健康摘要。</summary>
public sealed record PlatformHealthServiceDto(string ServiceNId, string Name, string State,
    DateTimeOffset? LastObservedOn, IReadOnlyList<PlatformHealthCheckDto> Checks,
    int ConfiguredCount, int EnabledCount, int HealthyCount, int DegradedCount,
    int UnavailableCount, int UnknownCount, int DisabledCount);

/// <summary>概览与工作台共同使用的授权服务摘要。</summary>
public sealed record PlatformHealthSummary(string State, DateTimeOffset GeneratedOn,
    IReadOnlyList<PlatformHealthServiceDto> Services, int HealthyCount, int DegradedCount,
    int UnavailableCount, int UnknownCount, int DisabledCount);
