using IndustrialPlatform.SystemData.Application.Pf04;
using IndustrialPlatform.SystemData.Application.Auditing;
using IndustrialPlatform.SystemData.Contracts.Files;
using IndustrialPlatform.SystemData.Domain.Files;

namespace IndustrialPlatform.SystemData.Application.Files;

public sealed class FileService : IFileService
{
    private readonly IFileStore _store;
    private readonly IFileContentStore _contentStore;
    private readonly TimeProvider _clock;
    private readonly IUploadResumeTicketService _tickets;
    private readonly IFileUploadCoordinator _uploadCoordinator;
    private readonly ILocalAuditCommand _audit;
    private readonly ISystemDataWriteTransaction _transaction;

    public FileService(IFileStore store, IFileContentStore contentStore, TimeProvider clock, IUploadResumeTicketService? tickets = null, IFileUploadCoordinator? uploadCoordinator = null, ILocalAuditCommand? audit = null, ISystemDataWriteTransaction? transaction = null)
    {
        _store = store;
        _contentStore = contentStore;
        _clock = clock;
        _tickets = tickets ?? new HmacUploadResumeTicketService();
        _uploadCoordinator = uploadCoordinator ?? new NoopFileUploadCoordinator();
        _audit = audit ?? new NoopLocalAuditCommand();
        _transaction = transaction ?? new NoopSystemDataWriteTransaction();
    }

    public async Task<UploadSessionV1> CreateSessionAsync(string tenantNId, string userNId, CreateUploadSessionRequest request, CancellationToken cancellationToken)
    {
        var length = request.Length ?? -1;
        if (length < 0) throw Error("FILE_UPLOAD_LENGTH_REQUIRED", "必须提供非负文件长度。");
        string? sha256;
        string fileName;
        try
        {
            sha256 = FileUploadRules.NormalizeOptionalSha256(request.Sha256);
            fileName = FileUploadRules.SanitizeFileName(request.FileName);
        }
        catch (ArgumentException exception)
        {
            throw Error("FILE_UPLOAD_INPUT_INVALID", exception.Message);
        }
        var now = _clock.GetUtcNow();
        var session = new FileUploadSessionRecord(
            tenantNId,
            NormalizeNId(request.SessionNId, "ups"),
            NormalizeNId(request.TransportId, "tr"),
            userNId,
            NormalizeText(request.Purpose, "default"),
            fileName,
            string.IsNullOrWhiteSpace(request.ContentType) ? "application/octet-stream" : request.ContentType.Trim(),
            length,
            sha256,
            request.SampleFingerprint?.Trim(),
            0,
            1,
            "WaitingForProof",
            now.AddHours(24),
            null,
            null,
            null,
            now,
            now);
        await _transaction.ExecuteAsync(async () =>
        {
            await _store.InsertSessionAsync(session, cancellationToken);
            await _audit.RecordAsync(Audit(tenantNId, userNId, "file.upload-session.create", "FileUploadSession", session.SessionNId, null, null, session.Status), cancellationToken);
        }, cancellationToken);
        return ToContract(session);
    }

    public async Task<FileUploadDiscoveryV1> DiscoverAsync(string tenantNId, string userNId, DiscoverUploadSessionRequest request, CancellationToken cancellationToken)
    {
        var length = request.Length ?? -1;
        if (length < 0 || string.IsNullOrWhiteSpace(request.SampleFingerprint)) throw Error("FILE_UPLOAD_DISCOVERY_INPUT_INVALID", "断点发现需要长度与样本指纹。");
        var candidates = await _store.FindCandidatesAsync(tenantNId, userNId, NormalizeText(request.Purpose, "default"), request.SampleFingerprint.Trim(), length, cancellationToken);
        return new FileUploadDiscoveryV1 { Candidates = candidates.Select(session => ToContract(session)).ToArray() };
    }

    public async Task<UploadSessionV1> GetSessionAsync(string tenantNId, string sessionNId, CancellationToken cancellationToken) =>
        ToContract(await RequireSessionAsync(tenantNId, sessionNId, cancellationToken));

    public async Task<UploadSessionV1> GetSessionAsync(string tenantNId, string userNId, string sessionNId, CancellationToken cancellationToken) =>
        ToContract(await RequireOwnedSessionAsync(tenantNId, sessionNId, userNId, cancellationToken), userNId);

    public async Task<UploadSessionV1> GetSessionByTransportAsync(string tenantNId, string transportId, CancellationToken cancellationToken) =>
        ToContract(await _store.GetSessionByTransportAsync(tenantNId, transportId, cancellationToken) ?? throw Error("FILE_UPLOAD_NOT_FOUND", "上传会话不存在。", 404));

    public async Task<UploadSessionV1> GetSessionByTransportAsync(string tenantNId, string userNId, string transportId, CancellationToken cancellationToken)
    {
        var session = await _store.GetSessionByTransportAsync(tenantNId, transportId, cancellationToken) ?? throw Error("FILE_UPLOAD_NOT_FOUND", "上传会话不存在。", 404);
        EnsureOwner(session, userNId);
        return ToContract(session, userNId);
    }

    public async Task<UploadSessionV1> SetContentHashAsync(string tenantNId, string sessionNId, string userNId, ContentHashRequest request, CancellationToken cancellationToken)
    {
        await using var lease = await _uploadCoordinator.AcquireAsync(tenantNId, sessionNId, cancellationToken);
        var session = await RequireOwnedSessionAsync(tenantNId, sessionNId, userNId, cancellationToken);
        if (session.CurrentOffset != 0) throw Error("FILE_UPLOAD_PROOF_REQUIRED", "上传已开始后不能修改完整性证明。");
        if (session.Status is "Cancelled" or "Completed") throw Error("FILE_UPLOAD_CANCELLED", "上传会话已结束。", 409);
        string sha256;
        try { sha256 = FileUploadRules.NormalizeSha256(request.Sha256); }
        catch (ArgumentException exception) { throw Error("FILE_UPLOAD_INPUT_INVALID", exception.Message); }
        if (session.ExpectedSha256 is not null && !string.Equals(sha256, session.ExpectedSha256, StringComparison.OrdinalIgnoreCase)) throw Error("FILE_UPLOAD_CONTENT_MISMATCH", "完整性证明与会话不一致。");
        var updated = session with { ExpectedSha256 = sha256, Status = "Ready", LastUpdatedOn = _clock.GetUtcNow(), ErrorCode = null };
        await _transaction.ExecuteAsync(async () =>
        {
            if (!await _store.UpdateSessionAsync(updated, session.CurrentOffset, session.WriterEpoch, cancellationToken)) throw Error("FILE_UPLOAD_WRITER_REPLACED", "上传写入者已被替换。", 409);
            await _audit.RecordAsync(Audit(tenantNId, userNId, "file.upload-proof.set", "FileUploadSession", sessionNId, session.Status, updated.Status, updated.ExpectedSha256), cancellationToken);
        }, cancellationToken);
        return ToContract(updated, userNId);
    }

