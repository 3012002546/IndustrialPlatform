using IndustrialPlatform.Collaboration.Application;
using IndustrialPlatform.Collaboration.Contracts;
using IndustrialPlatform.SystemData.Application.Pf04;
using IndustrialPlatform.SystemData.Application.Files;
using IndustrialPlatform.SystemData.Contracts.Files;
using System.Security.Cryptography;

namespace IndustrialPlatform.Collaboration.Infrastructure;

public sealed class SystemDataFilePort : ICollaborationFilePort
{
    private readonly IFileService _files;

    public SystemDataFilePort(IFileService files) => _files = files;

    public async Task<AttachmentUploadResult> CreateUploadAsync(string tenantNId, string userNId, AttachmentRecord attachment, CancellationToken cancellationToken)
    {
        var session = await _files.CreateSessionAsync(tenantNId, userNId, new CreateUploadSessionRequest
        {
            SessionNId = attachment.AttachmentNId,
            TransportId = "collab-" + attachment.AttachmentNId,
            FileName = attachment.FileNameSnapshot,
            ContentType = attachment.ContentTypeSnapshot,
            Length = attachment.SizeSnapshot,
            Purpose = CollaborationServiceConstants.AttachmentPurpose,
        }, cancellationToken);
        return new AttachmentUploadResult(session.SessionNId, session.TransportId, session.ExpiresOn, session.FileName, session.ContentType, session.Length, session.Offset, session.WriterEpoch, session.Status, session.ResumeTicket);
    }

    public async Task<AttachmentUploadResult> GetUploadAsync(string tenantNId, string userNId, string sessionNId, CancellationToken cancellationToken)
    {
        var session = await _files.GetSessionAsync(tenantNId, userNId, sessionNId, cancellationToken);
        return ToUploadResult(session);
    }

    public async Task<AttachmentUploadResult> SetContentHashAsync(string tenantNId, string userNId, string sessionNId, string sha256, CancellationToken cancellationToken) =>
        ToUploadResult(await _files.SetContentHashAsync(tenantNId, sessionNId, userNId, new ContentHashRequest { Sha256 = sha256 }, cancellationToken));

    public async Task<AttachmentUploadResult> ResumeProofAsync(string tenantNId, string userNId, string sessionNId, int writerEpoch, string proof, CancellationToken cancellationToken) =>
        ToUploadResult(await _files.ResumeProofAsync(tenantNId, sessionNId, userNId, new ResumeProofRequest { WriterEpoch = writerEpoch, Proof = proof }, cancellationToken));

    public async Task<AttachmentUploadResult> TakeoverAsync(string tenantNId, string userNId, string sessionNId, int expectedWriterEpoch, string? idempotencyKey, string? proof, CancellationToken cancellationToken) =>
        ToUploadResult(await _files.TakeoverAsync(tenantNId, sessionNId, userNId, new TakeoverUploadRequest { ExpectedWriterEpoch = expectedWriterEpoch, IdempotencyKey = idempotencyKey, Proof = proof }, cancellationToken));

    public async Task<AttachmentUploadResult> PauseAsync(string tenantNId, string userNId, string sessionNId, CancellationToken cancellationToken) =>
        ToUploadResult(await _files.PauseAsync(tenantNId, sessionNId, userNId, cancellationToken));

    public async Task<AttachmentUploadResult> ResumeAsync(string tenantNId, string userNId, string sessionNId, int writerEpoch, string proof, CancellationToken cancellationToken) =>
        ToUploadResult(await _files.ResumeAsync(tenantNId, sessionNId, userNId, new ResumeProofRequest { WriterEpoch = writerEpoch, Proof = proof }, cancellationToken));

    public async Task<AttachmentUploadResult> CancelAsync(string tenantNId, string userNId, string sessionNId, string? reason, CancellationToken cancellationToken) =>
        ToUploadResult(await _files.CancelAsync(tenantNId, sessionNId, userNId, new SetUploadStateRequest { Reason = reason }, cancellationToken));

    public async Task<AttachmentUploadResult> AppendAsync(string tenantNId, string userNId, string transportId, long expectedOffset, int writerEpoch, Stream content, string? resumeTicket, CancellationToken cancellationToken) =>
        ToUploadResult(await _files.AppendAsync(tenantNId, transportId, userNId, expectedOffset, writerEpoch, content, resumeTicket, cancellationToken));

    public async Task<AttachmentUploadResult> CompleteUploadAsync(string tenantNId, string userNId, string sessionNId, CancellationToken cancellationToken) =>
        ToUploadResult(sessionNId, await _files.CompleteAsync(tenantNId, sessionNId, userNId, cancellationToken));

