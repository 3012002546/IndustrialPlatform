using System.Text.Json;
using IndustrialPlatform.Infrastructure.Database;
using IndustrialPlatform.SystemData.Application.Auditing;
using IndustrialPlatform.SystemData.Application.Files;
using IndustrialPlatform.SystemData.Contracts.Files;
using IndustrialPlatform.SystemData.Contracts.Auditing;
using IndustrialPlatform.SystemData.Infrastructure.Persistence.Entities;
using SqlSugar;
using IndustrialPlatform.RuntimeSupport.Files;

namespace IndustrialPlatform.RuntimeSupport.Persistence;

/// <summary>文件/审计同库持久化；不查询通知或聊天控制面表。平台扩展通过虚方法保留自身引用检查。</summary>
public class FileAuditOperationalStore : IFileStore, IFileStatusOutbox, IAuditStore, IAuditFilteredStore
{
    private readonly SqlSugarDbContext _dbContext;
    public FileAuditOperationalStore(SqlSugarDbContext dbContext) => _dbContext = dbContext;
    public async Task SetOwnerContextAsync(string tenantNId, string sessionNId, string ownerContextJson, CancellationToken cancellationToken) =>
        await _dbContext.SqlSugar.Updateable<FileUploadSessionTable>().SetColumns(row => new FileUploadSessionTable { OwnerContextJson = ownerContextJson }).Where(row => row.TenantNId == tenantNId && row.SessionNId == sessionNId && row.OwnerContextJson == null).ExecuteCommandAsync(cancellationToken);

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

