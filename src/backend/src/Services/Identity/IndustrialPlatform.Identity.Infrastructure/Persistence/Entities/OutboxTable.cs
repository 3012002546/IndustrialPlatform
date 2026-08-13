using SqlSugar;

namespace IndustrialPlatform.Identity.Infrastructure.Persistence.Entities;

/// <summary>
/// identity_outbox 表的持久化模型(§20,蛇形列名)。
/// 独立于公共实体列:Outbox 只记录事件信封的完整可交付副本,不承载业务生命周期字段;
/// <see cref="PublishedOn"/> 为空表示尚未成功发布(发布至少一次),<see cref="RetryCount"/>/<see cref="LastError"/>
/// 记录发布重试状态供运维排查。索引 <c>ix_outbox_publish (published_on, event_created_time)</c> 支撑待发布扫描。
/// </summary>
[SugarTable("identity_outbox")]
public sealed class OutboxTable
{
    [SugarColumn(ColumnName = "event_id", IsPrimaryKey = true)]
    public Guid EventId { get; set; }

    [SugarColumn(ColumnName = "event_type")]
    public string EventType { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "event_version")]
    public int EventVersion { get; set; }

    [SugarColumn(ColumnName = "payload")]
    public string Payload { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "event_created_time")]
    public DateTimeOffset EventCreatedTime { get; set; }

    [SugarColumn(ColumnName = "published_on")]
    public DateTimeOffset? PublishedOn { get; set; }

    [SugarColumn(ColumnName = "retry_count")]
    public int RetryCount { get; set; }

    [SugarColumn(ColumnName = "last_error")]
    public string? LastError { get; set; }
}
