using IndustrialPlatform.SystemData.Contracts.Notifications;

namespace IndustrialPlatform.SystemData.Application.Notifications;

public sealed record AnnouncementRecord(
    string TenantNId,
    string AnnouncementNId,
    string Title,
    string Body,
    int Priority,
    string Status,
    IReadOnlyList<string> RecipientUserNIds,
    DateTimeOffset? PublishedOn,
    DateTimeOffset? ExpiresOn,
    string CreatedByUserNId,
    DateTimeOffset CreatedOn,
    DateTimeOffset LastUpdatedOn,
    DateTimeOffset? RevokedOn,
    string? ResourceNId = null,
    string? TargetRoute = null,
    string? IdempotencyKey = null);

public sealed record AnnouncementPageRecord(
    IReadOnlyList<AnnouncementRecord> Items,
    int Page,
    int PageSize,
    long Total);

public sealed record NotificationMessageRecord(
    string TenantNId,
    string NotificationNId,
    string? AnnouncementNId,
    string Kind,
    string Title,
    string Body,
    string SenderUserNId,
    DateTimeOffset CreatedOn,
    DateTimeOffset? ExpiresOn,
    DateTimeOffset? RevokedOn,
    string? ResourceNId = null,
    string? TargetRoute = null,
    string? IdempotencyKey = null);

public sealed record InboxDeliveryRecord(
    string TenantNId,
    string NotificationNId,
    string RecipientUserNId,
    DateTimeOffset DeliveredOn,
    DateTimeOffset? ReadOn,
    DateTimeOffset? ExpiresOn,
    DateTimeOffset? RevokedOn);

public interface INotificationStore
{
    Task<AnnouncementRecord?> GetAnnouncementAsync(string tenantNId, string announcementNId, CancellationToken cancellationToken);
    Task<NotificationMessageRecord?> GetMessageAsync(string tenantNId, string notificationNId, CancellationToken cancellationToken);
    Task<IReadOnlyList<string>> GetMessageRecipientsAsync(string tenantNId, string notificationNId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<string>>([]);
    Task<IReadOnlyList<AnnouncementRecord>> ListAnnouncementsAsync(string tenantNId, string? search, CancellationToken cancellationToken);
    async Task<AnnouncementPageRecord> ListAnnouncementsPageAsync(string tenantNId, string? search, int page, int pageSize, CancellationToken cancellationToken)
    {
        var normalizedPage = Math.Max(page, 1);
        var normalizedPageSize = Math.Clamp(pageSize, 1, 200);
        var items = await ListAnnouncementsAsync(tenantNId, search, cancellationToken);
        return new AnnouncementPageRecord(items.Skip((normalizedPage - 1) * normalizedPageSize).Take(normalizedPageSize).ToArray(), normalizedPage, normalizedPageSize, items.Count);
    }
    Task InsertAnnouncementAsync(AnnouncementRecord announcement, CancellationToken cancellationToken);
    Task UpdateAnnouncementAsync(AnnouncementRecord announcement, CancellationToken cancellationToken);
    Task PublishAnnouncementAsync(AnnouncementRecord announcement, NotificationMessageRecord message, IReadOnlyList<InboxDeliveryRecord> deliveries, CancellationToken cancellationToken);
    Task RevokeAnnouncementAsync(AnnouncementRecord announcement, CancellationToken cancellationToken);
    Task InsertMessageAsync(NotificationMessageRecord message, IReadOnlyList<InboxDeliveryRecord> deliveries, CancellationToken cancellationToken);
    Task<NotificationInboxPageV1> ListInboxAsync(string tenantNId, string userNId, int page, int pageSize, CancellationToken cancellationToken);
    Task<bool> MarkReadAsync(string tenantNId, string userNId, string notificationNId, bool read, CancellationToken cancellationToken);
    Task<int> MarkManyReadAsync(string tenantNId, string userNId, IReadOnlyList<string> notificationNIds, CancellationToken cancellationToken);
    Task<int> ExpireAsync(DateTimeOffset now, CancellationToken cancellationToken);
}

public interface INotificationService
{
    Task<NotificationAnnouncementV1> CreateAnnouncementAsync(string tenantNId, string userNId, CreateAnnouncementRequest request, CancellationToken cancellationToken);
    Task<NotificationAnnouncementV1> UpdateAnnouncementAsync(string tenantNId, string userNId, string announcementNId, UpdateAnnouncementRequest request, CancellationToken cancellationToken);
    Task<NotificationAnnouncementV1> PublishAnnouncementAsync(string tenantNId, string userNId, string announcementNId, CancellationToken cancellationToken);
    Task<NotificationAnnouncementV1> RevokeAnnouncementAsync(string tenantNId, string userNId, string announcementNId, CancellationToken cancellationToken);
    Task<NotificationInboxItemV1> SendSystemMessageAsync(string tenantNId, string userNId, SystemMessageRequest request, CancellationToken cancellationToken);
    Task<NotificationInboxPageV1> GetInboxAsync(string tenantNId, string userNId, int page, int pageSize, CancellationToken cancellationToken);
    Task<bool> MarkReadAsync(string tenantNId, string userNId, string notificationNId, bool read, CancellationToken cancellationToken);
    Task<int> MarkManyReadAsync(string tenantNId, string userNId, IReadOnlyList<string> notificationNIds, CancellationToken cancellationToken);
    Task<IReadOnlyList<NotificationAnnouncementV1>> ListAnnouncementsAsync(string tenantNId, string? search, CancellationToken cancellationToken);
    Task<NotificationAnnouncementPageV1> ListAnnouncementsPageAsync(string tenantNId, string? search, int page, int pageSize, CancellationToken cancellationToken);
}

public interface INotificationNotifier
{
    Task NotifyAsync(string tenantNId, IReadOnlyCollection<string> recipientUserNIds, CancellationToken cancellationToken);
}

/// <summary>通知目标的运行时资源校验；资源撤销后不得继续生成或导航到失效目标。</summary>
public interface INotificationTargetValidator
{
    Task<bool> IsAllowedAsync(string tenantNId, string? resourceNId, string? targetRoute, CancellationToken cancellationToken);
}

public sealed class AllowNotificationTargetValidator : INotificationTargetValidator
{
    public Task<bool> IsAllowedAsync(string tenantNId, string? resourceNId, string? targetRoute, CancellationToken cancellationToken) => Task.FromResult(true);
}

public sealed class NoopNotificationNotifier : INotificationNotifier
{
    public Task NotifyAsync(string tenantNId, IReadOnlyCollection<string> recipientUserNIds, CancellationToken cancellationToken) => Task.CompletedTask;
}
