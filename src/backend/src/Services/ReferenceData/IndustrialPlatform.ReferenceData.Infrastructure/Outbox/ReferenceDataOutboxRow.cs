using SqlSugar;

namespace IndustrialPlatform.ReferenceData.Infrastructure.Outbox;

internal sealed class ReferenceDataOutboxRow
{
    [SugarColumn(ColumnName = "event_id", IsPrimaryKey = true)] public Guid EventId { get; set; }
    [SugarColumn(ColumnName = "module_key")] public string ModuleKey { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "event_name")] public string EventName { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "aggregate_id")] public Guid AggregateId { get; set; }
    [SugarColumn(ColumnName = "revision")] public long Revision { get; set; }
    [SugarColumn(ColumnName = "payload")] public string Payload { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "status")] public string Status { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "attempt_count")] public int AttemptCount { get; set; }
    [SugarColumn(ColumnName = "next_attempt_on")] public DateTimeOffset? NextAttemptOn { get; set; }
    [SugarColumn(ColumnName = "last_error")] public string? LastError { get; set; }
    [SugarColumn(ColumnName = "created_time")] public DateTimeOffset CreatedTime { get; set; }
    [SugarColumn(ColumnName = "published_on")] public DateTimeOffset? PublishedOn { get; set; }
}
