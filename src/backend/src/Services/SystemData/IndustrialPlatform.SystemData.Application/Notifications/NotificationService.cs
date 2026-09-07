using IndustrialPlatform.SystemData.Application.IdentityDirectory;
using IndustrialPlatform.SystemData.Application.Administration;
using IndustrialPlatform.SystemData.Application.Auditing;
using IndustrialPlatform.SystemData.Application.Pf04;
using IndustrialPlatform.SystemData.Contracts.Administration;
using IndustrialPlatform.SystemData.Contracts.Notifications;
using IndustrialPlatform.SystemData.Domain.Notifications;

namespace IndustrialPlatform.SystemData.Application.Notifications;

public sealed class NotificationService : INotificationService
{
    private readonly INotificationStore _store;
    private readonly IIdentityUserDirectory _directory;
    private readonly TimeProvider _clock;
    private readonly INotificationNotifier _notifier;
    private readonly INotificationTargetValidator _targetValidator;
    private readonly ILocalAuditCommand _audit;
    private readonly ISystemDataWriteTransaction _transaction;

    public NotificationService(INotificationStore store, IIdentityUserDirectory directory, TimeProvider clock, INotificationNotifier? notifier = null, INotificationTargetValidator? targetValidator = null, ILocalAuditCommand? audit = null, ISystemDataWriteTransaction? transaction = null)
    {
        _store = store;
        _directory = directory;
        _clock = clock;
        _notifier = notifier ?? new NoopNotificationNotifier();
        _targetValidator = targetValidator ?? new AllowNotificationTargetValidator();
        _audit = audit ?? new NoopLocalAuditCommand();
        _transaction = transaction ?? new NoopSystemDataWriteTransaction();
    }

    public async Task<NotificationAnnouncementV1> CreateAnnouncementAsync(string tenantNId, string userNId, CreateAnnouncementRequest request, CancellationToken cancellationToken)
    {
        var requestedId = string.IsNullOrWhiteSpace(request.AnnouncementNId) ? null : request.AnnouncementNId.Trim();
        var requestedKey = request.IdempotencyKey?.Trim();
        await EnsureTargetAllowedAsync(tenantNId, request.ResourceNId, request.TargetRoute, cancellationToken);
        AnnouncementRecord? existing = requestedId is null
            ? (string.IsNullOrWhiteSpace(requestedKey)
                ? null
                : (await _store.ListAnnouncementsAsync(tenantNId, null, cancellationToken)).FirstOrDefault(value => string.Equals(value.IdempotencyKey, requestedKey, StringComparison.Ordinal)))
            : await _store.GetAnnouncementAsync(tenantNId, requestedId, cancellationToken);
        if (existing is not null)
        {
            var existingRequestRecipients = await ValidateRecipientsAsync(tenantNId, request.RecipientUserNIds, cancellationToken);
            if (AnnouncementMatches(existing, request, existingRequestRecipients)) return ToContract(existing);
            throw Error("NOTIFICATION_IDEMPOTENCY_CONFLICT", "公告幂等键已绑定不同内容。", 409);
        }
        var recipients = await ValidateRecipientsAsync(tenantNId, request.RecipientUserNIds, cancellationToken);
        var now = _clock.GetUtcNow();
        var announcement = new AnnouncementRecord(tenantNId, Normalize(request.AnnouncementNId ?? requestedKey, "ann"), ValidateTitle(request.Title), ValidateBody(request.Body), ValidatePriority(request.Priority), "Draft", recipients, null, request.ExpiresOn, userNId, now, now, null, request.ResourceNId?.Trim(), ValidateRoute(request.TargetRoute), requestedKey);
        var insertedAnnouncement = false;
        try
        {
            await _transaction.ExecuteAsync(async () =>
            {
                await _store.InsertAnnouncementAsync(announcement, cancellationToken);
                insertedAnnouncement = true;
                await _audit.RecordAsync(Audit(tenantNId, userNId, "notification.announcement.create", "Announcement", announcement.AnnouncementNId, null, announcement.Status, null), cancellationToken);
            }, cancellationToken);
        }
        catch (Exception exception) when (!insertedAnnouncement && exception is not OperationCanceledException && (requestedId is not null || !string.IsNullOrWhiteSpace(requestedKey)))
        {
            var raced = requestedId is null
                ? (await _store.ListAnnouncementsAsync(tenantNId, null, cancellationToken)).FirstOrDefault(value => string.Equals(value.IdempotencyKey, requestedKey, StringComparison.Ordinal))
                : await _store.GetAnnouncementAsync(tenantNId, requestedId, cancellationToken);
            if (raced is not null && AnnouncementMatches(raced, request, recipients)) return ToContract(raced);
            throw Error("NOTIFICATION_IDEMPOTENCY_CONFLICT", "公告幂等键已绑定不同内容。", 409);
        }
        return ToContract(announcement);
    }

