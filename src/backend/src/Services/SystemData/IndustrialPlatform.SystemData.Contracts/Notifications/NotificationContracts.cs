namespace IndustrialPlatform.SystemData.Contracts.Notifications;

public sealed record CreateAnnouncementRequest
{
    public string? AnnouncementNId { get; init; }
    public string? Title { get; init; }
    public string? Body { get; init; }
    public int? Priority { get; init; }
    public IReadOnlyList<string>? RecipientUserNIds { get; init; }
    public DateTimeOffset? ExpiresOn { get; init; }
    public string? ResourceNId { get; init; }
    public string? TargetRoute { get; init; }
    public string? IdempotencyKey { get; init; }
}

public sealed record UpdateAnnouncementRequest
{
    public string? Title { get; init; }
    public string? Body { get; init; }
    public int? Priority { get; init; }
    public IReadOnlyList<string>? RecipientUserNIds { get; init; }
    public DateTimeOffset? ExpiresOn { get; init; }
    public string? ResourceNId { get; init; }
    public string? TargetRoute { get; init; }
    public string? IdempotencyKey { get; init; }
}

public sealed record SystemMessageRequest
{
    public string? Title { get; init; }
    public string? Body { get; init; }
    public IReadOnlyList<string>? RecipientUserNIds { get; init; }
    public DateTimeOffset? ExpiresOn { get; init; }
    public string? ResourceNId { get; init; }
    public string? TargetRoute { get; init; }
    public string? IdempotencyKey { get; init; }
}

public sealed record NotificationAnnouncementV1
{
    public string TenantNId { get; init; } = string.Empty;
    public string AnnouncementNId { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Body { get; init; } = string.Empty;
    public int Priority { get; init; }
    public string Status { get; init; } = string.Empty;
    public int RecipientCount { get; init; }
    public DateTimeOffset? PublishedOn { get; init; }
    public DateTimeOffset? ExpiresOn { get; init; }
    public string? ResourceNId { get; init; }
    public string? TargetRoute { get; init; }
}

public sealed record NotificationInboxItemV1
{
    public string NotificationNId { get; init; } = string.Empty;
    public string Kind { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Body { get; init; } = string.Empty;
    public string SenderUserNId { get; init; } = string.Empty;
    public bool IsRead { get; init; }
    public DateTimeOffset DeliveredOn { get; init; }
    public DateTimeOffset? ReadOn { get; init; }
    public DateTimeOffset? ExpiresOn { get; init; }
    public string? ResourceNId { get; init; }
    public string? TargetRoute { get; init; }
}

public sealed record NotificationInboxPageV1
{
    public IReadOnlyList<NotificationInboxItemV1> Items { get; init; } = [];
    public int Page { get; init; }
    public int PageSize { get; init; }
    public long Total { get; init; }
    public int UnreadCount { get; init; }
}

public sealed record MarkNotificationReadRequest
{
    public bool? Read { get; init; }
}

public sealed record BatchReadNotificationsRequest
{
    public IReadOnlyList<string>? NotificationNIds { get; init; }
}
