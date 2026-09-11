using IndustrialPlatform.EventBus.Events;

namespace IndustrialPlatform.Collaboration.Contracts;

/// <summary>跨实例协作实时投递事件。Payload 保留在协作 outbox 的版本化 JSON 中。</summary>
public sealed class CollaborationRealtimeIntegrationEvent : IntegrationEvent
{
    public string TenantNId { get; init; } = string.Empty;
    public string SourceEventType { get; init; } = string.Empty;
    public string Payload { get; init; } = "{}";

    public override string EventType => "collaboration.realtime.dispatch.v1";
}