    public async Task<NotificationAnnouncementV1> UpdateAnnouncementAsync(string tenantNId, string userNId, string announcementNId, UpdateAnnouncementRequest request, CancellationToken cancellationToken)
    {
        var current = await _store.GetAnnouncementAsync(tenantNId, announcementNId, cancellationToken) ?? throw Error("NOTIFICATION_NOT_FOUND", "公告不存在。", 404);
        if (current.Status != "Draft") throw Error("NOTIFICATION_PUBLISHED_IMMUTABLE", "公告发布后不可直接修改。");
        await EnsureTargetAllowedAsync(tenantNId, request.ResourceNId ?? current.ResourceNId, request.TargetRoute ?? current.TargetRoute, cancellationToken);
        var recipients = request.RecipientUserNIds is null ? current.RecipientUserNIds : await ValidateRecipientsAsync(tenantNId, request.RecipientUserNIds, cancellationToken);
        var updated = current with
        {
            Title = request.Title is null ? current.Title : ValidateTitle(request.Title),
            Body = request.Body is null ? current.Body : ValidateBody(request.Body),
            Priority = request.Priority is null ? current.Priority : ValidatePriority(request.Priority),
            RecipientUserNIds = recipients,
            ExpiresOn = request.ExpiresOn ?? current.ExpiresOn,
            ResourceNId = request.ResourceNId?.Trim() ?? current.ResourceNId,
            TargetRoute = request.TargetRoute is null ? current.TargetRoute : ValidateRoute(request.TargetRoute),
            LastUpdatedOn = _clock.GetUtcNow()
        };
        await _transaction.ExecuteAsync(async () =>
        {
            await _store.UpdateAnnouncementAsync(updated, cancellationToken);
            await _audit.RecordAsync(Audit(tenantNId, userNId, "notification.announcement.update", "Announcement", announcementNId, current.Status, updated.Status, null), cancellationToken);
        }, cancellationToken);
        return ToContract(updated);
    }

    public async Task<NotificationAnnouncementV1> PublishAnnouncementAsync(string tenantNId, string userNId, string announcementNId, CancellationToken cancellationToken)
    {
        var current = await _store.GetAnnouncementAsync(tenantNId, announcementNId, cancellationToken) ?? throw Error("NOTIFICATION_NOT_FOUND", "公告不存在。", 404);
        if (current.Status == "Published") return ToContract(current);
        if (current.Status == "Revoked") throw Error("NOTIFICATION_REVOKED", "公告已撤回。");
        if (current.ExpiresOn <= _clock.GetUtcNow()) throw Error("NOTIFICATION_EXPIRED", "公告已过期。");
        var recipients = await ValidateRecipientsAsync(tenantNId, current.RecipientUserNIds, cancellationToken);
        await EnsureTargetAllowedAsync(tenantNId, current.ResourceNId, current.TargetRoute, cancellationToken);
        var publishedOn = _clock.GetUtcNow();
        var published = current with { Status = "Published", PublishedOn = publishedOn, LastUpdatedOn = publishedOn };
        var message = new NotificationMessageRecord(tenantNId, Normalize(null, "msg"), announcementNId, "Announcement", current.Title, current.Body, userNId, publishedOn, current.ExpiresOn, null, current.ResourceNId, current.TargetRoute, current.IdempotencyKey);
        var deliveries = recipients.Select(recipient => new InboxDeliveryRecord(tenantNId, message.NotificationNId, recipient, publishedOn, null, current.ExpiresOn, null)).ToArray();
        var publishedPersisted = false;
        try
        {
            await _transaction.ExecuteAsync(async () =>
            {
                await _store.PublishAnnouncementAsync(published, message, deliveries, cancellationToken);
                publishedPersisted = true;
                await _audit.RecordAsync(Audit(tenantNId, userNId, "notification.announcement.publish", "Announcement", announcementNId, current.Status, published.Status, null), cancellationToken);
            }, cancellationToken);
        }
        catch (InvalidOperationException) when (!publishedPersisted)
        {
            var raced = await _store.GetAnnouncementAsync(tenantNId, announcementNId, cancellationToken);
            if (raced?.Status == "Published") return ToContract(raced);
            throw Error("NOTIFICATION_PUBLISH_CONFLICT", "公告发布状态已被其他操作改变。", 409);
        }
        await _notifier.NotifyAsync(tenantNId, recipients, cancellationToken);
        return ToContract(published);
    }

