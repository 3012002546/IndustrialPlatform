using SqlSugar;

namespace IndustrialPlatform.SystemData.Infrastructure.Persistence.Entities;

[SugarTable("system_data_outbox")]
public sealed class SystemDataOutboxTable
{
    [SugarColumn(ColumnName = "event_id", IsPrimaryKey = true)] public Guid EventId { get; set; }
    [SugarColumn(ColumnName = "event_type")] public string EventType { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "event_version")] public string EventVersion { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "tenant_n_id")] public string TenantNId { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "payload")] public string Payload { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "event_created_time")] public DateTimeOffset EventCreatedTime { get; set; }
    [SugarColumn(ColumnName = "published_on")] public DateTimeOffset? PublishedOn { get; set; }
    [SugarColumn(ColumnName = "next_attempt_on")] public DateTimeOffset? NextAttemptOn { get; set; }
    [SugarColumn(ColumnName = "retry_count")] public int RetryCount { get; set; }
    [SugarColumn(ColumnName = "last_error")] public string? LastError { get; set; }
    [SugarColumn(ColumnName = "dead_on")] public DateTimeOffset? DeadOn { get; set; }
}