    public async Task<FileObjectRecord?> GetFileForReconciliationAsync(string tenantNId, string fileNId, CancellationToken cancellationToken) =>
        ToRecord(await _dbContext.SqlSugar.Queryable<FileObjectTable>().Where(t => t.TenantNId == tenantNId && t.FileNId == fileNId).FirstAsync(cancellationToken));

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
        var sugar = _dbContext.SqlSugar;
        var ownsTransaction = sugar.Ado.IsNoTran();
        if (ownsTransaction) sugar.Ado.BeginTran();
        try
        {
            var affected = await sugar.Updateable<FileObjectTable>()
                .SetColumns(t => new FileObjectTable { DeletionStatus = "Deleted", DeletedOn = deletedOn, LastUpdatedOn = deletedOn })
                .Where(t => t.TenantNId == file.TenantNId && t.FileNId == file.FileNId && t.DeletionStatus == "DeletionRequested")
                .ExecuteCommandAsync(cancellationToken);
            if (affected == 1)
            {
                await EnqueueFileStatusAsync(new FileStatusChangeRecord(Guid.NewGuid(), file.TenantNId, file.FileNId, file.ScanStatus, file.Restricted, "Deleted", deletedOn), cancellationToken);
            }
            if (ownsTransaction) sugar.Ado.CommitTran();
        }
        catch
        {
            if (ownsTransaction) sugar.Ado.RollbackTran();
            throw;
        }
    }

    public async Task<FilePageV1> ListFilesAsync(string tenantNId, string? search, int page, int pageSize, CancellationToken cancellationToken)
    {
        return await ListFilesPageAsync(tenantNId, search, null, null, null, null, page, pageSize, cancellationToken);
    }

    public async Task<FilePageV1> ListFilesPageAsync(string tenantNId, string? search, string? purpose, string? ownerUserNId, string? scanStatus, bool? restricted, int page, int pageSize, CancellationToken cancellationToken)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 200);
        var query = _dbContext.SqlSugar.Queryable<FileObjectTable>().Where(t => t.TenantNId == tenantNId && t.DeletionStatus != "Deleted");
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(t => t.FileNId.Contains(term) || t.FileName.Contains(term));
        }
        if (!string.IsNullOrWhiteSpace(ownerUserNId)) query = query.Where(t => t.OwnerUserNId == ownerUserNId.Trim());
        if (!string.IsNullOrWhiteSpace(scanStatus)) query = query.Where(t => t.ScanStatus == scanStatus.Trim());
        if (restricted is not null) query = query.Where(t => t.Restricted == restricted.Value);
        if (!string.IsNullOrWhiteSpace(purpose))
        {
            var sessionNIds = await _dbContext.SqlSugar.Queryable<FileUploadSessionTable>()
                .Where(t => t.TenantNId == tenantNId && t.Purpose == purpose.Trim())
                .Select(t => t.SessionNId)
                .ToListAsync(cancellationToken);
            if (sessionNIds.Count == 0) return new FilePageV1 { Page = page, PageSize = pageSize, Total = 0 };
            query = query.Where(t => sessionNIds.Contains(t.UploadSessionNId));
        }
        var total = await query.CountAsync(cancellationToken);
        var rows = await query.OrderBy(t => t.CreatedOn, SqlSugar.OrderByType.Desc).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        var fileRecords = rows.Select(row => ToRecord(row)!).ToArray();
        var fileNIds = fileRecords.Select(file => file.FileNId).ToArray();
        var sessionRecords = fileRecords.Length == 0
            ? []
            : (await _dbContext.SqlSugar.Queryable<FileUploadSessionTable>()
                .Where(t => t.TenantNId == tenantNId && fileRecords.Select(file => file.UploadSessionNId).Contains(t.SessionNId))
                .ToListAsync(cancellationToken)).Select(ToRecord).Where(value => value is not null).Select(value => value!).ToArray();
        var referenceRecords = fileNIds.Length == 0
            ? []
            : (await _dbContext.SqlSugar.Queryable<FileReferenceGrantTable>()
                .Where(t => t.TenantNId == tenantNId && fileNIds.Contains(t.FileNId) && t.DeletedOn == null)
                .ToListAsync(cancellationToken)).Select(ToRecord).Where(value => value is not null).Select(value => value!).ToArray();
        var purposes = sessionRecords.ToDictionary(value => value.SessionNId, value => value.Purpose, StringComparer.Ordinal);
        var references = referenceRecords.GroupBy(value => value.FileNId).ToDictionary(
            group => group.Key,
            group => (IReadOnlyList<FileReferenceSummaryRecord>)group.Select(value => new FileReferenceSummaryRecord(value.ReferenceNId, value.OwnerUserNId, value.Purpose, value.CreatedOn)).ToArray(),
            StringComparer.Ordinal);
        var items = fileRecords.Select(file => ToContract(file with
        {
            Purpose = purposes.GetValueOrDefault(file.UploadSessionNId),
            ReferenceSummary = references.GetValueOrDefault(file.FileNId) ?? []
        })).ToArray();
        return new FilePageV1 { Items = items, Page = page, PageSize = pageSize, Total = total };
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
            if (current.OwnerContextJson is not null)
            {
                using var owner = JsonDocument.Parse(current.OwnerContextJson);
                if (owner.RootElement.TryGetProperty("referenceNId", out var reference))
                {
                    var referenceNId = reference.GetString();
                    if (await sugar.Updateable<OwnedFileReferenceTable>().SetColumns(row => new OwnedFileReferenceTable { FileNId = file.FileNId })
                        .Where(row => row.TenantNId == file.TenantNId && row.ReferenceNId == referenceNId && row.Status == "Pending" && row.FileNId == null).ExecuteCommandAsync(cancellationToken) != 1)
                        throw new InvalidOperationException("Owned引用已经释放或绑定其他文件。");
                }
            }
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
            await EnqueueFileStatusAsync(new FileStatusChangeRecord(Guid.NewGuid(), file.TenantNId, file.FileNId, file.ScanStatus, file.Restricted, file.DeletionStatus, file.LastUpdatedOn), cancellationToken);
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

    public async Task InsertReferenceAsync(FileReferenceRecord reference, CancellationToken cancellationToken) =>
        await _dbContext.SqlSugar.Insertable(ToTable(reference)).ExecuteCommandAsync(cancellationToken);

    public async Task DeleteReferenceAsync(string tenantNId, string referenceNId, DateTimeOffset deletedOn, CancellationToken cancellationToken) =>
        await _dbContext.SqlSugar.Updateable<FileReferenceGrantTable>().SetColumns(t => new FileReferenceGrantTable { DeletedOn = deletedOn }).Where(t => t.TenantNId == tenantNId && t.ReferenceNId == referenceNId && t.DeletedOn == null).ExecuteCommandAsync(cancellationToken);

    public async Task UpdateFileAsync(FileObjectRecord file, CancellationToken cancellationToken)
    {
        var sugar = _dbContext.SqlSugar;
        var ownsTransaction = sugar.Ado.IsNoTran();
        if (ownsTransaction) sugar.Ado.BeginTran();
        try
        {
            var existing = await sugar.Queryable<FileObjectTable>()
                .Where(t => t.TenantNId == file.TenantNId && t.FileNId == file.FileNId)
                .FirstAsync(cancellationToken);
            if (existing is null)
            {
                if (ownsTransaction) sugar.Ado.RollbackTran();
                return;
            }
            await sugar.Updateable(ToTable(file, existing.Id))
                .Where(t => t.TenantNId == file.TenantNId && t.FileNId == file.FileNId)
                .ExecuteCommandAsync(cancellationToken);
            await EnqueueFileStatusAsync(new FileStatusChangeRecord(Guid.NewGuid(), file.TenantNId, file.FileNId, file.ScanStatus, file.Restricted, file.DeletionStatus, file.LastUpdatedOn), cancellationToken);
            if (ownsTransaction) sugar.Ado.CommitTran();
        }
        catch
        {
            if (ownsTransaction) sugar.Ado.RollbackTran();
            throw;
        }
    }

    public async Task EnqueueAsync(FileStatusChangeRecord item, CancellationToken cancellationToken) => await EnqueueFileStatusAsync(item, cancellationToken);

    public async Task<IReadOnlyList<FileStatusOutboxRecord>> GetPendingAsync(DateTimeOffset now, int limit, CancellationToken cancellationToken) =>
        (await _dbContext.SqlSugar.Queryable<FileStatusOutboxTable>()
            .Where(item => item.PublishedOn == null && item.DeadLetteredOn == null && (item.NextAttemptOn == null || item.NextAttemptOn <= now))
            .OrderBy(item => item.ObservedOn, OrderByType.Asc)
            .Take(Math.Clamp(limit, 1, 200))
            .ToListAsync(cancellationToken)).Select(item => ToFileStatusOutboxRecord(item)).ToArray();

    public Task MarkPublishedAsync(Guid eventId, DateTimeOffset publishedOn, CancellationToken cancellationToken) =>
        _dbContext.SqlSugar.Updateable<FileStatusOutboxTable>()
            .SetColumns(item => new FileStatusOutboxTable { PublishedOn = publishedOn, LastError = null, NextAttemptOn = null })
            .Where(item => item.EventId == eventId && item.PublishedOn == null && item.DeadLetteredOn == null)
            .ExecuteCommandAsync(cancellationToken);

    public async Task<bool> RecordFailureAsync(Guid eventId, int retryCount, string lastError, bool deadLetter, DateTimeOffset nextAttemptOn, CancellationToken cancellationToken) =>
        await _dbContext.SqlSugar.Updateable<FileStatusOutboxTable>()
            .SetColumns(item => new FileStatusOutboxTable
            {
                RetryCount = Math.Max(0, retryCount),
                LastError = lastError.Length > 2000 ? lastError.Substring(0, 2000) : lastError,
                NextAttemptOn = deadLetter ? null : nextAttemptOn,
                DeadLetteredOn = deadLetter ? nextAttemptOn : null,
            })
            .Where(item => item.EventId == eventId && item.PublishedOn == null && item.DeadLetteredOn == null)
            .ExecuteCommandAsync(cancellationToken) == 1;

    protected async Task EnqueueFileStatusAsync(FileStatusChangeRecord item, CancellationToken cancellationToken) =>
        await _dbContext.SqlSugar.Insertable(new FileStatusOutboxTable
        {
            EventId = item.EventId,
            TenantNId = item.TenantNId,
            FileNId = item.FileNId,
            ScanStatus = item.ScanStatus,
            Restricted = item.Restricted,
            DeletionStatus = item.DeletionStatus,
            ObservedOn = item.ObservedOn,
            RetryCount = 0,
        }).ExecuteCommandAsync(cancellationToken);

    private FileStatusOutboxRecord ToFileStatusOutboxRecord(FileStatusOutboxTable item) => new(
        item.EventId,
        item.TenantNId,
        item.FileNId,
        item.ScanStatus,
        item.Restricted,
        item.DeletionStatus,
        ReadUtc(item.ObservedOn),
        item.RetryCount,
        ReadUtc(item.PublishedOn),
        ReadUtc(item.NextAttemptOn),
        item.LastError,
        ReadUtc(item.DeadLetteredOn));

    public async Task<AuditFactRecord?> GetAsync(string tenantNId, string producerServiceKey, string auditEventNId, CancellationToken cancellationToken) =>
        ToRecord(await _dbContext.SqlSugar.Queryable<AuditFactTable>().Where(t => t.TenantNId == tenantNId && t.ProducerServiceKey == producerServiceKey && t.AuditEventNId == auditEventNId).FirstAsync(cancellationToken));

    public async Task<AuditLifecycleRecord?> GetLifecycleAsync(string tenantNId, string producerServiceKey, string auditEventNId, CancellationToken cancellationToken) =>
        ToLifecycleRecord(await _dbContext.SqlSugar.Queryable<AuditLifecycleTable>().Where(t => t.TenantNId == tenantNId && t.ProducerServiceKey == producerServiceKey && t.AuditEventNId == auditEventNId).FirstAsync(cancellationToken));

    public async Task InsertAsync(AuditFactRecord fact, CancellationToken cancellationToken)
    {
        var sugar = _dbContext.SqlSugar;
        var ownsTransaction = sugar.Ado.IsNoTran();
        if (ownsTransaction) sugar.Ado.BeginTran();
        try
        {
            await sugar.Insertable(ToTable(fact)).ExecuteCommandAsync(cancellationToken);
            await sugar.Insertable(new AuditLifecycleTable { Id = Guid.NewGuid(), TenantNId = fact.TenantNId, ProducerServiceKey = fact.ProducerServiceKey, AuditEventNId = fact.AuditEventNId, State = "Active", ChangedOn = fact.ReceivedOn }).ExecuteCommandAsync(cancellationToken);
            if (ownsTransaction) sugar.Ado.CommitTran();
        }
        catch
        {
            if (ownsTransaction) sugar.Ado.RollbackTran();
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

    public async Task<AuditFactPageV1> QueryFilteredAsync(string tenantNId, string? producerServiceKey, string? action, DateTimeOffset from, DateTimeOffset until, string? actor, string? objectNId, int page, int pageSize, CancellationToken cancellationToken)
    {
        var query = _dbContext.SqlSugar.Queryable<AuditFactTable>().Where(row => row.TenantNId == tenantNId && row.OccurredOn >= from && row.OccurredOn <= until);
        if (!string.IsNullOrWhiteSpace(producerServiceKey)) query = query.Where(row => row.ProducerServiceKey == producerServiceKey);
        if (!string.IsNullOrWhiteSpace(action)) query = query.Where(row => row.Action == action);
        if (!string.IsNullOrWhiteSpace(actor)) query = query.Where(row => row.ActorUserNId == actor);
        if (!string.IsNullOrWhiteSpace(objectNId)) query = query.Where(row => row.ObjectNId == objectNId);
        var total = await query.CountAsync(cancellationToken);
        var rows = await query.OrderBy(row => row.OccurredOn, OrderByType.Desc).OrderBy(row => row.Id).Skip(checked((page - 1) * pageSize)).Take(pageSize).ToListAsync(cancellationToken);
        return new() { Items = rows.Select(row => ToContract(ToRecord(row)!)).ToArray(), Page = page, PageSize = pageSize, Total = total };
    }

    private static FileUploadSessionTable ToTable(FileUploadSessionRecord value) => ToTable(value, Guid.NewGuid());
    private static FileUploadSessionTable ToTable(FileUploadSessionRecord value, Guid id) => new() { Id = id, TenantNId = value.TenantNId, SessionNId = value.SessionNId, TransportId = value.TransportId, UploaderUserNId = value.UploaderUserNId, Purpose = value.Purpose, FileName = value.FileName, ContentType = value.ContentType, ExpectedLength = value.ExpectedLength, ExpectedSha256 = value.ExpectedSha256, SampleFingerprint = value.SampleFingerprint, OwnerContextJson = value.OwnerContextJson, CurrentOffset = value.CurrentOffset, WriterEpoch = value.WriterEpoch, Status = value.Status, ExpiresOn = value.ExpiresOn, CompletedOn = value.CompletedOn, FileNId = value.FileNId, ErrorCode = value.ErrorCode, CreatedOn = value.CreatedOn, LastUpdatedOn = value.LastUpdatedOn };
    private FileUploadSessionRecord? ToRecord(FileUploadSessionTable? value) => value is null ? null : new(value.TenantNId, value.SessionNId, value.TransportId, value.UploaderUserNId, value.Purpose, value.FileName, value.ContentType, value.ExpectedLength, value.ExpectedSha256, value.SampleFingerprint, value.CurrentOffset, value.WriterEpoch, value.Status, ReadUtc(value.ExpiresOn), ReadUtc(value.CompletedOn), value.FileNId, value.ErrorCode, ReadUtc(value.CreatedOn), ReadUtc(value.LastUpdatedOn), value.OwnerContextJson);
    private static FileObjectTable ToTable(FileObjectRecord value) => ToTable(value, Guid.NewGuid());
    private static FileObjectTable ToTable(FileObjectRecord value, Guid id) => new() { Id = id, TenantNId = value.TenantNId, FileNId = value.FileNId, UploadSessionNId = value.UploadSessionNId, OwnerUserNId = value.OwnerUserNId ?? string.Empty, FileName = value.FileName, ContentType = value.ContentType, ContentLength = value.ContentLength, Sha256 = value.Sha256, StorageKey = value.StorageKey, ScanStatus = value.ScanStatus, Restricted = value.Restricted, DeletionStatus = value.DeletionStatus, CreatedOn = value.CreatedOn, LastUpdatedOn = value.LastUpdatedOn, DeletedOn = value.DeletedOn, RetentionUntil = value.RetentionUntil };
    private FileObjectRecord? ToRecord(FileObjectTable? value) => value is null ? null : new(value.TenantNId, value.FileNId, value.UploadSessionNId, value.FileName, value.ContentType, value.ContentLength, value.Sha256, value.StorageKey, value.ScanStatus, value.Restricted, value.DeletionStatus, ReadUtc(value.CreatedOn), ReadUtc(value.LastUpdatedOn), ReadUtc(value.DeletedOn), ReadUtc(value.RetentionUntil), value.OwnerUserNId);
    private static FileReferenceGrantTable ToTable(FileReferenceRecord value) => new() { Id = Guid.NewGuid(), TenantNId = value.TenantNId, ReferenceNId = value.ReferenceNId, FileNId = value.FileNId, OwnerUserNId = value.OwnerUserNId, Purpose = value.Purpose, CreatedOn = value.CreatedOn, DeletedOn = value.DeletedOn };
    private FileReferenceRecord? ToRecord(FileReferenceGrantTable? value) => value is null ? null : new(value.TenantNId, value.ReferenceNId, value.FileNId, value.OwnerUserNId, value.Purpose, ReadUtc(value.CreatedOn), ReadUtc(value.DeletedOn));
    private static AuditFactTable ToTable(AuditFactRecord value) => new() { Id = Guid.NewGuid(), TenantNId = value.TenantNId, ProducerServiceKey = value.ProducerServiceKey, AuditEventNId = value.AuditEventNId, OccurredOn = value.OccurredOn, ReceivedOn = value.ReceivedOn, ActorUserNId = value.ActorUserNId, Action = value.Action, ObjectType = value.ObjectType, ObjectNId = value.ObjectNId, PayloadJson = value.PayloadJson, PayloadHash = value.PayloadHash, TraceId = value.TraceId, Severity = value.Severity, SourceIp = value.SourceIp, UserAgent = value.UserAgent };
    private AuditFactRecord? ToRecord(AuditFactTable? value) => value is null ? null : new(value.TenantNId, value.ProducerServiceKey, value.AuditEventNId, ReadUtc(value.OccurredOn), ReadUtc(value.ReceivedOn), value.ActorUserNId, value.Action, value.ObjectType, value.ObjectNId, value.PayloadJson, value.PayloadHash, value.TraceId, value.Severity, value.SourceIp, value.UserAgent);
    private AuditLifecycleRecord? ToLifecycleRecord(AuditLifecycleTable? value) => value is null ? null : new(value.TenantNId, value.ProducerServiceKey, value.AuditEventNId, value.State, ReadUtc(value.RetentionUntil), value.LegalHold, value.LegalHoldReason, ReadUtc(value.ChangedOn));
    private static FileObjectV1 ToContract(FileObjectRecord value) => new()
    {
        TenantNId = value.TenantNId,
        FileNId = value.FileNId,
        FileName = value.FileName,
        ContentType = value.ContentType,
        Length = value.ContentLength,
        Sha256 = value.Sha256,
        ScanStatus = value.ScanStatus,
        Restricted = value.Restricted,
        Purpose = value.Purpose,
        OwnerUserNId = value.OwnerUserNId,
        ReferenceCount = value.ReferenceSummary?.Count ?? 0,
        ReferenceSummary = value.ReferenceSummary?.Select(reference => new FileReferenceSummaryV1
        {
            ReferenceNId = reference.ReferenceNId,
            OwnerUserNId = reference.OwnerUserNId,
            Purpose = reference.Purpose,
            CreatedOn = reference.CreatedOn
        }).ToArray() ?? [],
        DeletionStatus = value.DeletionStatus,
        CreatedOn = value.CreatedOn,
        LastUpdatedOn = value.LastUpdatedOn,
        RetentionUntil = value.RetentionUntil
    };
    private static AuditFactV1 ToContract(AuditFactRecord value) => new() { TenantNId = value.TenantNId, ProducerServiceKey = value.ProducerServiceKey, AuditEventNId = value.AuditEventNId, OccurredOn = value.OccurredOn, ReceivedOn = value.ReceivedOn, ActorUserNId = value.ActorUserNId, Action = value.Action, ObjectType = value.ObjectType, ObjectNId = value.ObjectNId, PayloadJson = value.PayloadJson, TraceId = value.TraceId ?? string.Empty, Severity = value.Severity };

    protected static string? Limit(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim()[..Math.Min(value.Trim().Length, 1000)];
    // SQLite stores these UTC timestamps without an offset; SqlSugar materializes them with the machine's local offset.
    private DateTimeOffset ReadUtc(DateTimeOffset value) => _dbContext.DbType == DbType.Sqlite ? new DateTimeOffset(DateTime.SpecifyKind(value.DateTime, DateTimeKind.Utc)) : value;
    private DateTimeOffset? ReadUtc(DateTimeOffset? value) => value is null ? null : ReadUtc(value.Value);

    private static FileObjectRecord? RollbackAndReturn(ISqlSugarClient sugar, FileObjectRecord? value, bool ownsTransaction)
    {
        if (ownsTransaction) sugar.Ado.RollbackTran();
        return value;
    }
    public virtual async Task<bool> TryRequestDeletionAsync(FileObjectRecord file, CancellationToken cancellationToken)
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
                + "AND NOT EXISTS (SELECT 1 FROM file_owned_reference r WHERE r.tenant_n_id = @tenantNId AND r.file_n_id = @fileNId AND (r.status IN ('Pending','Active') OR r.retention_until>@updatedOn)) ",
                new SugarParameter[]
                {
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

    public virtual async Task<bool> HasActiveReferencesAsync(string tenantNId, string fileNId, CancellationToken cancellationToken) =>
        await _dbContext.SqlSugar.Queryable<FileReferenceGrantTable>().AnyAsync(t => t.TenantNId == tenantNId && t.FileNId == fileNId && t.DeletedOn == null, cancellationToken)
        || await _dbContext.SqlSugar.Queryable<OwnedFileReferenceTable>().AnyAsync(t => t.TenantNId == tenantNId && t.FileNId == fileNId && (t.Status == "Pending" || t.Status == "Active" || t.LegalHold), cancellationToken);

    public virtual Task<bool> HasActiveLegalHoldsAsync(string tenantNId, string fileNId, CancellationToken cancellationToken) => Task.FromResult(false);
    public virtual async Task RecordAccessAsync(string tenantNId, string actorUserNId, string action, string scope, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var payload = JsonSerializer.Serialize(new { scope = Limit(scope) });
        var eventNId = "access-" + Guid.NewGuid().ToString("N");
        var hash = IndustrialPlatform.SystemData.Domain.Auditing.AuditPayloadRules.ComputeIdempotencyHash("platform-studio", eventNId, now, actorUserNId, action, "AuditQuery", null, payload, null, "Info", null, null);
        await InsertAsync(new AuditFactRecord(tenantNId, "platform-studio", eventNId, now, now, actorUserNId, action, "AuditQuery", null, payload, hash, null, "Info", null, null), cancellationToken);
    }

    public virtual Task RecordLifecycleAuditAsync(string tenantNId, string producerServiceKey, string auditEventNId, string action, string reason, CancellationToken cancellationToken) =>
        RecordAccessAsync(tenantNId, "audit-lifecycle", action, $"{producerServiceKey}/{auditEventNId}: {reason}", cancellationToken);
}