    public async Task<NotificationAnnouncementV1> RevokeAnnouncementAsync(string tenantNId, string userNId, string announcementNId, CancellationToken cancellationToken)
    {
        var current = await _store.GetAnnouncementAsync(tenantNId, announcementNId, cancellationToken) ?? throw Error("NOTIFICATION_NOT_FOUND", "公告不存在。", 404);
        var revoked = current with { Status = "Revoked", RevokedOn = _clock.GetUtcNow(), LastUpdatedOn = _clock.GetUtcNow() };
        await _transaction.ExecuteAsync(async () =>
        {
            await _store.RevokeAnnouncementAsync(revoked, cancellationToken);
            await _audit.RecordAsync(Audit(tenantNId, userNId, "notification.announcement.revoke", "Announcement", announcementNId, current.Status, revoked.Status, null), cancellationToken);
        }, cancellationToken);
        await _notifier.NotifyAsync(tenantNId, current.RecipientUserNIds, cancellationToken);
        return ToContract(revoked);
    }

    public async Task<NotificationInboxItemV1> SendSystemMessageAsync(string tenantNId, string userNId, SystemMessageRequest request, CancellationToken cancellationToken)
    {
        var recipients = await ValidateRecipientsAsync(tenantNId, request.RecipientUserNIds, cancellationToken);
        await EnsureTargetAllowedAsync(tenantNId, request.ResourceNId, request.TargetRoute, cancellationToken);
        var now = _clock.GetUtcNow();
        var message = new NotificationMessageRecord(tenantNId, Normalize(null, "msg"), null, "System", ValidateTitle(request.Title), ValidateBody(request.Body), userNId, now, request.ExpiresOn, null);
        if (!string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            var key = request.IdempotencyKey.Trim();
            var deterministicId = $"msg-{Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes($"{tenantNId}|{key}"))).ToLowerInvariant()[..24]}";
            if (await _store.GetMessageAsync(tenantNId, deterministicId, cancellationToken) is { } existing)
            {
                var existingRecipients = await _store.GetMessageRecipientsAsync(tenantNId, deterministicId, cancellationToken);
                if (MessageMatches(existing, request, recipients, existingRecipients)) return ToInboxContract(existing, PreferredRecipient(existingRecipients, userNId));
                throw Error("NOTIFICATION_IDEMPOTENCY_CONFLICT", "系统消息幂等键已绑定不同内容。", 409);
            }
            message = message with { NotificationNId = deterministicId, IdempotencyKey = key };
        }
        message = message with { ResourceNId = request.ResourceNId?.Trim(), TargetRoute = ValidateRoute(request.TargetRoute) };
        var deliveries = recipients.Select(recipient => new InboxDeliveryRecord(tenantNId, message.NotificationNId, recipient, now, null, request.ExpiresOn, null)).ToArray();
        var insertedMessage = false;
        try
        {
            await _transaction.ExecuteAsync(async () =>
            {
                await _store.InsertMessageAsync(message, deliveries, cancellationToken);
                insertedMessage = true;
                await _audit.RecordAsync(Audit(tenantNId, userNId, "notification.system.send", "NotificationMessage", message.NotificationNId, null, message.Kind, message.IdempotencyKey), cancellationToken);
            }, cancellationToken);
        }
        catch (Exception exception) when (!insertedMessage && exception is not OperationCanceledException && !string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            var raced = await _store.GetMessageAsync(tenantNId, message.NotificationNId, cancellationToken);
            if (raced is not null)
            {
                var racedRecipients = await _store.GetMessageRecipientsAsync(tenantNId, message.NotificationNId, cancellationToken);
                if (MessageMatches(raced, request, recipients, racedRecipients)) return ToInboxContract(raced, PreferredRecipient(racedRecipients, userNId));
            }
            throw Error("NOTIFICATION_IDEMPOTENCY_CONFLICT", "系统消息幂等键已绑定不同内容。", 409);
        }
        await _notifier.NotifyAsync(tenantNId, recipients, cancellationToken);
        return ToInboxContract(message, recipients.Count == 0 ? userNId : recipients[0]);
    }

