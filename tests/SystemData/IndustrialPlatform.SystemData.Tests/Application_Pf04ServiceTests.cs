using IndustrialPlatform.SystemData.Application.IdentityDirectory;
using IndustrialPlatform.SystemData.Application.Notifications;
using IndustrialPlatform.SystemData.Application.Pf04;
using IndustrialPlatform.SystemData.Contracts.Administration;
using IndustrialPlatform.SystemData.Contracts.Notifications;

namespace IndustrialPlatform.SystemData.Tests;

public sealed class Pf04ServiceTests
{
    [Fact]
    public async Task Announcement_Create_with_same_id_is_idempotent()
    {
        var store = new NotificationStoreStub();
        var service = new NotificationService(store, new DirectoryStub(), TimeProvider.System);
        var request = new CreateAnnouncementRequest { AnnouncementNId = "ann-1", Title = "标题", Body = "正文", RecipientUserNIds = ["user-1"] };

        var first = await service.CreateAnnouncementAsync("tenant-1", "admin", request, CancellationToken.None);
        var retry = await service.CreateAnnouncementAsync("tenant-1", "admin", request, CancellationToken.None);

        Assert.Equal(first.AnnouncementNId, retry.AnnouncementNId);
        Assert.Equal(1, store.InsertedAnnouncements);
    }

    [Fact]
    public async Task Announcement_Publish_revalidates_targets_before_delivery()
    {
        var directory = new DirectoryStub { Status = "Active" };
        var store = new NotificationStoreStub();
        var service = new NotificationService(store, directory, TimeProvider.System);
        await service.CreateAnnouncementAsync("tenant-1", "admin", new CreateAnnouncementRequest { AnnouncementNId = "ann-2", Title = "标题", Body = "正文", RecipientUserNIds = ["user-1"] }, CancellationToken.None);
        directory.Status = "Disabled";

        var exception = await Assert.ThrowsAsync<Pf04ServiceException>(() => service.PublishAnnouncementAsync("tenant-1", "admin", "ann-2", CancellationToken.None));

        Assert.Equal("NOTIFICATION_TARGET_INVALID", exception.Code);
        Assert.Equal(0, store.PublishCount);
    }

    [Fact]
    public async Task Announcement_idempotency_key_rejects_different_payload()
    {
        var store = new NotificationStoreStub();
        var service = new NotificationService(store, new DirectoryStub(), TimeProvider.System);
        var request = new CreateAnnouncementRequest { IdempotencyKey = "key-1", Title = "标题", Body = "正文", RecipientUserNIds = ["user-1"] };

        await service.CreateAnnouncementAsync("tenant-1", "admin", request, CancellationToken.None);
        var exception = await Assert.ThrowsAsync<Pf04ServiceException>(() => service.CreateAnnouncementAsync("tenant-1", "admin", request with { Body = "不同正文" }, CancellationToken.None));

        Assert.Equal("NOTIFICATION_IDEMPOTENCY_CONFLICT", exception.Code);
        Assert.Equal(1, store.InsertedAnnouncements);
    }

    [Fact]
    public async Task System_message_idempotency_key_is_payload_bound()
    {
        var store = new NotificationStoreStub();
        var service = new NotificationService(store, new DirectoryStub(), TimeProvider.System);
        var request = new SystemMessageRequest { IdempotencyKey = "msg-key", Title = "标题", Body = "正文", RecipientUserNIds = ["user-1"] };

        var first = await service.SendSystemMessageAsync("tenant-1", "admin", request, CancellationToken.None);
        var retry = await service.SendSystemMessageAsync("tenant-1", "admin", request, CancellationToken.None);
        var exception = await Assert.ThrowsAsync<Pf04ServiceException>(() => service.SendSystemMessageAsync("tenant-1", "admin", request with { Body = "不同正文" }, CancellationToken.None));

        Assert.Equal(first.NotificationNId, retry.NotificationNId);
        Assert.Equal("NOTIFICATION_IDEMPOTENCY_CONFLICT", exception.Code);
        Assert.Equal(1, store.InsertedMessages);
    }

    [Fact]
    public async Task MarkRead_is_monotonic_and_cannot_unread()
    {
        var store = new NotificationStoreStub();
        var service = new NotificationService(store, new DirectoryStub(), TimeProvider.System);

        Assert.True(await service.MarkReadAsync("tenant-1", "user-1", "msg-1", false, CancellationToken.None));

        Assert.True(store.LastReadValue);
    }

