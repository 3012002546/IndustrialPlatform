using System.Text.Json;
using IndustrialPlatform.Infrastructure.Database;
using IndustrialPlatform.SystemData.Application.Auditing;
using IndustrialPlatform.SystemData.Application.Files;
using IndustrialPlatform.SystemData.Application.Notifications;
using IndustrialPlatform.SystemData.Contracts.Files;
using IndustrialPlatform.SystemData.Contracts.Notifications;
using IndustrialPlatform.SystemData.Contracts.Auditing;
using IndustrialPlatform.SystemData.Infrastructure.Persistence.Entities;
using SqlSugar;

namespace IndustrialPlatform.SystemData.Infrastructure.Persistence.SystemData;

/// <summary>平台专属通知及聊天引用/保全适配；文件和审计算法继承同一RuntimeSupport实现。</summary>
public sealed class SystemDataOperationalStore : IndustrialPlatform.RuntimeSupport.Persistence.FileAuditOperationalStore, IFileStore, INotificationStore
{
    private readonly SqlSugarDbContext _dbContext;
    public SystemDataOperationalStore(SqlSugarDbContext dbContext) : base(dbContext) => _dbContext = dbContext;
    public override async Task<bool> HasActiveReferencesAsync(string tenantNId, string fileNId, CancellationToken cancellationToken)
    {
        if (await base.HasActiveReferencesAsync(tenantNId, fileNId, cancellationToken)) return true;
        return await _dbContext.SqlSugar.Queryable<CollaborationFileReferenceTable>().AnyAsync(t => t.TenantNId == tenantNId && t.FileNId == fileNId && t.Status == "Active", cancellationToken);
    }

    public override Task<bool> HasActiveLegalHoldsAsync(string tenantNId, string fileNId, CancellationToken cancellationToken) =>
        _dbContext.SqlSugar.Queryable<CollaborationFileHoldTable>().AnyAsync(t => t.TenantNId == tenantNId && t.FileNId == fileNId && t.Status == "Active", cancellationToken);

    public override async Task<bool> TryRequestDeletionAsync(FileObjectRecord file, CancellationToken cancellationToken)
    {
        var sugar = _dbContext.SqlSugar;
        var ownsTransaction = sugar.Ado.IsNoTran();
        if (ownsTransaction) sugar.Ado.BeginTran();
        try
        {
            var affected = await sugar.Ado.ExecuteCommandAsync(
                "UPDATE system_file_object SET deletion_status = @status, last_updated_on = @updatedOn "
                + "WHERE tenant_n_id = @tenantNId AND file_n_id = @fileNId AND deletion_status = 'Active' "
                + "AND NOT EXISTS (SELECT 1 FROM system_file_reference_grant r WHERE r.tenant_n_id = @tenantNId AND r.file_n_id = @fileNId AND r.deleted_on IS NULL) "
                + "AND NOT EXISTS (SELECT 1 FROM file_owned_reference r WHERE r.tenant_n_id=@tenantNId AND r.file_n_id=@fileNId AND (r.status IN ('Pending','Active') OR r.retention_until>@updatedOn OR r.legal_hold=@held)) "
                + "AND NOT EXISTS (SELECT 1 FROM system_collaboration_file_reference r WHERE r.tenant_n_id = @tenantNId AND r.file_n_id = @fileNId AND r.status = 'Active') "
                + "AND NOT EXISTS (SELECT 1 FROM system_collaboration_file_hold h WHERE h.tenant_n_id = @tenantNId AND h.file_n_id = @fileNId AND h.status = 'Active')",
                new SugarParameter[]
                {
                    new SugarParameter("@held", true),
                    new SugarParameter("@status", file.DeletionStatus),
                    new SugarParameter("@updatedOn", file.LastUpdatedOn),
                    new SugarParameter("@tenantNId", file.TenantNId),
                    new SugarParameter("@fileNId", file.FileNId),
                }, cancellationToken: cancellationToken);
            if (affected == 1)
            {
                await EnqueueFileStatusAsync(new FileStatusChangeRecord(Guid.NewGuid(), file.TenantNId, file.FileNId, file.ScanStatus, file.Restricted, file.DeletionStatus, file.LastUpdatedOn), cancellationToken);
            }
            if (ownsTransaction) sugar.Ado.CommitTran();
            return affected == 1;
        }
        catch
        {
            if (ownsTransaction) sugar.Ado.RollbackTran();
            throw;
        }
    }