    public async Task<UploadSessionV1> ResumeProofAsync(string tenantNId, string sessionNId, string userNId, ResumeProofRequest request, CancellationToken cancellationToken)
    {
        await using var lease = await _uploadCoordinator.AcquireAsync(tenantNId, sessionNId, cancellationToken);
        var session = await RequireOwnedSessionAsync(tenantNId, sessionNId, userNId, cancellationToken);
        EnsureEpoch(session, request.WriterEpoch);
        if (session.Status is "Cancelled" or "Completed") throw Error("FILE_UPLOAD_CANCELLED", "上传会话已结束。");
        if (session.ExpectedSha256 is null) throw Error("FILE_UPLOAD_PROOF_REQUIRED", "需要先登记完整文件 SHA-256。");
        string proof;
        try { proof = FileUploadRules.NormalizeSha256(request.Proof); }
        catch (ArgumentException exception) { throw Error("FILE_UPLOAD_INPUT_INVALID", exception.Message); }
        if (!string.Equals(proof, session.ExpectedSha256, StringComparison.OrdinalIgnoreCase)) throw Error("FILE_UPLOAD_CONTENT_MISMATCH", "完整性证明与会话不一致。");
        var updated = session with { Status = "Ready", LastUpdatedOn = _clock.GetUtcNow(), ErrorCode = null };
        await _transaction.ExecuteAsync(async () =>
        {
            if (!await _store.UpdateSessionAsync(updated, session.CurrentOffset, session.WriterEpoch, cancellationToken)) throw Error("FILE_UPLOAD_WRITER_REPLACED", "上传写入者已被替换。", 409);
            await _audit.RecordAsync(Audit(tenantNId, userNId, "file.upload-proof.resume", "FileUploadSession", sessionNId, session.Status, updated.Status, updated.CurrentOffset.ToString(System.Globalization.CultureInfo.InvariantCulture)), cancellationToken);
        }, cancellationToken);
        return ToContract(updated, userNId);
    }

