namespace IndustrialPlatform.Identity.Application.Management;

/// <summary>
/// 待写入 Outbox 的集成事件信封(§20)。由领域事件映射为集成事件并序列化后产生;
/// 与业务写入在同一数据库事务提交,保证发布至少一次。
/// </summary>
public sealed record OutboxEnvelope(
    Guid EventId,
    string EventType,
    int EventVersion,
    string Payload,
    DateTimeOffset EventCreatedTime);
