namespace IndustrialPlatform.EventBus.Events;

/// <summary>文件扫描/删除状态变化事件，供引用方更新其本地文件投影。</summary>
public sealed class FileStatusChangedIntegrationEvent : IntegrationEvent
{
    public string TenantNId { get; init; } = string.Empty;
    public string FileNId { get; init; } = string.Empty;
    public string ScanStatus { get; init; } = string.Empty;
    public bool Restricted { get; init; }
    public string DeletionStatus { get; init; } = string.Empty;
    /// <summary>
    /// 保留为兼容字段，但文件事件不是权威状态快照，消费者不得用它做版本判定。
    /// </summary>
    public int? StateVersion { get; init; }
    public DateTimeOffset ObservedOn { get; init; }

    public override string EventType => "systemdata.file.status-changed.v1";
}
