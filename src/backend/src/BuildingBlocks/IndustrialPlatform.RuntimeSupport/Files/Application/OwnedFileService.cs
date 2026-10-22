using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IndustrialPlatform.Infrastructure.Database;
using IndustrialPlatform.RuntimeSupport.Persistence;
using IndustrialPlatform.SystemData.Application.Auditing;
using IndustrialPlatform.SystemData.Application.Files;
using IndustrialPlatform.SystemData.Application.Operations;
using IndustrialPlatform.SystemData.Contracts.Files;
using IndustrialPlatform.SystemData.Domain.Files;
using Microsoft.AspNetCore.DataProtection;

namespace IndustrialPlatform.RuntimeSupport.Files;

/// <summary>唯一owned状态机。调用者必须是服务端已完成资源授权的受信适配器。</summary>
public sealed class OwnedFileService(SqlSugarDbContext database, IFileService files, IFileStore store,
    IFileContentStore content, FileAuditOperationalStore persistence, ISystemDataWriteTransaction transaction,
    ILocalAuditCommand audit, IFileUploadCoordinator coordinator, IDataProtectionProvider protection, TimeProvider clock) : IOwnedFileService
{
    private readonly IDataProtector _tickets = protection.CreateProtector("RuntimeSupport.OwnedFile.Read.v1");
    private static readonly HashSet<string> Kinds = ["ReportArtifact", "ReportPreview", "DetailPreview", "WorkbookVersion", "StudioAsset"];

    public async Task<OwnedFilePreparedV1> PrepareAsync(TrustedServiceContext context, OwnedFilePrepareV1 request, CancellationToken cancellationToken)
    {
        Validate(context);
        if (request.OwnerService != context.ServiceKey || !Kinds.Contains(request.OwnerKind)) throw Error("FILE_OWNER_FORBIDDEN", 403);
        Key(request.OperationKey); Key(request.OwnerNId);
        var hash = FileUploadRules.NormalizeSha256(request.ExpectedSha256);
        var id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new[] { context.TenantNId, context.ServiceKey, request.OperationKey })))).ToLowerInvariant();
        var sessionNId = "owned-upload-" + id;
        await using var lease = await coordinator.AcquireAsync(context.TenantNId, sessionNId, cancellationToken);
        var existing = await database.SqlSugar.Queryable<OwnedFileReferenceTable>().Where(row => row.TenantNId == context.TenantNId && row.OwnerService == context.ServiceKey && row.OperationKey == request.OperationKey).FirstAsync(cancellationToken);
        if (existing is not null)
        {
            var session = await store.GetSessionAsync(context.TenantNId, sessionNId, cancellationToken);
            if (existing.OwnerKind != request.OwnerKind || existing.OwnerNId != request.OwnerNId || existing.Status == "Released"
                || session is null || session.UploaderUserNId != context.ActorUserNId || session.FileName != FileUploadRules.SanitizeFileName(request.FileName)
                || session.ContentType != request.ContentType || session.ExpectedLength != request.ExpectedLength || session.ExpectedSha256 != hash) throw Error("FILE_OPERATION_CONFLICT", 409);
            return new(sessionNId, existing.ReferenceNId, session.ExpiresOn);
        }
        var now = clock.GetUtcNow();
        var reference = new OwnedFileReferenceTable { TenantNId = context.TenantNId, ReferenceNId = "owned-" + id, OwnerService = context.ServiceKey,
            OwnerKind = request.OwnerKind, OwnerNId = request.OwnerNId, OperationKey = request.OperationKey, Status = "Pending", CreatedOn = now,
            LastUpdatedOn = now, ExpiresOn = now.AddHours(24) };
        await transaction.ExecuteAsync(async () =>
        {
            await files.CreateSessionAsync(context.TenantNId, context.ActorUserNId, new CreateUploadSessionRequest { SessionNId = sessionNId, FileName = request.FileName,
                ContentType = request.ContentType, Length = request.ExpectedLength, Sha256 = hash, Purpose = "studio.owned." + request.OwnerKind }, cancellationToken);
            await persistence.SetOwnerContextAsync(context.TenantNId, sessionNId, JsonSerializer.Serialize(new { producerServiceKey = context.ServiceKey, referenceNId = reference.ReferenceNId, actorUserNId = context.ActorUserNId, tenantNId = context.TenantNId }), cancellationToken);
            await database.SqlSugar.Insertable(reference).ExecuteCommandAsync(cancellationToken);
            await Record(context, "file.owned.prepare", reference.ReferenceNId, cancellationToken);
        }, cancellationToken);
        var persisted = await store.GetSessionAsync(context.TenantNId, sessionNId, cancellationToken) ?? throw Error("FILE_UPLOAD_NOT_FOUND", 404);
        return new(sessionNId, reference.ReferenceNId, persisted.ExpiresOn);
    }

    public async Task<OwnedFileStatusV1> CommitAsync(TrustedServiceContext context, OwnedFileCommitV1 request, CancellationToken cancellationToken)
    {
        var row = await Require(context, request.ReferenceNId, cancellationToken);
        var file = await store.GetFileAsync(context.TenantNId, request.FileNId, cancellationToken) ?? throw Error("FILE_NOT_FOUND", 404);
        var session = await store.GetSessionAsync(context.TenantNId, file.UploadSessionNId, cancellationToken);
        var expected = "owned-upload-" + row.ReferenceNId[6..];
        if (session is null || session.SessionNId != expected || session.UploaderUserNId != context.ActorUserNId || session.Status != "Completed"
            || file.Sha256 != FileUploadRules.NormalizeSha256(request.Sha256) || file.ContentLength != session.ExpectedLength || file.Sha256 != session.ExpectedSha256
            || file.DeletionStatus != "Active" || row.Status == "Released" || row.FileNId is not null && row.FileNId != file.FileNId) throw Error("FILE_OWNED_COMMIT_CONFLICT", 409);
        if (file.ScanStatus != "Clean" || file.Restricted) return new(row.ReferenceNId, row.FileNId, "Quarantined", row.Version.ToString(CultureInfo.InvariantCulture), row.ExpiresOn);
        if (row.Status == "Active") return Status(row);
        await transaction.ExecuteAsync(async () =>
        {
            // Atomic file-state check prevents attaching content already selected for deletion.
            var affected = await database.SqlSugar.Ado.ExecuteCommandAsync("UPDATE file_owned_reference SET file_n_id=@file, status='Active', version=version+1, last_updated_on=@now WHERE tenant_n_id=@tenant AND reference_n_id=@reference AND version=@version AND status='Pending' AND EXISTS (SELECT 1 FROM system_file_object f WHERE f.tenant_n_id=@tenant AND f.file_n_id=@file AND f.deletion_status='Active' AND f.scan_status='Clean')",
                new { file = file.FileNId, now = clock.GetUtcNow(), tenant = context.TenantNId, reference = row.ReferenceNId, version = row.Version }, cancellationToken);
            if (affected != 1) throw Error("FILE_VERSION_CONFLICT", 409);
            await Record(context, "file.owned.commit", row.ReferenceNId, cancellationToken);
        }, cancellationToken);
        return await GetStatusAsync(context, row.ReferenceNId, cancellationToken);
    }

    public async Task<OwnedFileReadTicketV1> GetReadTicketAsync(TrustedServiceContext context, string referenceNId, CancellationToken cancellationToken)
    {
        var row = await Require(context, referenceNId, cancellationToken);
        var file = await Readable(row, cancellationToken);
        var expiry = clock.GetUtcNow().AddSeconds(30);
        return new(_tickets.Protect(JsonSerializer.Serialize(new ReadClaim(context, referenceNId, file.Sha256, expiry))), expiry);
    }
    public async Task<Stream> ReadAsync(TrustedServiceContext context, OwnedFileReadV1 request, CancellationToken cancellationToken)
    {
        Validate(context);
        ReadClaim? claim;
        try { claim = JsonSerializer.Deserialize<ReadClaim>(_tickets.Unprotect(request.AuthorizationTicket)); }
        catch (Exception exception) when (exception is CryptographicException or JsonException or ArgumentException) { throw Error("FILE_READ_TICKET_INVALID", 403); }
        if (claim is null || claim.Context != context || request.ActorUserNId != context.ActorUserNId || claim.Reference != request.ReferenceNId || claim.Expires <= clock.GetUtcNow() || claim.Expires > clock.GetUtcNow().AddSeconds(30)) throw Error("FILE_READ_TICKET_INVALID", 403);
        var row = await Require(context, request.ReferenceNId, cancellationToken);
        var file = await Readable(row, cancellationToken);
        if (claim.Sha256 != file.Sha256) throw Error("FILE_READ_TICKET_INVALID", 403);
        await Record(context, "file.owned.read", row.ReferenceNId, cancellationToken);
        return await content.OpenReadAsync(file.StorageKey, cancellationToken);
    }
    public async Task<OwnedFileStatusV1> GetStatusAsync(TrustedServiceContext context, string referenceNId, CancellationToken cancellationToken) => Status(await Require(context, referenceNId, cancellationToken));
    public async Task<OwnedFileStatusV1> ReleaseAsync(TrustedServiceContext context, OwnedFileReleaseV1 request, CancellationToken cancellationToken)
    {
        var row = await Require(context, request.ReferenceNId, cancellationToken);
        if (row.Status == "Released") { if (Version(request.ExpectedVersion) != row.Version && Version(request.ExpectedVersion) != row.Version - 1) throw Error("FILE_VERSION_CONFLICT", 409); return Status(row); }
        if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Length > 512) throw Error("FILE_RELEASE_REASON_REQUIRED", 422);
        if (row.LegalHold || row.RetentionUntil > clock.GetUtcNow() || row.FileNId is not null && await store.HasActiveLegalHoldsAsync(context.TenantNId, row.FileNId, cancellationToken)) throw Error("FILE_RETENTION_ACTIVE", 409);
        if (row.Status == "Pending")
        {
            var session = await store.GetSessionAsync(context.TenantNId, "owned-upload-" + row.ReferenceNId[6..], cancellationToken);
            if (row.CreatedOn.AddHours(24) > clock.GetUtcNow()) throw Error("FILE_PENDING_RETENTION_ACTIVE", 409);
            if (session is not null && session.Status is not ("Completed" or "Cancelled" or "Expired") && (session.ExpiresOn > clock.GetUtcNow() || session.LastUpdatedOn > clock.GetUtcNow().AddHours(-24))) throw Error("FILE_UPLOAD_ACTIVE", 409);
            if (row.FileNId is not null)
            {
                var pendingFile = await store.GetFileAsync(context.TenantNId, row.FileNId, cancellationToken);
                if (pendingFile is not null && pendingFile.ScanStatus is not ("Clean" or "Malicious")) throw Error("FILE_SCAN_UNCONFIRMED", 409);
            }
        }
        await Change(context, row, request.ExpectedVersion, () => { row.Status = "Released"; row.ReleasedOn = clock.GetUtcNow(); }, "file.owned.release", cancellationToken);
        return Status(row);
    }
    public async Task<OwnedFileStatusV1> SetRetentionAsync(TrustedServiceContext context, OwnedFileRetentionV1 request, CancellationToken cancellationToken)
    {
        var row = await Require(context, request.ReferenceNId, cancellationToken);
        if (row.Status == "Released" || request.LegalHold && string.IsNullOrWhiteSpace(request.HoldReason) || request.HoldReason?.Length > 512) throw Error("FILE_RETENTION_INVALID", 422);
        await Change(context, row, request.ExpectedVersion, () => { row.RetentionUntil = request.RetentionUntil?.ToUniversalTime(); row.LegalHold = request.LegalHold; row.HoldReason = request.LegalHold ? request.HoldReason : null; }, "file.owned.retention", cancellationToken);
        return Status(row);
    }
    public async Task<OwnedFileListV1> ListByOwnerAsync(TrustedServiceContext context, string ownerKind, string ownerNId, string? cursor, CancellationToken cancellationToken)
    {
        Validate(context); Key(ownerNId);
        if (!Kinds.Contains(ownerKind) || cursor?.Length > 128) throw Error("FILE_OWNER_INVALID", 422);
        var query = database.SqlSugar.Queryable<OwnedFileReferenceTable>().Where(row => row.TenantNId == context.TenantNId && row.OwnerService == context.ServiceKey && row.OwnerKind == ownerKind && row.OwnerNId == ownerNId);
        if (cursor is not null) query = query.Where("reference_n_id > @cursor", new { cursor });
        var rows = await query.OrderBy(row => row.ReferenceNId).Take(201).ToListAsync(cancellationToken);
        return new(rows.Take(200).Select(row => Status(Normalize(row))).ToArray(), rows.Count > 200 ? rows[199].ReferenceNId : null);
    }
    private async Task Change(TrustedServiceContext context, OwnedFileReferenceTable row, string expectedVersion, Action mutate, string action, CancellationToken ct)
    {
        var expected = Version(expectedVersion);
        if (row.Version != expected) throw Error("FILE_VERSION_CONFLICT", 409);
        mutate(); row.Version = checked(expected + 1); row.LastUpdatedOn = clock.GetUtcNow();
        await transaction.ExecuteAsync(async () =>
        {
            if (await database.SqlSugar.Updateable(row).Where(value => value.TenantNId == context.TenantNId && value.ReferenceNId == row.ReferenceNId && value.Version == expected).ExecuteCommandAsync(ct) != 1) throw Error("FILE_VERSION_CONFLICT", 409);
            await Record(context, action, row.ReferenceNId, ct);
        }, ct);
    }
    private async Task<OwnedFileReferenceTable> Require(TrustedServiceContext context, string reference, CancellationToken ct)
    {
        Validate(context); Key(reference);
        return Normalize(await database.SqlSugar.Queryable<OwnedFileReferenceTable>().Where(row => row.TenantNId == context.TenantNId && row.OwnerService == context.ServiceKey && row.ReferenceNId == reference).FirstAsync(ct) ?? throw Error("FILE_OWNED_NOT_FOUND", 404));
    }
    private OwnedFileReferenceTable Normalize(OwnedFileReferenceTable row)
    {
        if (database.DbType == SqlSugar.DbType.Sqlite)
        {
            static DateTimeOffset Utc(DateTimeOffset time) => new(DateTime.SpecifyKind(time.DateTime, DateTimeKind.Utc));
            row.CreatedOn = Utc(row.CreatedOn); row.LastUpdatedOn = Utc(row.LastUpdatedOn);
            row.ExpiresOn = row.ExpiresOn is { } expiry ? Utc(expiry) : null;
            row.ReleasedOn = row.ReleasedOn is { } released ? Utc(released) : null;
            row.RetentionUntil = row.RetentionUntil is { } retention ? Utc(retention) : null;
        }
        return row;
    }
    private async Task<FileObjectRecord> Readable(OwnedFileReferenceTable row, CancellationToken ct)
    {
        var file = row.FileNId is null ? null : await store.GetFileAsync(row.TenantNId, row.FileNId, ct);
        if (row.Status != "Active" || file is null || file.ScanStatus != "Clean" || file.Restricted || file.DeletionStatus != "Active") throw Error("FILE_CONTENT_UNAVAILABLE", 409);
        return file;
    }
    private Task Record(TrustedServiceContext context, string action, string reference, CancellationToken ct) => audit.RecordAsync(new(context.TenantNId, context.ActorUserNId, action, "OwnedFileReference", reference, null, null, null, ""), ct);
    private static void Validate(TrustedServiceContext context) { Key(context.TenantNId); Key(context.ActorUserNId); if (context.ServiceKey != "platform-studio") throw Error("FILE_OWNER_FORBIDDEN", 403); }
    private static void Key(string value) { if (string.IsNullOrWhiteSpace(value) || value.Length > 128 || value.Any(char.IsControl)) throw Error("FILE_OWNER_INVALID", 422); }
    private static long Version(string value) => long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var version) && version >= 0 ? version : throw Error("FILE_VERSION_INVALID", 422);
    private static OwnedFileStatusV1 Status(OwnedFileReferenceTable row) => new(row.ReferenceNId, row.FileNId, row.Status, row.Version.ToString(CultureInfo.InvariantCulture), row.ExpiresOn);
    private static SystemDataOperationException Error(string code, int status) => new(code, code, status);
    private sealed record ReadClaim(TrustedServiceContext Context, string Reference, string Sha256, DateTimeOffset Expires);
}