    public async Task<string?> AddReferenceAsync(string tenantNId, string userNId, string fileNId, string referenceNId, CancellationToken cancellationToken, string? purpose = null)
    {
        var file = await _files.GetFileAsync(tenantNId, fileNId, cancellationToken);
        if (file?.ReferenceSummary.Any(item => string.Equals(item.ReferenceNId, referenceNId, StringComparison.Ordinal)) == true)
            return referenceNId;
        await _files.AddReferenceAsync(tenantNId, userNId, fileNId, new FileReferenceRequest { ReferenceNId = referenceNId, Purpose = purpose ?? CollaborationServiceConstants.AttachmentPurpose }, cancellationToken);
        return referenceNId;
    }

    public async Task<string?> BindReferenceAsync(string tenantNId, string userNId, string fileNId, string referenceNId, string conversationNId, string messageNId, string attachmentNId, string uploaderUserNId, string purpose, string requestNId, CancellationToken cancellationToken)
    {
        _ = await _files.BindReferenceAsync(tenantNId, userNId, referenceNId, new FileBindingRequest
        {
            RequestNId = requestNId,
            FileNId = fileNId,
            ConversationNId = conversationNId,
            MessageNId = messageNId,
            AttachmentNId = attachmentNId,
            UploaderUserNId = uploaderUserNId,
            Purpose = purpose,
        }, cancellationToken);
        return referenceNId;
    }

    public async Task ReleaseReferenceAsync(string tenantNId, string userNId, string fileNId, string referenceNId, long expectedVersion, string requestNId, CancellationToken cancellationToken)
    {
        await _files.ReleaseReferenceAsync(tenantNId, userNId, fileNId, referenceNId, new FileBindingReleaseRequest { RequestNId = requestNId, ExpectedVersion = expectedVersion }, cancellationToken);
    }

    public async Task<ExportArtifactResult> CreateExportArtifactAsync(string tenantNId, string userNId, string exportNId, string fileName, string contentType, Stream content, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        await using var buffered = new MemoryStream();
        await content.CopyToAsync(buffered, cancellationToken);
        var bytes = buffered.ToArray();
        var sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var sessionNId = $"{exportNId}-file";
        UploadSessionV1 session;
        try
        {
            session = await _files.CreateSessionAsync(tenantNId, userNId, new CreateUploadSessionRequest
            {
                SessionNId = sessionNId,
                TransportId = $"collab-export-{exportNId}",
                FileName = fileName,
                ContentType = contentType,
                Length = bytes.LongLength,
                Sha256 = sha256,
                Purpose = CollaborationServiceConstants.ComplianceExportPurpose,
            }, cancellationToken);
        }
        catch
        {
            session = await _files.GetSessionAsync(tenantNId, userNId, sessionNId, cancellationToken);
        }

        if (session.Status == "Completed" && session.FileNId is { Length: > 0 })
        {
            var completed = await _files.GetFileAsync(tenantNId, session.FileNId, cancellationToken)
                ?? throw new InvalidOperationException("导出上传会话已完成但文件对象不存在。");
            return new ExportArtifactResult(completed.FileNId, completed.FileName, completed.ContentType, completed.Length, completed.ScanStatus);
        }

        var ready = session.Status == "Ready" || session.Status == "Uploading"
            ? session
            : await _files.SetContentHashAsync(tenantNId, session.SessionNId, userNId, new ContentHashRequest { Sha256 = sha256 }, cancellationToken);
        await using var payload = new MemoryStream(bytes, writable: false);
        await _files.AppendAsync(tenantNId, ready.TransportId, userNId, ready.Offset, ready.WriterEpoch, payload, ready.ResumeTicket, cancellationToken);
        var file = await _files.CompleteAsync(tenantNId, ready.SessionNId, userNId, cancellationToken);
        return new ExportArtifactResult(file.FileNId, file.FileName, file.ContentType, file.Length, file.ScanStatus);
    }

    public async Task<string?> AddExportReferenceAsync(string tenantNId, string userNId, string fileNId, string referenceNId, CancellationToken cancellationToken)
    {
        return await BindExportReferenceAsync(tenantNId, userNId, fileNId, referenceNId, referenceNId, referenceNId, cancellationToken);
    }

    public async Task<string?> BindExportReferenceAsync(string tenantNId, string userNId, string fileNId, string referenceNId, string conversationNId, string exportNId, CancellationToken cancellationToken)
    {
        await _files.BindReferenceAsync(tenantNId, userNId, referenceNId, new FileBindingRequest
        {
            RequestNId = $"export-reference-{exportNId}",
            FileNId = fileNId,
            ConversationNId = conversationNId,
            MessageNId = exportNId,
            AttachmentNId = exportNId,
            UploaderUserNId = userNId,
            Purpose = CollaborationServiceConstants.ComplianceExportPurpose,
        }, cancellationToken);
        return referenceNId;
    }

