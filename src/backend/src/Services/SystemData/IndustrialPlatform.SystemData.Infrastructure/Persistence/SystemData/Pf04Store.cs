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

/// <summary>
/// PF-04 持久化适配器。文件、通知、审计共享 SystemData 的 SqlSugar 连接与事务边界。
/// </summary>
public sealed class Pf04Store : IFileStore, INotificationStore, IAuditStore
{
    private readonly SqlSugarDbContext _dbContext;

    public Pf04Store(SqlSugarDbContext dbContext) => _dbContext = dbContext;

    public async Task<FileUploadSessionRecord?> GetSessionAsync(string tenantNId, string sessionNId, CancellationToken cancellationToken) =>
        ToRecord(await _dbContext.SqlSugar.Queryable<FileUploadSessionTable>().Where(t => t.TenantNId == tenantNId && t.SessionNId == sessionNId).FirstAsync(cancellationToken));

    public async Task<FileUploadSessionRecord?> GetSessionByTransportAsync(string tenantNId, string transportId, CancellationToken cancellationToken) =>
        ToRecord(await _dbContext.SqlSugar.Queryable<FileUploadSessionTable>().Where(t => t.TenantNId == tenantNId && t.TransportId == transportId).FirstAsync(cancellationToken));

    public async Task<IReadOnlyList<FileUploadSessionRecord>> ExpireSessionsAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var rows = await _dbContext.SqlSugar.Queryable<FileUploadSessionTable>()
            .Where(t => t.ExpiresOn <= now && t.Status != "Completed" && t.Status != "Cancelled" && t.Status != "Expired")
            .Take(100)
            .ToListAsync(cancellationToken);
        foreach (var row in rows)
        {
            await _dbContext.SqlSugar.Updateable<FileUploadSessionTable>()
                .SetColumns(t => new FileUploadSessionTable { Status = "Expired", ErrorCode = "FILE_UPLOAD_EXPIRED", LastUpdatedOn = now })
                .Where(t => t.Id == row.Id && t.Status != "Completed" && t.Status != "Cancelled")
                .ExecuteCommandAsync(cancellationToken);
        }
        return rows.Select(row => ToRecord(row)!).ToArray();
    }

    public async Task<IReadOnlyList<FileUploadSessionRecord>> FindCandidatesAsync(string tenantNId, string uploaderUserNId, string purpose, string sampleFingerprint, long length, CancellationToken cancellationToken) =>
        (await _dbContext.SqlSugar.Queryable<FileUploadSessionTable>().Where(t => t.TenantNId == tenantNId && t.UploaderUserNId == uploaderUserNId && t.Purpose == purpose && t.SampleFingerprint == sampleFingerprint && t.ExpectedLength == length && t.Status != "Cancelled" && t.Status != "Completed").OrderBy(t => t.CreatedOn, SqlSugar.OrderByType.Desc).Take(20).ToListAsync(cancellationToken)).Select(row => ToRecord(row)!).ToArray();

    public async Task InsertSessionAsync(FileUploadSessionRecord session, CancellationToken cancellationToken) =>
        await _dbContext.SqlSugar.Insertable(ToTable(session)).ExecuteCommandAsync(cancellationToken);

    public async Task<bool> UpdateSessionAsync(FileUploadSessionRecord session, long expectedOffset, int expectedEpoch, CancellationToken cancellationToken)
    {
        var existing = await _dbContext.SqlSugar.Queryable<FileUploadSessionTable>()
            .Where(t => t.TenantNId == session.TenantNId && t.SessionNId == session.SessionNId && t.CurrentOffset == expectedOffset && t.WriterEpoch == expectedEpoch)
            .FirstAsync(cancellationToken);
        if (existing is null) return false;

        var affected = await _dbContext.SqlSugar.Updateable(ToTable(session, existing.Id))
            .Where(t => t.TenantNId == session.TenantNId && t.SessionNId == session.SessionNId && t.CurrentOffset == expectedOffset && t.WriterEpoch == expectedEpoch)
            .ExecuteCommandAsync(cancellationToken);
        return affected == 1;
    }

    public async Task<bool> UpdateAppendAsync(FileUploadSessionRecord session, long expectedOffset, int expectedEpoch, string expectedStatus, CancellationToken cancellationToken)
    {
        var existing = await _dbContext.SqlSugar.Queryable<FileUploadSessionTable>()
            .Where(t => t.TenantNId == session.TenantNId && t.SessionNId == session.SessionNId && t.CurrentOffset == expectedOffset && t.WriterEpoch == expectedEpoch && t.Status == expectedStatus)
            .FirstAsync(cancellationToken);
        if (existing is null) return false;

        var affected = await _dbContext.SqlSugar.Updateable(ToTable(session, existing.Id))
            .Where(t => t.TenantNId == session.TenantNId && t.SessionNId == session.SessionNId && t.CurrentOffset == expectedOffset && t.WriterEpoch == expectedEpoch && t.Status == expectedStatus)
            .ExecuteCommandAsync(cancellationToken);
        return affected == 1;
    }

    public async Task<FileObjectRecord?> GetFileAsync(string tenantNId, string fileNId, CancellationToken cancellationToken) =>
        ToRecord(await _dbContext.SqlSugar.Queryable<FileObjectTable>().Where(t => t.TenantNId == tenantNId && t.FileNId == fileNId && t.DeletionStatus != "Deleted").FirstAsync(cancellationToken));

    public async Task<IReadOnlyList<FileObjectRecord>> ListPendingScanAsync(int limit, CancellationToken cancellationToken) =>
        (await _dbContext.SqlSugar.Queryable<FileObjectTable>()
            .Where(t => t.DeletionStatus == "Active" && (t.ScanStatus == "PendingScan" || t.ScanStatus == "Error" || t.ScanStatus == "Unknown"))
            .OrderBy(t => t.CreatedOn, SqlSugar.OrderByType.Asc)
            .Take(Math.Clamp(limit, 1, 100))
            .ToListAsync(cancellationToken)).Select(row => ToRecord(row)!).ToArray();

    public async Task<IReadOnlyList<FileObjectRecord>> ListDeletionCandidatesAsync(DateTimeOffset now, int limit, CancellationToken cancellationToken) =>
        (await _dbContext.SqlSugar.Queryable<FileObjectTable>()
            .Where(t => t.DeletionStatus == "DeletionRequested" && (t.RetentionUntil == null || t.RetentionUntil <= now))
            .OrderBy(t => t.LastUpdatedOn, SqlSugar.OrderByType.Asc)
            .Take(Math.Clamp(limit, 1, 100))
            .ToListAsync(cancellationToken)).Select(row => ToRecord(row)!).ToArray();

    public async Task MarkFileDeletedAsync(FileObjectRecord file, DateTimeOffset deletedOn, CancellationToken cancellationToken)
    {
        await _dbContext.SqlSugar.Updateable<FileObjectTable>()
            .SetColumns(t => new FileObjectTable { DeletionStatus = "Deleted", DeletedOn = deletedOn, LastUpdatedOn = deletedOn })
            .Where(t => t.TenantNId == file.TenantNId && t.FileNId == file.FileNId && t.DeletionStatus == "DeletionRequested")
            .ExecuteCommandAsync(cancellationToken);
    }

    public async Task<FilePageV1> ListFilesAsync(string tenantNId, string? search, int page, int pageSize, CancellationToken cancellationToken)
    {
        var query = _dbContext.SqlSugar.Queryable<FileObjectTable>().Where(t => t.TenantNId == tenantNId && t.DeletionStatus != "Deleted");
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(t => t.FileNId.Contains(term) || t.FileName.Contains(term));
        }
        var total = await query.CountAsync(cancellationToken);
        var rows = await query.OrderBy(t => t.CreatedOn, SqlSugar.OrderByType.Desc).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        return new FilePageV1 { Items = rows.Select(row => ToContract(ToRecord(row)!)).ToArray(), Page = page, PageSize = pageSize, Total = total };
    }

    public async Task<FileObjectRecord?> CompleteSessionAsync(FileUploadSessionRecord completedSession, FileObjectRecord file, long expectedOffset, int expectedEpoch, CancellationToken cancellationToken)
    {
        var sugar = _dbContext.SqlSugar;
        var ownsTransaction = sugar.Ado.IsNoTran();
        if (ownsTransaction) sugar.Ado.BeginTran();
        try
        {
            var current = await sugar.Queryable<FileUploadSessionTable>()
                .Where(t => t.TenantNId == completedSession.TenantNId && t.SessionNId == completedSession.SessionNId)
                .FirstAsync(cancellationToken);
            if (current is null) return RollbackAndReturn(sugar, null, ownsTransaction);
            if (current.Status == "Completed" && current.FileNId is not null)
            {
                var existing = await sugar.Queryable<FileObjectTable>()
                    .Where(t => t.TenantNId == completedSession.TenantNId && t.FileNId == current.FileNId)
                    .FirstAsync(cancellationToken);
                return RollbackAndReturn(sugar, ToRecord(existing), ownsTransaction);
            }
            if (current.CurrentOffset != expectedOffset || current.WriterEpoch != expectedEpoch || current.Status is "Cancelled" or "Expired") return RollbackAndReturn(sugar, null, ownsTransaction);

            await sugar.Insertable(ToTable(file)).ExecuteCommandAsync(cancellationToken);
            await sugar.Insertable(new FileScanAttemptTable
            {
                Id = Guid.NewGuid(),
                TenantNId = file.TenantNId,
                ScanNId = $"scan-{Guid.NewGuid():N}",
                FileNId = file.FileNId,
                Engine = "unavailable",
                Status = "PendingScan",
                StartedOn = file.CreatedOn,
                Detail = "scanner is not configured; download remains blocked"
            }).ExecuteCommandAsync(cancellationToken);
            var affected = await sugar.Updateable(ToTable(completedSession, current.Id))
                .Where(t => t.TenantNId == completedSession.TenantNId && t.SessionNId == completedSession.SessionNId && t.CurrentOffset == expectedOffset && t.WriterEpoch == expectedEpoch && t.Status != "Completed")
                .ExecuteCommandAsync(cancellationToken);
            if (affected != 1) return RollbackAndReturn(sugar, null, ownsTransaction);
            if (ownsTransaction) sugar.Ado.CommitTran();
            return file;
        }
        catch
        {
            if (ownsTransaction) sugar.Ado.RollbackTran();
            throw;
        }
    }

    public async Task AddScanAttemptAsync(string tenantNId, string fileNId, string status, string detail, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        await _dbContext.SqlSugar.Insertable(new FileScanAttemptTable { Id = Guid.NewGuid(), TenantNId = tenantNId, ScanNId = $"scan-{Guid.NewGuid():N}", FileNId = fileNId, Engine = "unavailable", Status = status, StartedOn = now, CompletedOn = null, Detail = detail }).ExecuteCommandAsync(cancellationToken);
    }

    public async Task<FileReferenceRecord?> GetReferenceAsync(string tenantNId, string referenceNId, CancellationToken cancellationToken) =>
        ToRecord(await _dbContext.SqlSugar.Queryable<FileReferenceGrantTable>().Where(t => t.TenantNId == tenantNId && t.ReferenceNId == referenceNId && t.DeletedOn == null).FirstAsync(cancellationToken));

    public async Task<FileReferenceRecord?> GetReferenceForFileAsync(string tenantNId, string fileNId, string ownerUserNId, CancellationToken cancellationToken) =>
        ToRecord(await _dbContext.SqlSugar.Queryable<FileReferenceGrantTable>().Where(t => t.TenantNId == tenantNId && t.FileNId == fileNId && t.OwnerUserNId == ownerUserNId && t.DeletedOn == null).FirstAsync(cancellationToken));

    public Task<bool> HasActiveReferencesAsync(string tenantNId, string fileNId, CancellationToken cancellationToken) =>
        _dbContext.SqlSugar.Queryable<FileReferenceGrantTable>().AnyAsync(t => t.TenantNId == tenantNId && t.FileNId == fileNId && t.DeletedOn == null, cancellationToken);

    public async Task InsertReferenceAsync(FileReferenceRecord reference, CancellationToken cancellationToken) =>
        await _dbContext.SqlSugar.Insertable(ToTable(reference)).ExecuteCommandAsync(cancellationToken);

    public async Task DeleteReferenceAsync(string tenantNId, string referenceNId, DateTimeOffset deletedOn, CancellationToken cancellationToken) =>
        await _dbContext.SqlSugar.Updateable<FileReferenceGrantTable>().SetColumns(t => new FileReferenceGrantTable { DeletedOn = deletedOn }).Where(t => t.TenantNId == tenantNId && t.ReferenceNId == referenceNId && t.DeletedOn == null).ExecuteCommandAsync(cancellationToken);

    public async Task UpdateFileAsync(FileObjectRecord file, CancellationToken cancellationToken)
    {
        var existing = await _dbContext.SqlSugar.Queryable<FileObjectTable>()
            .Where(t => t.TenantNId == file.TenantNId && t.FileNId == file.FileNId)
            .FirstAsync(cancellationToken);
        if (existing is null) return;
        await _dbContext.SqlSugar.Updateable(ToTable(file, existing.Id))
            .Where(t => t.TenantNId == file.TenantNId && t.FileNId == file.FileNId)
            .ExecuteCommandAsync(cancellationToken);
    }

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
        var query = _dbContext.SqlSugar.Queryable<NotificationAnnouncementTable>().Where(t => t.TenantNId == tenantNId);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(t => t.AnnouncementNId.Contains(term) || t.Title.Contains(term));
        }
        return (await query.OrderBy(t => t.CreatedOn, SqlSugar.OrderByType.Desc).Take(200).ToListAsync(cancellationToken)).Select(row => ToRecord(row)!).ToArray();
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

    public async Task<int> MarkManyReadAsync(string tenantNId, string userNId, IReadOnlyList<string> notificationNIds, CancellationToken cancellationToken)
    {
        var changed = 0;
        foreach (var notificationNId in notificationNIds.Distinct(StringComparer.Ordinal))
        {
            if (await MarkReadAsync(tenantNId, userNId, notificationNId, true, cancellationToken)) changed++;
        }
        return changed;
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

    public async Task<AuditFactRecord?> GetAsync(string tenantNId, string producerServiceKey, string auditEventNId, CancellationToken cancellationToken) =>
        ToRecord(await _dbContext.SqlSugar.Queryable<AuditFactTable>().Where(t => t.TenantNId == tenantNId && t.ProducerServiceKey == producerServiceKey && t.AuditEventNId == auditEventNId).FirstAsync(cancellationToken));

    public async Task<AuditLifecycleRecord?> GetLifecycleAsync(string tenantNId, string producerServiceKey, string auditEventNId, CancellationToken cancellationToken) =>
        ToLifecycleRecord(await _dbContext.SqlSugar.Queryable<AuditLifecycleTable>().Where(t => t.TenantNId == tenantNId && t.ProducerServiceKey == producerServiceKey && t.AuditEventNId == auditEventNId).FirstAsync(cancellationToken));

    public async Task InsertAsync(AuditFactRecord fact, CancellationToken cancellationToken)
    {
        var sugar = _dbContext.SqlSugar;
        sugar.Ado.BeginTran();
        try
        {
            await sugar.Insertable(ToTable(fact)).ExecuteCommandAsync(cancellationToken);
            await sugar.Insertable(new AuditLifecycleTable { Id = Guid.NewGuid(), TenantNId = fact.TenantNId, ProducerServiceKey = fact.ProducerServiceKey, AuditEventNId = fact.AuditEventNId, State = "Active", ChangedOn = fact.ReceivedOn }).ExecuteCommandAsync(cancellationToken);
            sugar.Ado.CommitTran();
        }
        catch
        {
            sugar.Ado.RollbackTran();
            throw;
        }
    }

    public async Task RecordIngressFailureAsync(string tenantNId, string producerServiceKey, string? auditEventNId, string errorCode, string errorSummary, string? payloadHash, CancellationToken cancellationToken) =>
        await _dbContext.SqlSugar.Insertable(new AuditIngressFailureTable
        {
            Id = Guid.NewGuid(),
            TenantNId = tenantNId,
            FailureNId = $"failure-{Guid.NewGuid():N}",
            ProducerServiceKey = producerServiceKey,
            AuditEventNId = auditEventNId,
            ErrorCode = errorCode,
            ErrorSummary = errorSummary,
            PayloadHash = payloadHash,
            OccurredOn = DateTimeOffset.UtcNow,
        }).ExecuteCommandAsync(cancellationToken);

    public async Task RecordAccessAsync(string tenantNId, string actorUserNId, string action, string scope, CancellationToken cancellationToken)
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

    public async Task UpdateLifecycleAsync(AuditLifecycleUpdate update, CancellationToken cancellationToken)
    {
        var row = await _dbContext.SqlSugar.Queryable<AuditLifecycleTable>()
            .Where(t => t.TenantNId == update.TenantNId && t.ProducerServiceKey == update.ProducerServiceKey && t.AuditEventNId == update.AuditEventNId)
            .FirstAsync(cancellationToken);
        if (row is null) return;
        row.State = update.State;
        row.RetentionUntil = update.RetentionUntil;
        row.ArchivedOn = update.State == "Archived" ? update.ChangedOn : row.ArchivedOn;
        row.DeletedOn = update.State == "Deleted" ? update.ChangedOn : row.DeletedOn;
        row.LegalHold = update.LegalHold ?? row.LegalHold;
        if (update.LegalHold == false) row.LegalHoldReason = null;
        else if (update.LegalHoldReason is not null) row.LegalHoldReason = Limit(update.LegalHoldReason);
        row.ChangedOn = update.ChangedOn;
        await _dbContext.SqlSugar.Updateable(row).Where(t => t.Id == row.Id).ExecuteCommandAsync(cancellationToken);
    }

    public async Task RecordLifecycleAuditAsync(string tenantNId, string producerServiceKey, string auditEventNId, string action, string reason, CancellationToken cancellationToken)
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

    public async Task<int> CleanupAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var sugar = _dbContext.SqlSugar;
        var ownsTransaction = sugar.Ado.IsNoTran();
        if (ownsTransaction) sugar.Ado.BeginTran();
        try
        {
            var candidates = await sugar.Queryable<AuditLifecycleTable>()
                .Where(t => t.State == "Deleted" && t.RetentionUntil != null && t.RetentionUntil <= now)
                .Take(500)
                .ToListAsync(cancellationToken);
            var removed = 0;
            foreach (var candidate in candidates)
            {
                if (!AuditLifecyclePolicy.CanDelete(candidate.State, candidate.RetentionUntil, candidate.LegalHold, now))
                {
                    await RecordLifecycleAuditAsync(candidate.TenantNId, candidate.ProducerServiceKey, candidate.AuditEventNId, "audit.cleanup.control-skip", candidate.LegalHoldReason ?? "cleanup policy rejected deletion", cancellationToken);
                    continue;
                }

                var deletedFacts = await sugar.Deleteable<AuditFactTable>()
                    .Where(t => t.TenantNId == candidate.TenantNId && t.ProducerServiceKey == candidate.ProducerServiceKey && t.AuditEventNId == candidate.AuditEventNId)
                    .ExecuteCommandAsync(cancellationToken);
                if (deletedFacts == 0)
                {
                    // Keep a lifecycle row without a fact as a durable orphan marker.
                    await RecordLifecycleAuditAsync(candidate.TenantNId, candidate.ProducerServiceKey, candidate.AuditEventNId, "audit.cleanup.orphan-skip", "审计事实不存在，保留生命周期记录。", cancellationToken);
                    continue;
                }

                var deletedLifecycle = await sugar.Deleteable<AuditLifecycleTable>()
                    .Where(t => t.Id == candidate.Id)
                    .ExecuteCommandAsync(cancellationToken);
                if (deletedLifecycle != 1) throw new InvalidOperationException("审计生命周期清理未能删除对应生命周期记录。");

                await RecordLifecycleAuditAsync(candidate.TenantNId, candidate.ProducerServiceKey, candidate.AuditEventNId, "audit.cleanup.deleted", "审计事实与生命周期记录已在同一事务中清理。", cancellationToken);
                removed++;
            }

            if (ownsTransaction) sugar.Ado.CommitTran();
            return removed;
        }
        catch
        {
            if (ownsTransaction) sugar.Ado.RollbackTran();
            throw;
        }
    }

    public async Task<bool> RecoverOutboxAsync(string tenantNId, Guid eventId, CancellationToken cancellationToken) =>
        await _dbContext.SqlSugar.Updateable<AuditOutboxTable>()
            .SetColumns(t => new AuditOutboxTable { RetryCount = 0, LastError = null, NextAttemptOn = DateTimeOffset.UtcNow, DeadOn = null })
            .Where(t => t.TenantNId == tenantNId && t.EventId == eventId && t.PublishedOn == null)
            .ExecuteCommandAsync(cancellationToken) == 1;

    public async Task<AuditFactPageV1> QueryAsync(string tenantNId, string? producerServiceKey, string? action, DateTimeOffset? from, DateTimeOffset? until, int page, int pageSize, CancellationToken cancellationToken)
    {
        var query = _dbContext.SqlSugar.Queryable<AuditFactTable>().Where(t => t.TenantNId == tenantNId);
        if (!string.IsNullOrWhiteSpace(producerServiceKey)) query = query.Where(t => t.ProducerServiceKey == producerServiceKey);
        if (!string.IsNullOrWhiteSpace(action)) query = query.Where(t => t.Action == action);
        if (from is not null) query = query.Where(t => t.OccurredOn >= from);
        if (until is not null) query = query.Where(t => t.OccurredOn <= until);
        var total = await query.CountAsync(cancellationToken);
        var rows = await query.OrderBy(t => t.OccurredOn, SqlSugar.OrderByType.Desc).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        return new AuditFactPageV1 { Items = rows.Select(row => ToContract(ToRecord(row)!)).ToArray(), Page = page, PageSize = pageSize, Total = total };
    }

    private static FileUploadSessionTable ToTable(FileUploadSessionRecord value) => ToTable(value, Guid.NewGuid());
    private static FileUploadSessionTable ToTable(FileUploadSessionRecord value, Guid id) => new() { Id = id, TenantNId = value.TenantNId, SessionNId = value.SessionNId, TransportId = value.TransportId, UploaderUserNId = value.UploaderUserNId, Purpose = value.Purpose, FileName = value.FileName, ContentType = value.ContentType, ExpectedLength = value.ExpectedLength, ExpectedSha256 = value.ExpectedSha256, SampleFingerprint = value.SampleFingerprint, CurrentOffset = value.CurrentOffset, WriterEpoch = value.WriterEpoch, Status = value.Status, ExpiresOn = value.ExpiresOn, CompletedOn = value.CompletedOn, FileNId = value.FileNId, ErrorCode = value.ErrorCode, CreatedOn = value.CreatedOn, LastUpdatedOn = value.LastUpdatedOn };
    private static FileUploadSessionRecord? ToRecord(FileUploadSessionTable? value) => value is null ? null : new(value.TenantNId, value.SessionNId, value.TransportId, value.UploaderUserNId, value.Purpose, value.FileName, value.ContentType, value.ExpectedLength, value.ExpectedSha256, value.SampleFingerprint, value.CurrentOffset, value.WriterEpoch, value.Status, value.ExpiresOn, value.CompletedOn, value.FileNId, value.ErrorCode, value.CreatedOn, value.LastUpdatedOn);
    private static FileObjectTable ToTable(FileObjectRecord value) => ToTable(value, Guid.NewGuid());
    private static FileObjectTable ToTable(FileObjectRecord value, Guid id) => new() { Id = id, TenantNId = value.TenantNId, FileNId = value.FileNId, UploadSessionNId = value.UploadSessionNId, OwnerUserNId = value.OwnerUserNId ?? string.Empty, FileName = value.FileName, ContentType = value.ContentType, ContentLength = value.ContentLength, Sha256 = value.Sha256, StorageKey = value.StorageKey, ScanStatus = value.ScanStatus, Restricted = value.Restricted, DeletionStatus = value.DeletionStatus, CreatedOn = value.CreatedOn, LastUpdatedOn = value.LastUpdatedOn, DeletedOn = value.DeletedOn, RetentionUntil = value.RetentionUntil };
    private static FileObjectRecord? ToRecord(FileObjectTable? value) => value is null ? null : new(value.TenantNId, value.FileNId, value.UploadSessionNId, value.FileName, value.ContentType, value.ContentLength, value.Sha256, value.StorageKey, value.ScanStatus, value.Restricted, value.DeletionStatus, value.CreatedOn, value.LastUpdatedOn, value.DeletedOn, value.RetentionUntil, value.OwnerUserNId);
    private static FileReferenceGrantTable ToTable(FileReferenceRecord value) => new() { Id = Guid.NewGuid(), TenantNId = value.TenantNId, ReferenceNId = value.ReferenceNId, FileNId = value.FileNId, OwnerUserNId = value.OwnerUserNId, Purpose = value.Purpose, CreatedOn = value.CreatedOn, DeletedOn = value.DeletedOn };
    private static FileReferenceRecord? ToRecord(FileReferenceGrantTable? value) => value is null ? null : new(value.TenantNId, value.ReferenceNId, value.FileNId, value.OwnerUserNId, value.Purpose, value.CreatedOn, value.DeletedOn);
    private static NotificationAnnouncementTable ToTable(AnnouncementRecord value) => ToTable(value, Guid.NewGuid());
    private static NotificationAnnouncementTable ToTable(AnnouncementRecord value, Guid id) => new() { Id = id, TenantNId = value.TenantNId, AnnouncementNId = value.AnnouncementNId, Title = value.Title, Body = value.Body, Priority = value.Priority, Status = value.Status, AudienceUserNIdsJson = JsonSerializer.Serialize(value.RecipientUserNIds), PublishedOn = value.PublishedOn, ExpiresOn = value.ExpiresOn, CreatedByUserNId = value.CreatedByUserNId, CreatedOn = value.CreatedOn, LastUpdatedOn = value.LastUpdatedOn, RevokedOn = value.RevokedOn, ResourceNId = value.ResourceNId, TargetRoute = value.TargetRoute, IdempotencyKey = value.IdempotencyKey };
    private static AnnouncementRecord? ToRecord(NotificationAnnouncementTable? value) => value is null ? null : new(value.TenantNId, value.AnnouncementNId, value.Title, value.Body, value.Priority, value.Status, JsonSerializer.Deserialize<string[]>(value.AudienceUserNIdsJson) ?? [], value.PublishedOn, value.ExpiresOn, value.CreatedByUserNId, value.CreatedOn, value.LastUpdatedOn, value.RevokedOn, value.ResourceNId, value.TargetRoute, value.IdempotencyKey);
    private static NotificationMessageTable ToTable(NotificationMessageRecord value) => new() { Id = Guid.NewGuid(), TenantNId = value.TenantNId, NotificationNId = value.NotificationNId, AnnouncementNId = value.AnnouncementNId, Kind = value.Kind, Title = value.Title, Body = value.Body, SenderUserNId = value.SenderUserNId, CreatedOn = value.CreatedOn, ExpiresOn = value.ExpiresOn, RevokedOn = value.RevokedOn, ResourceNId = value.ResourceNId, TargetRoute = value.TargetRoute, IdempotencyKey = value.IdempotencyKey };
    private static NotificationMessageRecord? ToRecord(NotificationMessageTable? value) => value is null ? null : new(value.TenantNId, value.NotificationNId, value.AnnouncementNId, value.Kind, value.Title, value.Body, value.SenderUserNId, value.CreatedOn, value.ExpiresOn, value.RevokedOn, value.ResourceNId, value.TargetRoute, value.IdempotencyKey);
    private static NotificationInboxDeliveryTable ToTable(InboxDeliveryRecord value) => new() { Id = Guid.NewGuid(), TenantNId = value.TenantNId, NotificationNId = value.NotificationNId, RecipientUserNId = value.RecipientUserNId, DeliveredOn = value.DeliveredOn, ReadOn = value.ReadOn, ExpiresOn = value.ExpiresOn, RevokedOn = value.RevokedOn };
    private static NotificationInboxItemV1 ToContract(NotificationMessageTable message, NotificationInboxDeliveryTable delivery) => new() { NotificationNId = message.NotificationNId, Kind = message.Kind, Title = message.Title, Body = message.Body, SenderUserNId = message.SenderUserNId, IsRead = delivery.ReadOn is not null, DeliveredOn = delivery.DeliveredOn, ReadOn = delivery.ReadOn, ExpiresOn = delivery.ExpiresOn, ResourceNId = message.ResourceNId, TargetRoute = message.TargetRoute };
    private static AuditFactTable ToTable(AuditFactRecord value) => new() { Id = Guid.NewGuid(), TenantNId = value.TenantNId, ProducerServiceKey = value.ProducerServiceKey, AuditEventNId = value.AuditEventNId, OccurredOn = value.OccurredOn, ReceivedOn = value.ReceivedOn, ActorUserNId = value.ActorUserNId, Action = value.Action, ObjectType = value.ObjectType, ObjectNId = value.ObjectNId, PayloadJson = value.PayloadJson, PayloadHash = value.PayloadHash, TraceId = value.TraceId, Severity = value.Severity, SourceIp = value.SourceIp, UserAgent = value.UserAgent };
    private static AuditFactRecord? ToRecord(AuditFactTable? value) => value is null ? null : new(value.TenantNId, value.ProducerServiceKey, value.AuditEventNId, value.OccurredOn, value.ReceivedOn, value.ActorUserNId, value.Action, value.ObjectType, value.ObjectNId, value.PayloadJson, value.PayloadHash, value.TraceId, value.Severity, value.SourceIp, value.UserAgent);
    private static AuditLifecycleRecord? ToLifecycleRecord(AuditLifecycleTable? value) => value is null ? null : new(value.TenantNId, value.ProducerServiceKey, value.AuditEventNId, value.State, value.RetentionUntil, value.LegalHold, value.LegalHoldReason, value.ChangedOn);
    private static FileObjectV1 ToContract(FileObjectRecord value) => new() { TenantNId = value.TenantNId, FileNId = value.FileNId, FileName = value.FileName, ContentType = value.ContentType, Length = value.ContentLength, Sha256 = value.Sha256, ScanStatus = value.ScanStatus, Restricted = value.Restricted, DeletionStatus = value.DeletionStatus, CreatedOn = value.CreatedOn, RetentionUntil = value.RetentionUntil };
    private static AuditFactV1 ToContract(AuditFactRecord value) => new() { TenantNId = value.TenantNId, ProducerServiceKey = value.ProducerServiceKey, AuditEventNId = value.AuditEventNId, OccurredOn = value.OccurredOn, ReceivedOn = value.ReceivedOn, ActorUserNId = value.ActorUserNId, Action = value.Action, ObjectType = value.ObjectType, ObjectNId = value.ObjectNId, PayloadJson = value.PayloadJson, TraceId = value.TraceId ?? string.Empty, Severity = value.Severity };

    private static string? Limit(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim()[..Math.Min(value.Trim().Length, 1000)];

    private static FileObjectRecord? RollbackAndReturn(ISqlSugarClient sugar, FileObjectRecord? value, bool ownsTransaction)
    {
        if (ownsTransaction) sugar.Ado.RollbackTran();
        return value;
    }
}