    public async Task<FileBusinessReferenceRecord?> GetBusinessReferenceAsync(string tenantNId, string referenceNId, CancellationToken cancellationToken) =>
        ToRecord(await _dbContext.SqlSugar.Queryable<CollaborationFileReferenceTable>()
            .Where(t => t.TenantNId == tenantNId && t.ReferenceNId == referenceNId).FirstAsync(cancellationToken));

    public async Task<FileBusinessReferenceRecord?> GetBusinessReferenceForAttachmentAsync(string tenantNId, string attachmentNId, string purpose, CancellationToken cancellationToken) =>
        ToRecord(await _dbContext.SqlSugar.Queryable<CollaborationFileReferenceTable>()
            .Where(t => t.TenantNId == tenantNId && t.AttachmentNId == attachmentNId && t.Purpose == purpose).FirstAsync(cancellationToken));

    public async Task InsertBusinessReferenceAsync(FileBusinessReferenceRecord reference, CancellationToken cancellationToken)
    {
        var affected = await _dbContext.SqlSugar.Ado.ExecuteCommandAsync(
            "INSERT INTO system_collaboration_file_reference "
            + "(tenant_n_id, reference_n_id, file_n_id, conversation_n_id, message_n_id, attachment_n_id, uploader_user_n_id, purpose, owner_service, status, version, created_on, released_on) "
            + "SELECT @tenantNId, @referenceNId, @fileNId, @conversationNId, @messageNId, @attachmentNId, @uploaderUserNId, @purpose, @ownerService, @status, @version, @createdOn, @releasedOn "
            + (_dbContext.SqlSugar.CurrentConnectionConfig.DbType == DbType.SqlServer
                ? "WHERE EXISTS (SELECT 1 FROM system_file_object WITH (UPDLOCK,HOLDLOCK) WHERE tenant_n_id = @tenantNId AND file_n_id = @fileNId AND deletion_status = 'Active') "
                    + "AND NOT EXISTS (SELECT 1 FROM system_collaboration_file_reference WITH (UPDLOCK,HOLDLOCK) WHERE tenant_n_id=@tenantNId AND (reference_n_id=@referenceNId OR (attachment_n_id=@attachmentNId AND purpose=@purpose)))"
                : "WHERE EXISTS (SELECT 1 FROM system_file_object WHERE tenant_n_id = @tenantNId AND file_n_id = @fileNId AND deletion_status = 'Active') ON CONFLICT DO NOTHING"),
            new SugarParameter[]
            {
                new SugarParameter("@tenantNId", reference.TenantNId), new SugarParameter("@referenceNId", reference.ReferenceNId),
                new SugarParameter("@fileNId", reference.FileNId), new SugarParameter("@conversationNId", reference.ConversationNId),
                new SugarParameter("@messageNId", reference.MessageNId), new SugarParameter("@attachmentNId", reference.AttachmentNId),
                new SugarParameter("@uploaderUserNId", reference.UploaderUserNId), new SugarParameter("@purpose", reference.Purpose),
                new SugarParameter("@ownerService", reference.OwnerService), new SugarParameter("@status", reference.Status),
                new SugarParameter("@version", reference.Version), new SugarParameter("@createdOn", reference.CreatedOn), new SugarParameter("@releasedOn", reference.ReleasedOn),
            }, cancellationToken: cancellationToken);
        if (affected != 1) throw new InvalidOperationException("文件已进入删除流程，不能建立业务文件引用。");
    }

    public Task<bool> ReleaseBusinessReferenceAsync(string tenantNId, string fileNId, string referenceNId, long expectedVersion, DateTimeOffset releasedOn, CancellationToken cancellationToken) =>
        _dbContext.SqlSugar.Updateable<CollaborationFileReferenceTable>()
            .SetColumns(t => new CollaborationFileReferenceTable { Status = "Released", Version = t.Version + 1, ReleasedOn = releasedOn })
            .Where(t => t.TenantNId == tenantNId && t.FileNId == fileNId && t.ReferenceNId == referenceNId && t.Status == "Active" && t.Version == expectedVersion)
            .ExecuteCommandAsync(cancellationToken)
            .ContinueWith(task => task.Result == 1, cancellationToken);