    public async Task<NotificationInboxPageV1> GetInboxAsync(string tenantNId, string userNId, int page, int pageSize, CancellationToken cancellationToken)
    {
        var result = await _store.ListInboxAsync(tenantNId, userNId, Math.Max(page, 1), Math.Clamp(pageSize, 1, 200), cancellationToken);
        var items = new List<NotificationInboxItemV1>(result.Items.Count);
        foreach (var item in result.Items)
        {
            if (await _targetValidator.IsAllowedAsync(tenantNId, item.ResourceNId, item.TargetRoute, cancellationToken)) items.Add(item);
            else items.Add(item with { ResourceNId = null, TargetRoute = null });
        }
        return result with { Items = items };
    }

    public async Task<bool> MarkReadAsync(string tenantNId, string userNId, string notificationNId, bool read, CancellationToken cancellationToken)
    {
        var changed = false;
        await _transaction.ExecuteAsync(async () =>
        {
            changed = await _store.MarkReadAsync(tenantNId, userNId, notificationNId, true, cancellationToken);
            if (changed)
                await _audit.RecordAsync(Audit(tenantNId, userNId, "notification.inbox.read", "NotificationMessage", notificationNId, "unread", "read", null), cancellationToken);
        }, cancellationToken);
        return changed;
    }

    public async Task<int> MarkManyReadAsync(string tenantNId, string userNId, IReadOnlyList<string> notificationNIds, CancellationToken cancellationToken)
    {
        if (notificationNIds.Count > 200) throw Error("NOTIFICATION_BATCH_LIMIT", "批量已读一次最多处理 200 条通知。");
        var changed = 0;
        await _transaction.ExecuteAsync(async () =>
        {
            changed = await _store.MarkManyReadAsync(tenantNId, userNId, notificationNIds, cancellationToken);
            if (changed > 0)
                await _audit.RecordAsync(Audit(tenantNId, userNId, "notification.inbox.batch-read", "NotificationInbox", userNId, $"count={changed}", "read", null), cancellationToken);
        }, cancellationToken);
        return changed;
    }

    public async Task<IReadOnlyList<NotificationAnnouncementV1>> ListAnnouncementsAsync(string tenantNId, string? search, CancellationToken cancellationToken) =>
        (await _store.ListAnnouncementsAsync(tenantNId, search, cancellationToken)).Select(ToContract).ToArray();

    public async Task<NotificationAnnouncementPageV1> ListAnnouncementsPageAsync(string tenantNId, string? search, int page, int pageSize, CancellationToken cancellationToken)
    {
        var result = await _store.ListAnnouncementsPageAsync(tenantNId, search, Math.Max(page, 1), Math.Clamp(pageSize, 1, 200), cancellationToken);
        return new NotificationAnnouncementPageV1
        {
            Items = result.Items.Select(ToContract).ToArray(),
            Page = result.Page,
            PageSize = result.PageSize,
            Total = result.Total
        };
    }

    private async Task<IReadOnlyList<string>> ValidateRecipientsAsync(string tenantNId, IReadOnlyList<string>? recipients, CancellationToken cancellationToken)
    {
        IReadOnlyList<string> normalized;
        try { normalized = NotificationTargetRules.Normalize(recipients ?? []); }
        catch (ArgumentException exception)
        {
            var code = exception.Message.Contains("不能超过", StringComparison.Ordinal) ? "NOTIFICATION_AUDIENCE_LIMIT" : "NOTIFICATION_TARGET_INVALID";
            throw Error(code, exception.Message, 422);
        }
        foreach (var recipient in normalized)
        {
            IdentityUserDirectoryEntryV1? user;
            try { user = await _directory.GetAsync(tenantNId, recipient, cancellationToken); }
            catch (IdentityDirectoryUnavailableException) { throw Error("NOTIFICATION_TARGET_UNAVAILABLE", "身份目录不可用，通知未发送。", 503); }
            if (user is null || !string.Equals(user.TenantNId, tenantNId, StringComparison.Ordinal) || !string.Equals(user.Status, "Active", StringComparison.OrdinalIgnoreCase)) throw Error("NOTIFICATION_TARGET_INVALID", "通知收件人无效。", 422);
        }
        return normalized;
    }

    private static string? ValidateRoute(string? route)
    {
        try { return NotificationContentRules.Route(route); }
        catch (ArgumentException exception) { throw Error("NOTIFICATION_TARGET_INVALID", exception.Message, 422); }
    }