    private sealed class DirectoryStub : IIdentityUserDirectory
    {
        public string Status { get; set; } = "Active";

        public Task<IdentityUserDirectoryEntryV1?> GetAsync(string tenantNId, string userNId, CancellationToken cancellationToken) =>
            Task.FromResult<IdentityUserDirectoryEntryV1?>(new() { TenantNId = tenantNId, UserNId = userNId, Status = Status });
    }

    private sealed class NotificationStoreStub : INotificationStore
    {
        private readonly Dictionary<string, AnnouncementRecord> _announcements = new(StringComparer.Ordinal);
        private readonly Dictionary<string, NotificationMessageRecord> _messages = new(StringComparer.Ordinal);
        private readonly Dictionary<string, IReadOnlyList<string>> _messageRecipients = new(StringComparer.Ordinal);
        public int InsertedAnnouncements { get; private set; }
        public int PublishCount { get; private set; }
        public bool LastReadValue { get; private set; }

        public Task<AnnouncementRecord?> GetAnnouncementAsync(string tenantNId, string announcementNId, CancellationToken cancellationToken) =>
            Task.FromResult(_announcements.TryGetValue($"{tenantNId}|{announcementNId}", out var value) ? value : null);

        public Task<NotificationMessageRecord?> GetMessageAsync(string tenantNId, string notificationNId, CancellationToken cancellationToken) => Task.FromResult(_messages.TryGetValue($"{tenantNId}|{notificationNId}", out var value) ? value : null);
        public Task<IReadOnlyList<string>> GetMessageRecipientsAsync(string tenantNId, string notificationNId, CancellationToken cancellationToken) => Task.FromResult(_messageRecipients.GetValueOrDefault($"{tenantNId}|{notificationNId}", []));
        public Task<IReadOnlyList<AnnouncementRecord>> ListAnnouncementsAsync(string tenantNId, string? search, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<AnnouncementRecord>>(_announcements.Values.ToArray());

        public Task InsertAnnouncementAsync(AnnouncementRecord announcement, CancellationToken cancellationToken)
        {
            _announcements[$"{announcement.TenantNId}|{announcement.AnnouncementNId}"] = announcement;
            InsertedAnnouncements++;
            return Task.CompletedTask;
        }

        public Task UpdateAnnouncementAsync(AnnouncementRecord announcement, CancellationToken cancellationToken)
        {
            _announcements[$"{announcement.TenantNId}|{announcement.AnnouncementNId}"] = announcement;
            return Task.CompletedTask;
        }

        public Task PublishAnnouncementAsync(AnnouncementRecord announcement, NotificationMessageRecord message, IReadOnlyList<InboxDeliveryRecord> deliveries, CancellationToken cancellationToken)
        {
            _announcements[$"{announcement.TenantNId}|{announcement.AnnouncementNId}"] = announcement;
            PublishCount++;
            return Task.CompletedTask;
        }

        public Task RevokeAnnouncementAsync(AnnouncementRecord announcement, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task InsertMessageAsync(NotificationMessageRecord message, IReadOnlyList<InboxDeliveryRecord> deliveries, CancellationToken cancellationToken)
        {
            _messages[$"{message.TenantNId}|{message.NotificationNId}"] = message;
            _messageRecipients[$"{message.TenantNId}|{message.NotificationNId}"] = deliveries.Select(value => value.RecipientUserNId).Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal).ToArray();
            InsertedMessages++;
            return Task.CompletedTask;
        }
        public int InsertedMessages { get; private set; }
        public Task<NotificationInboxPageV1> ListInboxAsync(string tenantNId, string userNId, int page, int pageSize, CancellationToken cancellationToken) => Task.FromResult(new NotificationInboxPageV1());

        public Task<bool> MarkReadAsync(string tenantNId, string userNId, string notificationNId, bool read, CancellationToken cancellationToken)
        {
            LastReadValue = read;
            return Task.FromResult(true);
        }

        public Task<int> MarkManyReadAsync(string tenantNId, string userNId, IReadOnlyList<string> notificationNIds, CancellationToken cancellationToken) => Task.FromResult(notificationNIds.Count);
        public Task<int> ExpireAsync(DateTimeOffset now, CancellationToken cancellationToken) => Task.FromResult(0);
    }
}