    public async Task<FileHoldRecord?> GetLegalHoldAsync(string tenantNId, string caseNId, string fileNId, CancellationToken cancellationToken) =>
        ToRecord(await _dbContext.SqlSugar.Queryable<CollaborationFileHoldTable>()
            .Where(t => t.TenantNId == tenantNId && t.CaseNId == caseNId && t.FileNId == fileNId).FirstAsync(cancellationToken));

    public async Task InsertLegalHoldAsync(FileHoldRecord hold, CancellationToken cancellationToken)
    {
        var affected = await _dbContext.SqlSugar.Ado.ExecuteCommandAsync(
            "INSERT INTO system_collaboration_file_hold "
            + "(tenant_n_id, case_n_id, file_n_id, scope_checksum, case_revision, owner_service, status, created_on, updated_on, released_on) "
            + "SELECT @tenantNId, @caseNId, @fileNId, @scopeChecksum, @caseRevision, @ownerService, @status, @createdOn, @updatedOn, @releasedOn "
            + (_dbContext.SqlSugar.CurrentConnectionConfig.DbType == DbType.SqlServer
                ? "WHERE EXISTS (SELECT 1 FROM system_file_object WITH (UPDLOCK,HOLDLOCK) WHERE tenant_n_id = @tenantNId AND file_n_id = @fileNId AND deletion_status = 'Active') "
                    + "AND NOT EXISTS (SELECT 1 FROM system_collaboration_file_hold WITH (UPDLOCK,HOLDLOCK) WHERE tenant_n_id=@tenantNId AND case_n_id=@caseNId AND file_n_id=@fileNId)"
                : "WHERE EXISTS (SELECT 1 FROM system_file_object WHERE tenant_n_id = @tenantNId AND file_n_id = @fileNId AND deletion_status = 'Active') ON CONFLICT DO NOTHING"),
            new SugarParameter[]
            {
                new SugarParameter("@tenantNId", hold.TenantNId), new SugarParameter("@caseNId", hold.CaseNId), new SugarParameter("@fileNId", hold.FileNId),
                new SugarParameter("@scopeChecksum", hold.ScopeChecksum), new SugarParameter("@caseRevision", hold.CaseRevision), new SugarParameter("@ownerService", hold.OwnerService),
                new SugarParameter("@status", hold.Status), new SugarParameter("@createdOn", hold.CreatedOn), new SugarParameter("@updatedOn", hold.UpdatedOn), new SugarParameter("@releasedOn", hold.ReleasedOn),
            }, cancellationToken: cancellationToken);
        if (affected != 1) throw new InvalidOperationException("文件已进入删除流程，不能建立 Legal Hold。");
    }

    public Task<bool> ReleaseLegalHoldAsync(string tenantNId, string caseNId, string fileNId, string scopeChecksum, long caseRevision, DateTimeOffset releasedOn, CancellationToken cancellationToken) =>
        _dbContext.SqlSugar.Updateable<CollaborationFileHoldTable>()
            .SetColumns(t => new CollaborationFileHoldTable { Status = "Released", UpdatedOn = releasedOn, ReleasedOn = releasedOn })
            .Where(t => t.TenantNId == tenantNId && t.CaseNId == caseNId && t.FileNId == fileNId && t.ScopeChecksum == scopeChecksum && t.CaseRevision == caseRevision && t.Status == "Active")
            .ExecuteCommandAsync(cancellationToken)
            .ContinueWith(task => task.Result == 1, cancellationToken);

    public async Task<AnnouncementRecord?> GetAnnouncementAsync(string tenantNId, string announcementNId, CancellationToken cancellationToken) =>
        ToRecord(await _dbContext.SqlSugar.Queryable<NotificationAnnouncementTable>().Where(t => t.TenantNId == tenantNId && t.AnnouncementNId == announcementNId).FirstAsync(cancellationToken));

    public async Task<NotificationMessageRecord?> GetMessageAsync(string tenantNId, string notificationNId, CancellationToken cancellationToken) =>
        ToRecord(await _dbContext.SqlSugar.Queryable<NotificationMessageTable>().Where(t => t.TenantNId == tenantNId && t.NotificationNId == notificationNId).FirstAsync(cancellationToken));