    public async Task ReleaseExportReferenceAsync(string tenantNId, string userNId, string fileNId, string referenceNId, CancellationToken cancellationToken)
    {
        try
        {
            await _files.ReleaseReferenceAsync(tenantNId, userNId, fileNId, referenceNId, new FileBindingReleaseRequest
            {
                RequestNId = $"export-release-{referenceNId}",
                ExpectedVersion = 0,
            }, cancellationToken);
        }
        catch (Pf04ServiceException exception) when (exception.Code == "FILE_BUSINESS_REFERENCE_NOT_FOUND")
        {
            await _files.DeleteReferenceAsync(tenantNId, userNId, fileNId, referenceNId, cancellationToken);
        }
    }

    public async Task AddLegalHoldReferenceAsync(string tenantNId, string userNId, string fileNId, string holdCaseNId, CancellationToken cancellationToken)
    {
        var referenceNId = $"HOLD-{holdCaseNId}-{fileNId}";
        var file = await _files.GetFileAsync(tenantNId, fileNId, cancellationToken);
        if (file?.ReferenceSummary.Any(item => string.Equals(item.ReferenceNId, referenceNId, StringComparison.Ordinal)) == true)
            return;
        await _files.AddReferenceAsync(tenantNId, userNId, fileNId, new FileReferenceRequest { ReferenceNId = referenceNId, Purpose = CollaborationServiceConstants.ComplianceLegalHoldPurpose }, cancellationToken);
    }

    public async Task AddLegalHoldReferenceAsync(string tenantNId, string userNId, string fileNId, string holdCaseNId, string requestNId, string scopeChecksum, long caseRevision, CancellationToken cancellationToken)
    {
        await _files.PutLegalHoldAsync(tenantNId, userNId, holdCaseNId, fileNId, new FileHoldRequest { RequestNId = requestNId, ScopeChecksum = scopeChecksum, CaseRevision = caseRevision }, cancellationToken);
    }

    public async Task ReleaseLegalHoldReferenceAsync(string tenantNId, string userNId, string fileNId, string holdCaseNId, string requestNId, string scopeChecksum, long caseRevision, CancellationToken cancellationToken)
    {
        await _files.ReleaseLegalHoldAsync(tenantNId, userNId, holdCaseNId, fileNId, new FileHoldRequest { RequestNId = requestNId, ScopeChecksum = scopeChecksum, CaseRevision = caseRevision }, cancellationToken);
    }

    public async Task ReleaseLegalHoldReferenceAsync(string tenantNId, string userNId, string fileNId, string holdCaseNId, CancellationToken cancellationToken)
    {
        var referenceNId = $"HOLD-{holdCaseNId}-{fileNId}";
        var file = await _files.GetFileAsync(tenantNId, fileNId, cancellationToken);
        if (file?.ReferenceSummary.Any(item => string.Equals(item.ReferenceNId, referenceNId, StringComparison.Ordinal)) != true)
            return;
        await _files.DeleteReferenceAsync(tenantNId, userNId, fileNId, referenceNId, cancellationToken);
    }

    public async Task<CollaborationFileState?> GetAsync(string tenantNId, string userNId, string fileNId, CancellationToken cancellationToken)
    {
        var file = await _files.GetFileAsync(tenantNId, fileNId, cancellationToken);
        return ToFileState(file);
    }

    public async Task<CollaborationFileState?> GetForReconciliationAsync(string tenantNId, string fileNId, CancellationToken cancellationToken) =>
        ToFileState(await _files.GetFileForReconciliationAsync(tenantNId, fileNId, cancellationToken));

    public Task<Stream> OpenContentAsync(string tenantNId, string userNId, string fileNId, string referenceNId, CancellationToken cancellationToken) =>
        _files.OpenFileContentAsync(tenantNId, userNId, fileNId, referenceNId, cancellationToken);

    private static CollaborationFileState? ToFileState(FileObjectV1? file) => file is null
        ? null
        : new CollaborationFileState(file.FileNId, file.FileName, file.ContentType, file.Length, file.ScanStatus, file.Restricted, file.DeletionStatus, file.LastUpdatedOn);

    private static AttachmentUploadResult ToUploadResult(UploadSessionV1 session) => new(session.SessionNId, session.TransportId, session.ExpiresOn, session.FileName, session.ContentType, session.Length, session.Offset, session.WriterEpoch, session.Status, session.ResumeTicket, session.FileNId, session.Sha256);

    private static AttachmentUploadResult ToUploadResult(string sessionNId, FileObjectV1 file) => new(
        sessionNId,
        null,
        file.RetentionUntil ?? DateTimeOffset.UtcNow,
        file.FileName,
        file.ContentType,
        file.Length,
        file.Length,
        null,
        file.ScanStatus,
        null,
        file.FileNId,
        file.Sha256);
}