    private async Task EnsureTargetAllowedAsync(string tenantNId, string? resourceNId, string? targetRoute, CancellationToken cancellationToken)
    {
        if (!await _targetValidator.IsAllowedAsync(tenantNId, resourceNId, targetRoute, cancellationToken))
            throw Error("NOTIFICATION_TARGET_INVALID", "通知目标资源已失效或无权访问。", 422);
    }

    private static string ValidateTitle(string? value)
    {
        try { return NotificationContentRules.Title(value); }
        catch (ArgumentException exception) { throw Error("NOTIFICATION_TITLE_INVALID", exception.Message, 422); }
    }

    private static string ValidateBody(string? value)
    {
        try { return NotificationContentRules.Body(value); }
        catch (ArgumentException exception) { throw Error("NOTIFICATION_BODY_INVALID", exception.Message, 422); }
    }

    private static int ValidatePriority(int? value)
    {
        var priority = value ?? 0;
        return priority is >= 0 and <= 5 ? priority : throw Error("NOTIFICATION_PRIORITY_INVALID", "通知优先级必须在 0 到 5 之间。", 422);
    }

    private static bool AnnouncementMatches(AnnouncementRecord existing, CreateAnnouncementRequest request, IReadOnlyList<string> recipients) =>
        string.Equals(existing.Title, request.Title?.Trim(), StringComparison.Ordinal)
        && string.Equals(existing.Body, request.Body?.Trim(), StringComparison.Ordinal)
        && existing.Priority == (request.Priority ?? 0)
        && existing.RecipientUserNIds.SequenceEqual(recipients, StringComparer.Ordinal)
        && existing.ExpiresOn == request.ExpiresOn
        && string.Equals(existing.ResourceNId, request.ResourceNId?.Trim(), StringComparison.Ordinal)
        && string.Equals(existing.TargetRoute, ValidateRoute(request.TargetRoute), StringComparison.Ordinal);

    private static bool MessageMatches(NotificationMessageRecord existing, SystemMessageRequest request, IReadOnlyList<string> recipients, IReadOnlyList<string> existingRecipients) =>
        string.Equals(existing.Title, request.Title?.Trim(), StringComparison.Ordinal)
        && string.Equals(existing.Body, request.Body?.Trim(), StringComparison.Ordinal)
        && existing.ExpiresOn == request.ExpiresOn
        && string.Equals(existing.ResourceNId, request.ResourceNId?.Trim(), StringComparison.Ordinal)
        && string.Equals(existing.TargetRoute, ValidateRoute(request.TargetRoute), StringComparison.Ordinal)
        && existingRecipients.SequenceEqual(recipients, StringComparer.Ordinal);

    private static string PreferredRecipient(IReadOnlyList<string> recipients, string fallback)
    {
        for (var index = 0; index < recipients.Count; index++)
            if (string.Equals(recipients[index], fallback, StringComparison.Ordinal)) return fallback;
        return recipients.Count == 0 ? fallback : recipients[0];
    }

    private static string Normalize(string? value, string prefix) => string.IsNullOrWhiteSpace(value) ? $"{prefix}-{Guid.NewGuid():N}" : value.Trim();
    private static Pf04ServiceException Error(string code, string message, int status = 400) => new(code, message, status);

    private static LocalAuditEntry Audit(string tenantNId, string actorUserNId, string action, string objectType, string objectNId, string? before, string? after, string? reason) =>
        new(tenantNId, actorUserNId, action, objectType, objectNId, reason, before, after, Guid.NewGuid().ToString("N"));

    private static NotificationAnnouncementV1 ToContract(AnnouncementRecord announcement) => new()
    {
        TenantNId = announcement.TenantNId,
        AnnouncementNId = announcement.AnnouncementNId,
        Title = announcement.Title,
        Body = announcement.Body,
        Priority = announcement.Priority,
        Status = announcement.Status,
        RecipientCount = announcement.RecipientUserNIds.Count,
        PublishedOn = announcement.PublishedOn,
        ExpiresOn = announcement.ExpiresOn,
        ResourceNId = announcement.ResourceNId,
        TargetRoute = announcement.TargetRoute
    };

    private static NotificationInboxItemV1 ToInboxContract(NotificationMessageRecord message, string recipient) => new()
    {
        NotificationNId = message.NotificationNId,
        Kind = message.Kind,
        Title = message.Title,
        Body = message.Body,
        SenderUserNId = message.SenderUserNId,
        DeliveredOn = message.CreatedOn,
        ExpiresOn = message.ExpiresOn,
        ResourceNId = message.ResourceNId,
        TargetRoute = message.TargetRoute,
    };
}