    public async Task<IReadOnlyList<string>> GetMessageRecipientsAsync(string tenantNId, string notificationNId, CancellationToken cancellationToken) =>
        (await _dbContext.SqlSugar.Queryable<NotificationInboxDeliveryTable>()
            .Where(t => t.TenantNId == tenantNId && t.NotificationNId == notificationNId)
            .Select(t => t.RecipientUserNId)
            .ToListAsync(cancellationToken))
        .Distinct(StringComparer.Ordinal)
        .OrderBy(value => value, StringComparer.Ordinal)
        .ToArray();

    public async Task<IReadOnlyList<AnnouncementRecord>> ListAnnouncementsAsync(string tenantNId, string? search, CancellationToken cancellationToken)
    {
        var result = await ListAnnouncementsPageAsync(tenantNId, search, 1, 200, cancellationToken);
        return result.Items;
    }

    public async Task<AnnouncementPageRecord> ListAnnouncementsPageAsync(string tenantNId, string? search, int page, int pageSize, CancellationToken cancellationToken)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 200);
        var query = _dbContext.SqlSugar.Queryable<NotificationAnnouncementTable>().Where(t => t.TenantNId == tenantNId);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(t => t.AnnouncementNId.Contains(term) || t.Title.Contains(term));
        }
        var total = await query.CountAsync(cancellationToken);
        var rows = await query.OrderBy(t => t.CreatedOn, SqlSugar.OrderByType.Desc).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        return new AnnouncementPageRecord(rows.Select(row => ToRecord(row)!).ToArray(), page, pageSize, total);
    }

    public async Task InsertAnnouncementAsync(AnnouncementRecord announcement, CancellationToken cancellationToken) =>
        await _dbContext.SqlSugar.Insertable(ToTable(announcement)).ExecuteCommandAsync(cancellationToken);

    public async Task UpdateAnnouncementAsync(AnnouncementRecord announcement, CancellationToken cancellationToken)
    {
        var existing = await _dbContext.SqlSugar.Queryable<NotificationAnnouncementTable>()
            .Where(t => t.TenantNId == announcement.TenantNId && t.AnnouncementNId == announcement.AnnouncementNId)
            .FirstAsync(cancellationToken);
        if (existing is null) return;
        await _dbContext.SqlSugar.Updateable(ToTable(announcement, existing.Id))
            .Where(t => t.TenantNId == announcement.TenantNId && t.AnnouncementNId == announcement.AnnouncementNId)
            .ExecuteCommandAsync(cancellationToken);
    }

    public async Task PublishAnnouncementAsync(AnnouncementRecord announcement, NotificationMessageRecord message, IReadOnlyList<InboxDeliveryRecord> deliveries, CancellationToken cancellationToken)
    {
        var sugar = _dbContext.SqlSugar;
        var ownsTransaction = sugar.Ado.IsNoTran();
        if (ownsTransaction) sugar.Ado.BeginTran();
        try
        {
            var existing = await sugar.Queryable<NotificationAnnouncementTable>()
                .Where(t => t.TenantNId == announcement.TenantNId && t.AnnouncementNId == announcement.AnnouncementNId && t.Status == "Draft")
                .FirstAsync(cancellationToken);
            if (existing is null) throw new InvalidOperationException("The announcement is not a draft or does not exist.");
            await sugar.Updateable(ToTable(announcement, existing.Id)).Where(t => t.TenantNId == announcement.TenantNId && t.AnnouncementNId == announcement.AnnouncementNId && t.Status == "Draft").ExecuteCommandAsync(cancellationToken);
            await sugar.Insertable(ToTable(message)).ExecuteCommandAsync(cancellationToken);
            await sugar.Insertable(deliveries.Select(ToTable).ToArray()).ExecuteCommandAsync(cancellationToken);
            if (ownsTransaction) sugar.Ado.CommitTran();
        }
        catch
        {
            if (ownsTransaction) sugar.Ado.RollbackTran();
            throw;
        }
    }

    public async Task RevokeAnnouncementAsync(AnnouncementRecord announcement, CancellationToken cancellationToken)
    {
        var sugar = _dbContext.SqlSugar;
        var ownsTransaction = sugar.Ado.IsNoTran();
        if (ownsTransaction) sugar.Ado.BeginTran();
        try
        {
            var existing = await sugar.Queryable<NotificationAnnouncementTable>()
                .Where(t => t.TenantNId == announcement.TenantNId && t.AnnouncementNId == announcement.AnnouncementNId)
                .FirstAsync(cancellationToken);
            if (existing is null)
            {
                return;
            }
            await sugar.Updateable(ToTable(announcement, existing.Id))
                .Where(t => t.TenantNId == announcement.TenantNId && t.AnnouncementNId == announcement.AnnouncementNId)
                .ExecuteCommandAsync(cancellationToken);
            var messages = await sugar.Queryable<NotificationMessageTable>()
                .Where(t => t.TenantNId == announcement.TenantNId && t.AnnouncementNId == announcement.AnnouncementNId)
                .ToListAsync(cancellationToken);
            foreach (var message in messages)
            {
                await sugar.Updateable<NotificationMessageTable>()
                    .SetColumns(t => new NotificationMessageTable { RevokedOn = announcement.RevokedOn })
                    .Where(t => t.Id == message.Id)
                    .ExecuteCommandAsync(cancellationToken);
                await sugar.Updateable<NotificationInboxDeliveryTable>()
                    .SetColumns(t => new NotificationInboxDeliveryTable { RevokedOn = announcement.RevokedOn })
                    .Where(t => t.TenantNId == announcement.TenantNId && t.NotificationNId == message.NotificationNId)
                    .ExecuteCommandAsync(cancellationToken);
            }
            if (ownsTransaction) sugar.Ado.CommitTran();
        }
        catch
        {
            if (ownsTransaction) sugar.Ado.RollbackTran();
            throw;
        }
    }

    public async Task InsertMessageAsync(NotificationMessageRecord message, IReadOnlyList<InboxDeliveryRecord> deliveries, CancellationToken cancellationToken)
    {
        var sugar = _dbContext.SqlSugar;
        var ownsTransaction = sugar.Ado.IsNoTran();
        if (ownsTransaction) sugar.Ado.BeginTran();
        try
        {
            await sugar.Insertable(ToTable(message)).ExecuteCommandAsync(cancellationToken);
            await sugar.Insertable(deliveries.Select(ToTable).ToArray()).ExecuteCommandAsync(cancellationToken);
            if (ownsTransaction) sugar.Ado.CommitTran();
        }
        catch
        {
            if (ownsTransaction) sugar.Ado.RollbackTran();
            throw;
        }
    }

    public async Task<NotificationInboxPageV1> ListInboxAsync(string tenantNId, string userNId, int page, int pageSize, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var deliveries = await _dbContext.SqlSugar.Queryable<NotificationInboxDeliveryTable>().Where(t => t.TenantNId == tenantNId && t.RecipientUserNId == userNId && t.RevokedOn == null && (t.ExpiresOn == null || t.ExpiresOn > now)).OrderBy(t => t.DeliveredOn, SqlSugar.OrderByType.Desc).ToListAsync(cancellationToken);
        var messageIds = deliveries.Select(t => t.NotificationNId).Distinct(StringComparer.Ordinal).ToArray();
        var messages = messageIds.Length == 0 ? [] : await _dbContext.SqlSugar.Queryable<NotificationMessageTable>().Where(t => t.TenantNId == tenantNId && messageIds.Contains(t.NotificationNId) && t.RevokedOn == null).ToListAsync(cancellationToken);
        var messageMap = messages.ToDictionary(t => t.NotificationNId, StringComparer.Ordinal);
        var visible = deliveries.Where(t => messageMap.ContainsKey(t.NotificationNId)).ToArray();
        var items = visible.Skip((page - 1) * pageSize).Take(pageSize).Select(t => ToContract(messageMap[t.NotificationNId], t)).ToArray();
        return new NotificationInboxPageV1 { Items = items, Page = page, PageSize = pageSize, Total = visible.LongLength, UnreadCount = visible.Count(t => t.ReadOn is null) };
    }

    public async Task<bool> MarkReadAsync(string tenantNId, string userNId, string notificationNId, bool read, CancellationToken cancellationToken)
    {
        var row = await _dbContext.SqlSugar.Queryable<NotificationInboxDeliveryTable>().Where(t => t.TenantNId == tenantNId && t.RecipientUserNId == userNId && t.NotificationNId == notificationNId && t.RevokedOn == null).FirstAsync(cancellationToken);
        if (row is null) return false;
        if (!read || row.ReadOn is not null) return true;
        row.ReadOn = DateTimeOffset.UtcNow;
        await _dbContext.SqlSugar.Updateable(row).Where(t => t.Id == row.Id).ExecuteCommandAsync(cancellationToken);
        return true;
    }

    public async Task<int> ExpireAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var changed = 0;
        changed += await _dbContext.SqlSugar.Updateable<NotificationMessageTable>()
            .SetColumns(t => new NotificationMessageTable { RevokedOn = now })
            .Where(t => t.RevokedOn == null && t.ExpiresOn != null && t.ExpiresOn <= now)
            .ExecuteCommandAsync(cancellationToken);
        changed += await _dbContext.SqlSugar.Updateable<NotificationInboxDeliveryTable>()
            .SetColumns(t => new NotificationInboxDeliveryTable { RevokedOn = now })
            .Where(t => t.RevokedOn == null && t.ExpiresOn != null && t.ExpiresOn <= now)
            .ExecuteCommandAsync(cancellationToken);
        return changed;
    }

    private static CollaborationFileReferenceTable ToTable(FileBusinessReferenceRecord value) => new() { TenantNId = value.TenantNId, ReferenceNId = value.ReferenceNId, FileNId = value.FileNId, ConversationNId = value.ConversationNId, MessageNId = value.MessageNId, AttachmentNId = value.AttachmentNId, UploaderUserNId = value.UploaderUserNId, Purpose = value.Purpose, OwnerService = value.OwnerService, Status = value.Status, Version = value.Version, CreatedOn = value.CreatedOn, ReleasedOn = value.ReleasedOn };
    private static FileBusinessReferenceRecord? ToRecord(CollaborationFileReferenceTable? value) => value is null ? null : new(value.TenantNId, value.ReferenceNId, value.FileNId, value.ConversationNId, value.MessageNId, value.AttachmentNId, value.UploaderUserNId, value.Purpose, value.OwnerService, value.Status, value.Version, value.CreatedOn, value.ReleasedOn);
    private static CollaborationFileHoldTable ToTable(FileHoldRecord value) => new() { TenantNId = value.TenantNId, CaseNId = value.CaseNId, FileNId = value.FileNId, ScopeChecksum = value.ScopeChecksum, CaseRevision = value.CaseRevision, OwnerService = value.OwnerService, Status = value.Status, CreatedOn = value.CreatedOn, UpdatedOn = value.UpdatedOn, ReleasedOn = value.ReleasedOn };
    private static FileHoldRecord? ToRecord(CollaborationFileHoldTable? value) => value is null ? null : new(value.TenantNId, value.CaseNId, value.FileNId, value.ScopeChecksum, value.CaseRevision, value.OwnerService, value.Status, value.CreatedOn, value.UpdatedOn, value.ReleasedOn);
    private static NotificationAnnouncementTable ToTable(AnnouncementRecord value) => ToTable(value, Guid.NewGuid());
    private static NotificationAnnouncementTable ToTable(AnnouncementRecord value, Guid id) => new() { Id = id, TenantNId = value.TenantNId, AnnouncementNId = value.AnnouncementNId, Title = value.Title, Body = value.Body, Priority = value.Priority, Status = value.Status, AudienceUserNIdsJson = JsonSerializer.Serialize(value.RecipientUserNIds), PublishedOn = value.PublishedOn, ExpiresOn = value.ExpiresOn, CreatedByUserNId = value.CreatedByUserNId, CreatedOn = value.CreatedOn, LastUpdatedOn = value.LastUpdatedOn, RevokedOn = value.RevokedOn, ResourceNId = value.ResourceNId, TargetRoute = value.TargetRoute, IdempotencyKey = value.IdempotencyKey };
    private static AnnouncementRecord? ToRecord(NotificationAnnouncementTable? value) => value is null ? null : new(value.TenantNId, value.AnnouncementNId, value.Title, value.Body, value.Priority, value.Status, JsonSerializer.Deserialize<string[]>(value.AudienceUserNIdsJson) ?? [], value.PublishedOn, value.ExpiresOn, value.CreatedByUserNId, value.CreatedOn, value.LastUpdatedOn, value.RevokedOn, value.ResourceNId, value.TargetRoute, value.IdempotencyKey);
    private static NotificationMessageTable ToTable(NotificationMessageRecord value) => new() { Id = Guid.NewGuid(), TenantNId = value.TenantNId, NotificationNId = value.NotificationNId, AnnouncementNId = value.AnnouncementNId, Kind = value.Kind, Title = value.Title, Body = value.Body, SenderUserNId = value.SenderUserNId, CreatedOn = value.CreatedOn, ExpiresOn = value.ExpiresOn, RevokedOn = value.RevokedOn, ResourceNId = value.ResourceNId, TargetRoute = value.TargetRoute, IdempotencyKey = value.IdempotencyKey };
    private static NotificationMessageRecord? ToRecord(NotificationMessageTable? value) => value is null ? null : new(value.TenantNId, value.NotificationNId, value.AnnouncementNId, value.Kind, value.Title, value.Body, value.SenderUserNId, value.CreatedOn, value.ExpiresOn, value.RevokedOn, value.ResourceNId, value.TargetRoute, value.IdempotencyKey);
    private static NotificationInboxDeliveryTable ToTable(InboxDeliveryRecord value) => new() { Id = Guid.NewGuid(), TenantNId = value.TenantNId, NotificationNId = value.NotificationNId, RecipientUserNId = value.RecipientUserNId, DeliveredOn = value.DeliveredOn, ReadOn = value.ReadOn, ExpiresOn = value.ExpiresOn, RevokedOn = value.RevokedOn };
    private static NotificationInboxItemV1 ToContract(NotificationMessageTable message, NotificationInboxDeliveryTable delivery) => new() { NotificationNId = message.NotificationNId, Kind = message.Kind, Title = message.Title, Body = message.Body, SenderUserNId = message.SenderUserNId, IsRead = delivery.ReadOn is not null, DeliveredOn = delivery.DeliveredOn, ReadOn = delivery.ReadOn, ExpiresOn = delivery.ExpiresOn, ResourceNId = message.ResourceNId, TargetRoute = message.TargetRoute };
    public async Task<int> MarkManyReadAsync(string tenantNId, string userNId, IReadOnlyList<string> notificationNIds, CancellationToken cancellationToken)
    {
        var changed = 0;
        foreach (var notificationNId in notificationNIds.Distinct(StringComparer.Ordinal))
        {
            if (await MarkReadAsync(tenantNId, userNId, notificationNId, true, cancellationToken)) changed++;
        }
        return changed;
    }

    public override async Task RecordAccessAsync(string tenantNId, string actorUserNId, string action, string scope, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        await _dbContext.SqlSugar.Insertable(new SystemDataOperationAuditTable
        {
            Id = Guid.NewGuid(),
            IsFrozen = true,
            IsLocked = false,
            IsDeleted = false,
            EntityType = "SystemData.AuditFact",
            CreatedOn = now,
            LastUpdatedOn = now,
            OptimisticVersion = 1,
            ConcurrencyVersion = Guid.NewGuid(),
            TenantNId = tenantNId,
            ActorUserNId = actorUserNId,
            Action = action,
            ObjectType = "AuditQuery",
            ObjectNId = Limit(scope) ?? "*",
            Reason = "audit access",
            TraceId = string.Empty,
        }).ExecuteCommandAsync(cancellationToken);
    }

    public override async Task RecordLifecycleAuditAsync(string tenantNId, string producerServiceKey, string auditEventNId, string action, string reason, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        await _dbContext.SqlSugar.Insertable(new SystemDataOperationAuditTable
        {
            Id = Guid.NewGuid(),
            IsFrozen = true,
            IsLocked = false,
            IsDeleted = false,
            EntityType = "SystemData.AuditLifecycle",
            CreatedOn = now,
            LastUpdatedOn = now,
            OptimisticVersion = 1,
            ConcurrencyVersion = Guid.NewGuid(),
            TenantNId = tenantNId,
            ActorUserNId = "system:audit-lifecycle",
            Action = action,
            ObjectType = producerServiceKey,
            ObjectNId = auditEventNId,
            Reason = Limit(reason) ?? "audit lifecycle control",
            TraceId = string.Empty,
        }).ExecuteCommandAsync(cancellationToken);
    }

}