    public async Task<UploadSessionV1> TakeoverAsync(string tenantNId, string sessionNId, string userNId, TakeoverUploadRequest request, CancellationToken cancellationToken)
    {
        await using var lease = await _uploadCoordinator.AcquireAsync(tenantNId, sessionNId, cancellationToken);
        var session = await RequireSessionAsync(tenantNId, sessionNId, cancellationToken);
        if (request.ExpectedWriterEpoch is null) throw Error("FILE_UPLOAD_EPOCH_REQUIRED", "接管必须携带当前 writer epoch。", 422);
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey)) throw Error("FILE_UPLOAD_IDEMPOTENCY_REQUIRED", "接管必须携带幂等键。", 422);
        if (session.Status is "Cancelled" or "Completed") throw Error("FILE_UPLOAD_CANCELLED", "上传会话已结束。");
        if (session.ExpectedSha256 is null) throw Error("FILE_UPLOAD_PROOF_REQUIRED", "接管前需要完整文件 SHA-256 证明。");
        string proof;
        try { proof = FileUploadRules.NormalizeSha256(request.Proof); }
        catch (ArgumentException exception) { throw Error("FILE_UPLOAD_INPUT_INVALID", exception.Message); }
        if (!string.Equals(proof, session.ExpectedSha256, StringComparison.OrdinalIgnoreCase)) throw Error("FILE_UPLOAD_CONTENT_MISMATCH", "完整性证明与会话不一致。");
        await _contentStore.EnsureLengthAsync(TransportStorageKey(session), session.CurrentOffset, cancellationToken);
        if (session.WriterEpoch == request.ExpectedWriterEpoch + 1
            && string.Equals(session.UploaderUserNId, userNId, StringComparison.Ordinal)
            && session.Status == "WaitingForProof")
            return ToContract(session, userNId);
        if (request.ExpectedWriterEpoch != session.WriterEpoch) throw Error("FILE_UPLOAD_WRITER_REPLACED", "写入者 epoch 已变化。", 409);
        var updated = session with { UploaderUserNId = userNId, WriterEpoch = session.WriterEpoch + 1, Status = "WaitingForProof", LastUpdatedOn = _clock.GetUtcNow() };
        await _transaction.ExecuteAsync(async () =>
        {
            if (!await _store.UpdateSessionAsync(updated, session.CurrentOffset, session.WriterEpoch, cancellationToken)) throw Error("FILE_UPLOAD_WRITER_REPLACED", "上传写入者已被替换。", 409);
            await _audit.RecordAsync(Audit(tenantNId, userNId, "file.upload.takeover", "FileUploadSession", sessionNId, $"epoch={session.WriterEpoch}", $"epoch={updated.WriterEpoch}", request.IdempotencyKey), cancellationToken);
        }, cancellationToken);
        return ToContract(updated, userNId);
    }

    public Task<UploadSessionV1> PauseAsync(string tenantNId, string sessionNId, string userNId, CancellationToken cancellationToken) => SetStateAsync(tenantNId, sessionNId, userNId, "Paused", null, cancellationToken);

    public Task<UploadSessionV1> ResumeAsync(string tenantNId, string sessionNId, string userNId, ResumeProofRequest request, CancellationToken cancellationToken) =>
        ResumeProofAsync(tenantNId, sessionNId, userNId, request, cancellationToken);

    public Task<UploadSessionV1> CancelAsync(string tenantNId, string sessionNId, string userNId, SetUploadStateRequest request, CancellationToken cancellationToken) => SetStateAsync(tenantNId, sessionNId, userNId, "Cancelled", request.Reason, cancellationToken);

    public Task<UploadSessionV1> AppendAsync(string tenantNId, string transportId, string userNId, long expectedOffset, int epoch, Stream content, CancellationToken cancellationToken) =>
        AppendAsync(tenantNId, transportId, userNId, expectedOffset, epoch, content, null, cancellationToken);

    public async Task<UploadSessionV1> AppendAsync(string tenantNId, string transportId, string userNId, long expectedOffset, int epoch, Stream content, string? resumeTicket, CancellationToken cancellationToken)
    {
        var located = await _store.GetSessionByTransportAsync(tenantNId, transportId, cancellationToken) ?? throw Error("FILE_UPLOAD_NOT_FOUND", "上传会话不存在。", 404);
        await using var lease = await _uploadCoordinator.AcquireAsync(tenantNId, located.SessionNId, cancellationToken);
        var session = await _store.GetSessionByTransportAsync(tenantNId, transportId, cancellationToken) ?? throw Error("FILE_UPLOAD_NOT_FOUND", "上传会话不存在。", 404);
        EnsureOwner(session, userNId);
        EnsureEpoch(session, epoch);
        EnsureWritable(session);
        if (expectedOffset != session.CurrentOffset) throw Error("FILE_UPLOAD_OFFSET_MISMATCH", "上传偏移以服务端为准。", 409);
        if (string.IsNullOrWhiteSpace(resumeTicket) || !_tickets.Validate(resumeTicket, tenantNId, session.SessionNId, userNId, epoch, expectedOffset, _clock.GetUtcNow()))
            throw Error("FILE_UPLOAD_RESUME_TICKET_INVALID", "断点续传票据无效或已过期。", 401);
        var storageKey = TransportStorageKey(session);
        await _contentStore.EnsureLengthAsync(storageKey, expectedOffset, cancellationToken);
        long newOffset;
        try
        {
            newOffset = await _contentStore.AppendAsync(storageKey, expectedOffset, content, cancellationToken);
        }
        catch (InvalidDataException exception)
        {
            throw Error("FILE_UPLOAD_OFFSET_MISMATCH", exception.Message, 409);
        }

        // The database is authoritative. A takeover/pause may have won while
        // the bytes were being written; never leave a stale physical tail.
        var current = await _store.GetSessionByTransportAsync(tenantNId, transportId, cancellationToken);
        if (current is null || current.WriterEpoch != epoch || current.CurrentOffset != expectedOffset || current.Status is not ("Ready" or "Uploading"))
        {
            if (current is not null) await _contentStore.EnsureLengthAsync(storageKey, current.CurrentOffset, cancellationToken);
            throw Error("FILE_UPLOAD_WRITER_REPLACED", "写入者或上传状态已变化。", 409);
        }
        var updated = session with { CurrentOffset = newOffset, LastUpdatedOn = _clock.GetUtcNow() };
        if (!await _store.UpdateAppendAsync(updated, expectedOffset, epoch, session.Status, cancellationToken))
        {
            current = await _store.GetSessionByTransportAsync(tenantNId, transportId, cancellationToken);
            if (current is not null) await _contentStore.EnsureLengthAsync(storageKey, current.CurrentOffset, cancellationToken);
            throw Error("FILE_UPLOAD_WRITER_REPLACED", "上传写入者已被替换。", 409);
        }
        return ToContract(updated, userNId);
    }

    public async Task<FileObjectV1> CompleteAsync(string tenantNId, string sessionNId, string userNId, CancellationToken cancellationToken)
    {
        await using var lease = await _uploadCoordinator.AcquireAsync(tenantNId, sessionNId, cancellationToken);
        var session = await RequireOwnedSessionAsync(tenantNId, sessionNId, userNId, cancellationToken);
        if (session.Status == "Completed" && session.FileNId is not null)
        {
            return ToContract(await _store.GetFileAsync(tenantNId, session.FileNId, cancellationToken) ?? throw Error("FILE_UPLOAD_NOT_FOUND", "文件对象不存在。", 404));
        }

        EnsureWritable(session);
        if (session.CurrentOffset != session.ExpectedLength) throw Error("FILE_UPLOAD_INCOMPLETE", "上传字节数尚未达到预期长度。");
        if (session.ExpectedSha256 is null) throw Error("FILE_UPLOAD_PROOF_REQUIRED", "上传开始前需要完整性证明。");
        await _contentStore.EnsureLengthAsync(TransportStorageKey(session), session.CurrentOffset, cancellationToken);
        if (session.ExpectedLength == 0)
        {
            await using var empty = new MemoryStream();
            await _contentStore.AppendAsync(TransportStorageKey(session), 0, empty, cancellationToken);
        }
        var hash = await _contentStore.ComputeSha256Async(TransportStorageKey(session), cancellationToken);
        if (!string.Equals(hash, session.ExpectedSha256, StringComparison.OrdinalIgnoreCase)) throw Error("FILE_UPLOAD_CONTENT_MISMATCH", "文件 SHA-256 校验失败。");

        var now = _clock.GetUtcNow();
        var file = new FileObjectRecord(tenantNId, NormalizeNId(null, "file"), session.SessionNId, session.FileName, session.ContentType, session.ExpectedLength, hash, TransportStorageKey(session), "PendingScan", false, "Active", now, now, null, null, session.UploaderUserNId, session.Purpose);
        var completed = session with { Status = "Completed", CompletedOn = now, FileNId = file.FileNId, LastUpdatedOn = now };
        FileObjectRecord? stored = null;
        await _transaction.ExecuteAsync(async () =>
        {
            stored = await _store.CompleteSessionAsync(completed, file, session.CurrentOffset, session.WriterEpoch, cancellationToken);
            if (stored is null) throw Error("FILE_UPLOAD_WRITER_REPLACED", "上传写入者已被替换。", 409);
            await _audit.RecordAsync(Audit(tenantNId, userNId, "file.upload.complete", "File", file.FileNId, $"offset={session.CurrentOffset}", $"scan={file.ScanStatus}", null), cancellationToken);
        }, cancellationToken);
        return ToContract(stored!);
    }

    public async Task<FileObjectV1?> GetFileAsync(string tenantNId, string fileNId, CancellationToken cancellationToken)
    {
        var file = await _store.GetFileAsync(tenantNId, fileNId, cancellationToken);
        return file is null ? null : ToContract(await RestorePurposeAsync(file, cancellationToken));
    }

    public async Task<FileObjectV1?> GetFileForReconciliationAsync(string tenantNId, string fileNId, CancellationToken cancellationToken)
    {
        var file = await _store.GetFileForReconciliationAsync(tenantNId, fileNId, cancellationToken);
        return file is null ? null : ToContract(await RestorePurposeAsync(file, cancellationToken));
    }

    public async Task<Stream> OpenFileContentAsync(string tenantNId, string userNId, string fileNId, string? referenceNId, CancellationToken cancellationToken)
    {
        var file = await _store.GetFileAsync(tenantNId, fileNId, cancellationToken) ?? throw Error("FILE_NOT_FOUND", "文件不存在。", 404);
        if (file.ScanStatus != "Clean")
        {
            if (file.ScanStatus == "Malicious") throw Error("FILE_MALICIOUS", "文件未通过安全扫描。", 422);
            throw Error("FILE_SCAN_PENDING", "文件尚未通过安全扫描。", 409);
        }
        if (file.Restricted || file.DeletionStatus != "Active") throw Error("FILE_ACCESS_RESTRICTED", "文件当前不可下载。", 403);
        if (!string.Equals(file.OwnerUserNId, userNId, StringComparison.Ordinal)
            && (string.IsNullOrWhiteSpace(referenceNId)
                || !await HasDownloadGrantAsync(tenantNId, userNId, fileNId, referenceNId, cancellationToken)))
            throw Error("FILE_ACCESS_DENIED", "当前用户没有该文件的用途授权。", 403);
        return await _contentStore.OpenReadAsync(file.StorageKey, cancellationToken);
    }

    public Task<FilePageV1> ListFilesAsync(string tenantNId, string? search, int page, int pageSize, CancellationToken cancellationToken) =>
        _store.ListFilesAsync(tenantNId, search, Math.Max(page, 1), Math.Clamp(pageSize, 1, 200), cancellationToken);

    public Task<FilePageV1> ListFilesPageAsync(string tenantNId, string? search, string? purpose, string? ownerUserNId, string? scanStatus, bool? restricted, int page, int pageSize, CancellationToken cancellationToken) =>
        _store.ListFilesPageAsync(tenantNId, search, purpose, ownerUserNId, scanStatus, restricted, Math.Max(page, 1), Math.Clamp(pageSize, 1, 200), cancellationToken);

    public async Task<FileReferenceRecord> AddReferenceAsync(string tenantNId, string userNId, string fileNId, FileReferenceRequest request, CancellationToken cancellationToken)
    {
        _ = await _store.GetFileAsync(tenantNId, fileNId, cancellationToken) ?? throw Error("FILE_NOT_FOUND", "文件不存在。", 404);
        if (string.IsNullOrWhiteSpace(request.Purpose)) throw Error("FILE_REFERENCE_PURPOSE_REQUIRED", "文件引用必须声明用途。", 422);
        var reference = new FileReferenceRecord(tenantNId, NormalizeNId(request.ReferenceNId, "ref"), fileNId, userNId, NormalizeText(request.Purpose, "default"), _clock.GetUtcNow(), null);
        await _transaction.ExecuteAsync(async () =>
        {
            try
            {
                await _store.InsertReferenceAsync(reference, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                throw Error("FILE_REFERENCE_EXISTS", "文件引用已存在。", 409);
            }
            await _audit.RecordAsync(Audit(tenantNId, userNId, "file.reference.create", "FileReference", reference.ReferenceNId, null, $"file={fileNId};purpose={reference.Purpose}", null), cancellationToken);
        }, cancellationToken);
        return reference;
    }

    public async Task<FileBindingV1> BindReferenceAsync(string tenantNId, string userNId, string referenceNId, FileBindingRequest request, CancellationToken cancellationToken)
    {
        var normalizedReferenceNId = RequireNId(referenceNId, "FILE_BUSINESS_REFERENCE_INVALID", "业务文件引用标识不能为空。");
        var fileNId = RequireNId(request.FileNId, "FILE_BUSINESS_REFERENCE_INVALID", "业务文件标识不能为空。");
        var conversationNId = RequireNId(request.ConversationNId, "FILE_BUSINESS_REFERENCE_INVALID", "会话标识不能为空。");
        var messageNId = RequireNId(request.MessageNId, "FILE_BUSINESS_REFERENCE_INVALID", "消息标识不能为空。");
        var attachmentNId = RequireNId(request.AttachmentNId, "FILE_BUSINESS_REFERENCE_INVALID", "附件标识不能为空。");
        var uploaderUserNId = RequireNId(request.UploaderUserNId, "FILE_BUSINESS_REFERENCE_INVALID", "上传者标识不能为空。");
        var purpose = NormalizeBusinessPurpose(request.Purpose);
        var file = await RequireFileAsync(tenantNId, fileNId, cancellationToken);
        // Older file rows do not persist purpose on the object; RestorePurposeAsync
        // recovers it from the upload session whenever that source still exists.
        if (file.Purpose is not null && !string.Equals(file.Purpose, purpose, StringComparison.Ordinal))
            throw Error("FILE_BUSINESS_REFERENCE_INVALID", "业务引用用途与文件上传用途不一致。", 422);
        if (!string.Equals(file.DeletionStatus, "Active", StringComparison.Ordinal))
            throw Error("FILE_DELETION_IN_PROGRESS", "文件正在删除或已删除，不能建立业务引用。", 409);

        var existingByReference = await _store.GetBusinessReferenceAsync(tenantNId, normalizedReferenceNId, cancellationToken);
        var existingByAttachment = await _store.GetBusinessReferenceForAttachmentAsync(tenantNId, attachmentNId, purpose, cancellationToken);
        var existing = existingByReference ?? existingByAttachment;
        if (existing is not null)
        {
            if (existing.Status != "Active" || !BusinessReferenceMatches(existing, tenantNId, normalizedReferenceNId, fileNId, conversationNId, messageNId, attachmentNId, uploaderUserNId, purpose))
                throw Error("FILE_BUSINESS_REFERENCE_CONFLICT", "业务文件引用已存在但绑定内容不一致。", 409);
            return ToBinding(existing);
        }

        var record = new FileBusinessReferenceRecord(
            tenantNId, normalizedReferenceNId, fileNId, conversationNId, messageNId, attachmentNId,
            uploaderUserNId, purpose, "collaboration", "Active", 0, _clock.GetUtcNow(), null);
        await _transaction.ExecuteAsync(async () =>
        {
            try
            {
                await _store.InsertBusinessReferenceAsync(record, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                var raced = await _store.GetBusinessReferenceAsync(tenantNId, normalizedReferenceNId, cancellationToken)
                    ?? await _store.GetBusinessReferenceForAttachmentAsync(tenantNId, attachmentNId, purpose, cancellationToken);
                if (raced is not null && raced.Status == "Active" && BusinessReferenceMatches(raced, tenantNId, normalizedReferenceNId, fileNId, conversationNId, messageNId, attachmentNId, uploaderUserNId, purpose))
                    return;
                throw Error("FILE_BUSINESS_REFERENCE_CONFLICT", "业务文件引用已存在但绑定内容不一致。", 409);
            }
            await _audit.RecordAsync(Audit(tenantNId, userNId, "file.reference.bind", "FileBusinessReference", normalizedReferenceNId, null, $"file={fileNId};attachment={attachmentNId};purpose={purpose}", null), cancellationToken);
        }, cancellationToken);
        return ToBinding(await _store.GetBusinessReferenceAsync(tenantNId, normalizedReferenceNId, cancellationToken) ?? record);
    }

    public async Task<FileBindingV1> ReleaseReferenceAsync(string tenantNId, string userNId, string fileNId, string referenceNId, FileBindingReleaseRequest request, CancellationToken cancellationToken)
    {
        var normalizedReferenceNId = RequireNId(referenceNId, "FILE_BUSINESS_REFERENCE_INVALID", "业务文件引用标识不能为空。");
        var requestNId = RequireNId(request.RequestNId, "FILE_BUSINESS_REFERENCE_INVALID", "请求标识不能为空。");
        _ = requestNId;
        var expectedVersion = request.ExpectedVersion ?? -1;
        if (expectedVersion < 0) throw Error("FILE_BUSINESS_REFERENCE_INVALID", "必须提供非负 expectedVersion。");
        var existing = await _store.GetBusinessReferenceAsync(tenantNId, normalizedReferenceNId, cancellationToken)
            ?? throw Error("FILE_BUSINESS_REFERENCE_NOT_FOUND", "业务文件引用不存在。", 404);
        if (!string.IsNullOrWhiteSpace(fileNId) && !string.Equals(existing.FileNId, fileNId, StringComparison.Ordinal))
            throw Error("FILE_BUSINESS_REFERENCE_NOT_FOUND", "业务文件引用不存在。", 404);
        if (existing.Status == "Released") return ToBinding(existing);
        if (existing.Version != expectedVersion)
            throw Error("FILE_BUSINESS_REFERENCE_CONFLICT", "业务文件引用版本已变化。", 409);
        var releasedOn = _clock.GetUtcNow();
        await _transaction.ExecuteAsync(async () =>
        {
            if (!await _store.ReleaseBusinessReferenceAsync(tenantNId, existing.FileNId, normalizedReferenceNId, expectedVersion, releasedOn, cancellationToken))
            {
                var current = await _store.GetBusinessReferenceAsync(tenantNId, normalizedReferenceNId, cancellationToken);
                if (current?.Status != "Released") throw Error("FILE_BUSINESS_REFERENCE_CONFLICT", "业务文件引用版本已变化。", 409);
            }
            await _audit.RecordAsync(Audit(tenantNId, userNId, "file.reference.release", "FileBusinessReference", normalizedReferenceNId, "Active", "Released", null), cancellationToken);
        }, cancellationToken);
        return ToBinding(await _store.GetBusinessReferenceAsync(tenantNId, normalizedReferenceNId, cancellationToken) ?? existing with { Status = "Released", Version = expectedVersion + 1, ReleasedOn = releasedOn });
    }

    public async Task<FileHoldV1> PutLegalHoldAsync(string tenantNId, string userNId, string caseNId, string fileNId, FileHoldRequest request, CancellationToken cancellationToken)
    {
        var normalizedCaseNId = RequireNId(caseNId, "FILE_LEGAL_HOLD_INVALID", "案件标识不能为空。");
        var normalizedFileNId = RequireNId(fileNId, "FILE_LEGAL_HOLD_INVALID", "文件标识不能为空。");
        var requestNId = RequireNId(request.RequestNId, "FILE_LEGAL_HOLD_INVALID", "请求标识不能为空.");
        _ = requestNId;
        var checksum = NormalizeChecksum(request.ScopeChecksum);
        var caseRevision = request.CaseRevision ?? 0;
        if (caseRevision < 1) throw Error("FILE_LEGAL_HOLD_INVALID", "案件版本必须为正数。");
        var file = await RequireFileAsync(tenantNId, normalizedFileNId, cancellationToken);
        if (!string.Equals(file.DeletionStatus, "Active", StringComparison.Ordinal))
            throw Error("FILE_DELETION_IN_PROGRESS", "文件正在删除或已删除，不能建立 Legal Hold。", 409);
        var existing = await _store.GetLegalHoldAsync(tenantNId, normalizedCaseNId, normalizedFileNId, cancellationToken);
        if (existing is not null)
        {
            if (!string.Equals(existing.ScopeChecksum, checksum, StringComparison.OrdinalIgnoreCase) || existing.CaseRevision != caseRevision)
                throw Error("FILE_LEGAL_HOLD_CONFLICT", "文件 Legal Hold 的案件版本或范围校验不一致。", 409);
            return ToHold(existing);
        }
        var record = new FileHoldRecord(tenantNId, normalizedCaseNId, normalizedFileNId, checksum, caseRevision, "collaboration", "Active", _clock.GetUtcNow(), _clock.GetUtcNow(), null);
        await _transaction.ExecuteAsync(async () =>
        {
            try
            {
                await _store.InsertLegalHoldAsync(record, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                var raced = await _store.GetLegalHoldAsync(tenantNId, normalizedCaseNId, normalizedFileNId, cancellationToken);
                if (raced is not null && string.Equals(raced.ScopeChecksum, checksum, StringComparison.OrdinalIgnoreCase) && raced.CaseRevision == caseRevision)
                    return;
                throw Error("FILE_LEGAL_HOLD_CONFLICT", "文件 Legal Hold 已存在但案件范围不一致。", 409);
            }
            await _audit.RecordAsync(Audit(tenantNId, userNId, "file.hold.put", "FileLegalHold", $"{normalizedCaseNId}:{normalizedFileNId}", null, checksum, null), cancellationToken);
        }, cancellationToken);
        return ToHold(await _store.GetLegalHoldAsync(tenantNId, normalizedCaseNId, normalizedFileNId, cancellationToken) ?? record);
    }

    public async Task<FileHoldV1> ReleaseLegalHoldAsync(string tenantNId, string userNId, string caseNId, string fileNId, FileHoldRequest request, CancellationToken cancellationToken)
    {
        var normalizedCaseNId = RequireNId(caseNId, "FILE_LEGAL_HOLD_INVALID", "案件标识不能为空。");
        var normalizedFileNId = RequireNId(fileNId, "FILE_LEGAL_HOLD_INVALID", "文件标识不能为空。");
        var requestNId = RequireNId(request.RequestNId, "FILE_LEGAL_HOLD_INVALID", "请求标识不能为空.");
        _ = requestNId;
        var checksum = NormalizeChecksum(request.ScopeChecksum);
        var caseRevision = request.CaseRevision ?? 0;
        if (caseRevision < 1) throw Error("FILE_LEGAL_HOLD_INVALID", "案件版本必须为正数。");
        var existing = await _store.GetLegalHoldAsync(tenantNId, normalizedCaseNId, normalizedFileNId, cancellationToken)
            ?? throw Error("FILE_LEGAL_HOLD_NOT_FOUND", "文件 Legal Hold 不存在。", 404);
        if (!string.Equals(existing.ScopeChecksum, checksum, StringComparison.OrdinalIgnoreCase) || existing.CaseRevision != caseRevision)
            throw Error("FILE_LEGAL_HOLD_CONFLICT", "文件 Legal Hold 的案件版本或范围校验不一致。", 409);
        if (existing.Status == "Released") return ToHold(existing);
        var releasedOn = _clock.GetUtcNow();
        await _transaction.ExecuteAsync(async () =>
        {
            if (!await _store.ReleaseLegalHoldAsync(tenantNId, normalizedCaseNId, normalizedFileNId, checksum, caseRevision, releasedOn, cancellationToken))
            {
                var current = await _store.GetLegalHoldAsync(tenantNId, normalizedCaseNId, normalizedFileNId, cancellationToken);
                if (current?.Status != "Released") throw Error("FILE_LEGAL_HOLD_CONFLICT", "文件 Legal Hold 已被其他请求改变。", 409);
            }
            await _audit.RecordAsync(Audit(tenantNId, userNId, "file.hold.release", "FileLegalHold", $"{normalizedCaseNId}:{normalizedFileNId}", "Active", "Released", null), cancellationToken);
        }, cancellationToken);
        return ToHold(await _store.GetLegalHoldAsync(tenantNId, normalizedCaseNId, normalizedFileNId, cancellationToken) ?? existing with { Status = "Released", UpdatedOn = releasedOn, ReleasedOn = releasedOn });
    }

    public async Task<FileHoldV1?> GetLegalHoldAsync(string tenantNId, string userNId, string caseNId, string fileNId, CancellationToken cancellationToken) =>
        (await _store.GetLegalHoldAsync(tenantNId, caseNId, fileNId, cancellationToken)) is { } hold ? ToHold(hold) : null;

    public async Task DeleteReferenceAsync(string tenantNId, string userNId, string referenceNId, CancellationToken cancellationToken)
    {
        var reference = await _store.GetReferenceAsync(tenantNId, referenceNId, cancellationToken) ?? throw Error("FILE_REFERENCE_NOT_FOUND", "文件引用不存在。", 404);
        if (!string.Equals(reference.OwnerUserNId, userNId, StringComparison.Ordinal)) throw Error("FILE_REFERENCE_NOT_FOUND", "文件引用不存在。", 404);
        await _transaction.ExecuteAsync(async () =>
        {
            await _store.DeleteReferenceAsync(tenantNId, referenceNId, _clock.GetUtcNow(), cancellationToken);
            await _audit.RecordAsync(Audit(tenantNId, userNId, "file.reference.delete", "FileReference", referenceNId, $"file={reference.FileNId}", "deleted", null), cancellationToken);
        }, cancellationToken);
    }

    public async Task DeleteReferenceAsync(string tenantNId, string userNId, string fileNId, string referenceNId, CancellationToken cancellationToken)
    {
        var reference = await _store.GetReferenceAsync(tenantNId, referenceNId, cancellationToken) ?? throw Error("FILE_REFERENCE_NOT_FOUND", "文件引用不存在。", 404);
        if (!string.Equals(reference.FileNId, fileNId, StringComparison.Ordinal)
            || !string.Equals(reference.OwnerUserNId, userNId, StringComparison.Ordinal))
            throw Error("FILE_REFERENCE_NOT_FOUND", "文件引用不存在。", 404);
        await _transaction.ExecuteAsync(async () =>
        {
            await _store.DeleteReferenceAsync(tenantNId, referenceNId, _clock.GetUtcNow(), cancellationToken);
            await _audit.RecordAsync(Audit(tenantNId, userNId, "file.reference.delete", "FileReference", referenceNId, $"file={reference.FileNId}", "deleted", null), cancellationToken);
        }, cancellationToken);
    }

    public async Task<FileObjectV1> RequestDeletionAsync(string tenantNId, string userNId, string fileNId, CancellationToken cancellationToken)
    {
        var file = await RequireFileAsync(tenantNId, fileNId, cancellationToken);
        if (file.RetentionUntil is { } retention && retention > _clock.GetUtcNow()) throw Error("FILE_RETENTION_ACTIVE", "文件仍处于保留期。", 409);
        var updated = file with { DeletionStatus = "DeletionRequested", LastUpdatedOn = _clock.GetUtcNow() };
        await _transaction.ExecuteAsync(async () =>
        {
            if (!await _store.TryRequestDeletionAsync(updated, cancellationToken))
            {
                if (await _store.HasActiveLegalHoldsAsync(tenantNId, fileNId, cancellationToken)) throw Error("FILE_LEGAL_HOLD_ACTIVE", "文件存在活动 Legal Hold。", 409);
                throw Error("FILE_REFERENCED", "文件仍有活动引用。", 409);
            }
            await _audit.RecordAsync(Audit(tenantNId, userNId, "file.deletion.request", "File", fileNId, file.DeletionStatus, updated.DeletionStatus, null), cancellationToken);
        }, cancellationToken);
        return ToContract(updated);
    }

    public async Task<FileObjectV1> SetRestrictionAsync(string tenantNId, string userNId, string fileNId, FileRestrictionRequest request, CancellationToken cancellationToken)
    {
        var file = await RequireFileAsync(tenantNId, fileNId, cancellationToken);
        var updated = file with { Restricted = request.Restricted ?? true, LastUpdatedOn = _clock.GetUtcNow() };
        await _transaction.ExecuteAsync(async () =>
        {
            await _store.UpdateFileAsync(updated, cancellationToken);
            await _audit.RecordAsync(Audit(tenantNId, userNId, "file.restriction.change", "File", fileNId, file.Restricted.ToString(), updated.Restricted.ToString(), null), cancellationToken);
        }, cancellationToken);
        return ToContract(updated);
    }

    private async Task<UploadSessionV1> SetStateAsync(string tenantNId, string sessionNId, string userNId, string status, string? reason, CancellationToken cancellationToken)
    {
        await using var lease = await _uploadCoordinator.AcquireAsync(tenantNId, sessionNId, cancellationToken);
        var session = await RequireOwnedSessionAsync(tenantNId, sessionNId, userNId, cancellationToken);
        if (session.Status is "Completed" or "Cancelled") throw Error("FILE_UPLOAD_CANCELLED", "上传会话已结束。");
        var updated = session with { Status = status, ErrorCode = reason, LastUpdatedOn = _clock.GetUtcNow() };
        await _transaction.ExecuteAsync(async () =>
        {
            if (!await _store.UpdateSessionAsync(updated, session.CurrentOffset, session.WriterEpoch, cancellationToken)) throw Error("FILE_UPLOAD_WRITER_REPLACED", "上传写入者已被替换。", 409);
            await _audit.RecordAsync(Audit(tenantNId, userNId, status == "Cancelled" ? "file.upload.cancel" : status == "Paused" ? "file.upload.pause" : "file.upload.resume", "FileUploadSession", sessionNId, session.Status, updated.Status, reason), cancellationToken);
        }, cancellationToken);
        return ToContract(updated);
    }

    private async Task<FileUploadSessionRecord> RequireSessionAsync(string tenantNId, string sessionNId, CancellationToken cancellationToken) =>
        await _store.GetSessionAsync(tenantNId, sessionNId, cancellationToken) ?? throw Error("FILE_UPLOAD_NOT_FOUND", "上传会话不存在。", 404);

    private async Task<FileUploadSessionRecord> RequireOwnedSessionAsync(string tenantNId, string sessionNId, string userNId, CancellationToken cancellationToken)
    {
        var session = await RequireSessionAsync(tenantNId, sessionNId, cancellationToken);
        EnsureOwner(session, userNId);
        return session;
    }

    private async Task<FileObjectRecord> RequireFileAsync(string tenantNId, string fileNId, CancellationToken cancellationToken) =>
        await _store.GetFileAsync(tenantNId, fileNId, cancellationToken) is { } file
            ? await RestorePurposeAsync(file, cancellationToken)
            : throw Error("FILE_NOT_FOUND", "文件不存在。", 404);

    private async Task<FileObjectRecord> RestorePurposeAsync(FileObjectRecord file, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(file.Purpose))
            return file;
        var session = await _store.GetSessionAsync(file.TenantNId, file.UploadSessionNId, cancellationToken);
        return session is null ? file : file with { Purpose = session.Purpose };
    }

    private void EnsureWritable(FileUploadSessionRecord session)
    {
        if (session.ExpiresOn <= _clock.GetUtcNow()) throw Error("FILE_UPLOAD_EXPIRED", "上传会话已过期。", 409);
        if (session.Status is "Cancelled") throw Error("FILE_UPLOAD_CANCELLED", "上传会话已取消。", 409);
        if (session.Status is not ("Ready" or "Uploading")) throw Error("FILE_UPLOAD_PROOF_REQUIRED", "上传开始前需要完整性证明或恢复上传。");
    }

    private static void EnsureOwner(FileUploadSessionRecord session, string userNId)
    {
        if (!string.Equals(session.UploaderUserNId, userNId, StringComparison.Ordinal)) throw Error("FILE_UPLOAD_NOT_FOUND", "上传会话不存在。", 404);
    }

    private static void EnsureEpoch(FileUploadSessionRecord session, int? epoch)
    {
        if (epoch is null || epoch != session.WriterEpoch) throw Error("FILE_UPLOAD_WRITER_REPLACED", "写入者 epoch 已变化。", 409);
    }

    private static string TransportStorageKey(FileUploadSessionRecord session) => $"{session.TenantNId}/{session.SessionNId}.bin";

    private static string NormalizeNId(string? value, string prefix) => string.IsNullOrWhiteSpace(value) ? $"{prefix}-{Guid.NewGuid():N}" : value.Trim();

    private static string RequireNId(string? value, string code, string message)
    {
        if (string.IsNullOrWhiteSpace(value)) throw Error(code, message);
        return value.Trim();
    }

    private static string NormalizeBusinessPurpose(string? purpose)
    {
        var normalized = RequireNId(purpose, "FILE_BUSINESS_REFERENCE_INVALID", "业务文件引用用途不能为空。");
        return normalized is "CollaborationMessageAttachment" or "CollaborationComplianceExport"
            ? normalized
            : throw Error("FILE_BUSINESS_REFERENCE_INVALID", "业务文件引用用途不受支持。");
    }

    private static string NormalizeChecksum(string? checksum)
    {
        var normalized = RequireNId(checksum, "FILE_LEGAL_HOLD_INVALID", "scopeChecksum 不能为空。").ToLowerInvariant();
        if (normalized.Length != 64 || normalized.Any(character => !Uri.IsHexDigit(character)))
            throw Error("FILE_LEGAL_HOLD_INVALID", "scopeChecksum 必须是 64 位十六进制值。");
        return normalized;
    }

    private async Task<bool> HasDownloadGrantAsync(string tenantNId, string userNId, string fileNId, string referenceNId, CancellationToken cancellationToken)
    {
        var reference = await _store.GetReferenceAsync(tenantNId, referenceNId, cancellationToken);
        if (reference is not null)
            return string.Equals(reference.FileNId, fileNId, StringComparison.Ordinal)
                && string.Equals(reference.OwnerUserNId, userNId, StringComparison.Ordinal);
        var business = await _store.GetBusinessReferenceAsync(tenantNId, referenceNId, cancellationToken);
        return business is not null
            && business.Status == "Active"
            && string.Equals(business.FileNId, fileNId, StringComparison.Ordinal);
    }

    private static bool BusinessReferenceMatches(FileBusinessReferenceRecord value, string tenantNId, string referenceNId, string fileNId, string conversationNId, string messageNId, string attachmentNId, string uploaderUserNId, string purpose) =>
        value.TenantNId == tenantNId && value.ReferenceNId == referenceNId && value.FileNId == fileNId
        && value.ConversationNId == conversationNId && value.MessageNId == messageNId && value.AttachmentNId == attachmentNId
        && value.UploaderUserNId == uploaderUserNId && value.Purpose == purpose && value.OwnerService == "collaboration";

    private static FileBindingV1 ToBinding(FileBusinessReferenceRecord value) => new() { TenantNId = value.TenantNId, ReferenceNId = value.ReferenceNId, FileNId = value.FileNId, Status = value.Status, Version = value.Version };

    private static FileHoldV1 ToHold(FileHoldRecord value) => new() { TenantNId = value.TenantNId, CaseNId = value.CaseNId, FileNId = value.FileNId, ScopeChecksum = value.ScopeChecksum, CaseRevision = value.CaseRevision, OwnerService = value.OwnerService, Status = value.Status, CreatedOn = value.CreatedOn, UpdatedOn = value.UpdatedOn, ReleasedOn = value.ReleasedOn };

    private static string NormalizeText(string? value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private UploadSessionV1 ToContract(FileUploadSessionRecord session, string? userNId = null)
    {
        string? resumeTicket = null;
        DateTimeOffset? resumeTicketExpiresOn = null;
        if (userNId is not null)
        {
            resumeTicket = IssueTicket(session, userNId, out var expiresOn);
            resumeTicketExpiresOn = expiresOn;
        }
        return new UploadSessionV1
        {
            TenantNId = session.TenantNId,
            SessionNId = session.SessionNId,
            TransportId = session.TransportId,
            FileName = session.FileName,
            ContentType = session.ContentType,
            Length = session.ExpectedLength,
            Offset = session.CurrentOffset,
            Sha256 = session.ExpectedSha256 ?? string.Empty,
            Purpose = session.Purpose,
            WriterEpoch = session.WriterEpoch,
            Status = session.Status,
            ExpiresOn = session.ExpiresOn,
            FileNId = session.FileNId,
            ErrorCode = session.ErrorCode,
            ResumeTicket = resumeTicket,
            ResumeTicketExpiresOn = resumeTicketExpiresOn
        };
    }

    private string IssueTicket(FileUploadSessionRecord session, string userNId, out DateTimeOffset expiresOn)
    {
        expiresOn = _clock.GetUtcNow().AddMinutes(10);
        return _tickets.Issue(session.TenantNId, session.SessionNId, userNId, session.WriterEpoch, session.CurrentOffset, expiresOn);
    }

    private static FileObjectV1? ToContractOrNull(FileObjectRecord? file) => file is null ? null : ToContract(file);

    private static FileObjectV1 ToContract(FileObjectRecord file) => new()
    {
        TenantNId = file.TenantNId,
        FileNId = file.FileNId,
        FileName = file.FileName,
        ContentType = file.ContentType,
        Length = file.ContentLength,
        Sha256 = file.Sha256,
        ScanStatus = file.ScanStatus,
        Restricted = file.Restricted,
        Purpose = file.Purpose,
        OwnerUserNId = file.OwnerUserNId,
        ReferenceCount = file.ReferenceSummary?.Count ?? 0,
        ReferenceSummary = file.ReferenceSummary?.Select(reference => new FileReferenceSummaryV1
        {
            ReferenceNId = reference.ReferenceNId,
            OwnerUserNId = reference.OwnerUserNId,
            Purpose = reference.Purpose,
            CreatedOn = reference.CreatedOn
        }).ToArray() ?? [],
        DeletionStatus = file.DeletionStatus,
        CreatedOn = file.CreatedOn,
        RetentionUntil = file.RetentionUntil
    };

    private static Pf04ServiceException Error(string code, string message, int status = 400) => new(code, message, status);

    private static LocalAuditEntry Audit(string tenantNId, string actorUserNId, string action, string objectType, string objectNId, string? before, string? after, string? reason) =>
        new(tenantNId, actorUserNId, action, objectType, objectNId, reason, before, after, Guid.NewGuid().ToString("N"));
}
