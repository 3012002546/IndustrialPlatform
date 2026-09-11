using IndustrialPlatform.SystemData.Contracts.Files;

namespace IndustrialPlatform.SystemData.Application.Files;

public sealed record FileUploadSessionRecord(
    string TenantNId,
    string SessionNId,
    string TransportId,
    string UploaderUserNId,
    string Purpose,
    string FileName,
    string ContentType,
    long ExpectedLength,
    string? ExpectedSha256,
    string? SampleFingerprint,
    long CurrentOffset,
    int WriterEpoch,
    string Status,
    DateTimeOffset ExpiresOn,
    DateTimeOffset? CompletedOn,
    string? FileNId,
    string? ErrorCode,
    DateTimeOffset CreatedOn,
    DateTimeOffset LastUpdatedOn);

public sealed record FileObjectRecord(
    string TenantNId,
    string FileNId,
    string UploadSessionNId,
    string FileName,
    string ContentType,
    long ContentLength,
    string Sha256,
    string StorageKey,
    string ScanStatus,
    bool Restricted,
    string DeletionStatus,
    DateTimeOffset CreatedOn,
    DateTimeOffset LastUpdatedOn,
    DateTimeOffset? DeletedOn,
    DateTimeOffset? RetentionUntil,
    string? OwnerUserNId = null,
    string? Purpose = null,
    IReadOnlyList<FileReferenceSummaryRecord>? ReferenceSummary = null);

public sealed record FileReferenceSummaryRecord(
    string ReferenceNId,
    string OwnerUserNId,
    string Purpose,
    DateTimeOffset CreatedOn);

public sealed record FileReferenceRecord(
    string TenantNId,
    string ReferenceNId,
    string FileNId,
    string OwnerUserNId,
    string Purpose,
    DateTimeOffset CreatedOn,
    DateTimeOffset? DeletedOn);

public sealed record FileBusinessReferenceRecord(
    string TenantNId,
    string ReferenceNId,
    string FileNId,
    string ConversationNId,
    string MessageNId,
    string AttachmentNId,
    string UploaderUserNId,
    string Purpose,
    string OwnerService,
    string Status,
    long Version,
    DateTimeOffset CreatedOn,
    DateTimeOffset? ReleasedOn);

public sealed record FileHoldRecord(
    string TenantNId,
    string CaseNId,
    string FileNId,
    string ScopeChecksum,
    long CaseRevision,
    string OwnerService,
    string Status,
    DateTimeOffset CreatedOn,
    DateTimeOffset UpdatedOn,
    DateTimeOffset? ReleasedOn);

public sealed record FileScanResult(string Status, string Detail);

public sealed record FileStatusChangeRecord(
    Guid EventId,
    string TenantNId,
    string FileNId,
    string ScanStatus,
    bool Restricted,
    string DeletionStatus,
    DateTimeOffset ObservedOn);

public sealed record FileStatusOutboxRecord(
    Guid EventId,
    string TenantNId,
    string FileNId,
    string ScanStatus,
    bool Restricted,
    string DeletionStatus,
    DateTimeOffset ObservedOn,
    int RetryCount,
    DateTimeOffset? PublishedOn,
    DateTimeOffset? NextAttemptOn,
    string? LastError,
    DateTimeOffset? DeadLetteredOn);

public interface IFileStatusOutbox
{
    Task EnqueueAsync(FileStatusChangeRecord item, CancellationToken cancellationToken);
    Task<IReadOnlyList<FileStatusOutboxRecord>> GetPendingAsync(DateTimeOffset now, int limit, CancellationToken cancellationToken);
    Task MarkPublishedAsync(Guid eventId, DateTimeOffset publishedOn, CancellationToken cancellationToken);
    Task<bool> RecordFailureAsync(Guid eventId, int retryCount, string lastError, bool deadLetter, DateTimeOffset nextAttemptOn, CancellationToken cancellationToken);
}

public interface IFileScanner
{
    Task<FileScanResult> ScanAsync(FileObjectRecord file, Stream content, CancellationToken cancellationToken);
}

/// <summary>
/// Coordinates the complete upload critical section (database read/CAS and the
/// corresponding physical file mutation). Implementations may coordinate across
/// processes when the content store is shared by multiple service instances.
/// </summary>
public interface IFileUploadCoordinator
{
    Task<IAsyncDisposable> AcquireAsync(string tenantNId, string sessionNId, CancellationToken cancellationToken);
}

public sealed class NoopFileUploadCoordinator : IFileUploadCoordinator
{
    private sealed class Lease : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    public Task<IAsyncDisposable> AcquireAsync(string tenantNId, string sessionNId, CancellationToken cancellationToken) =>
        Task.FromResult<IAsyncDisposable>(new Lease());
}

public interface IFileStore
{
    Task<FileUploadSessionRecord?> GetSessionAsync(string tenantNId, string sessionNId, CancellationToken cancellationToken);
    Task<FileUploadSessionRecord?> GetSessionByTransportAsync(string tenantNId, string transportId, CancellationToken cancellationToken);
    Task<IReadOnlyList<FileUploadSessionRecord>> ExpireSessionsAsync(DateTimeOffset now, CancellationToken cancellationToken);
    Task<IReadOnlyList<FileUploadSessionRecord>> FindCandidatesAsync(string tenantNId, string uploaderUserNId, string purpose, string sampleFingerprint, long length, CancellationToken cancellationToken);
    Task InsertSessionAsync(FileUploadSessionRecord session, CancellationToken cancellationToken);
    Task<bool> UpdateSessionAsync(FileUploadSessionRecord session, long expectedOffset, int expectedEpoch, CancellationToken cancellationToken);
    Task<bool> UpdateAppendAsync(FileUploadSessionRecord session, long expectedOffset, int expectedEpoch, string expectedStatus, CancellationToken cancellationToken) =>
        UpdateSessionAsync(session, expectedOffset, expectedEpoch, cancellationToken);
    Task<FileObjectRecord?> GetFileAsync(string tenantNId, string fileNId, CancellationToken cancellationToken);
    Task<FileObjectRecord?> GetFileForReconciliationAsync(string tenantNId, string fileNId, CancellationToken cancellationToken) =>
        GetFileAsync(tenantNId, fileNId, cancellationToken);
    Task<IReadOnlyList<FileObjectRecord>> ListPendingScanAsync(int limit, CancellationToken cancellationToken);
    Task<IReadOnlyList<FileObjectRecord>> ListDeletionCandidatesAsync(DateTimeOffset now, int limit, CancellationToken cancellationToken);
    Task MarkFileDeletedAsync(FileObjectRecord file, DateTimeOffset deletedOn, CancellationToken cancellationToken);
    Task<FilePageV1> ListFilesAsync(string tenantNId, string? search, int page, int pageSize, CancellationToken cancellationToken);
    async Task<FilePageV1> ListFilesPageAsync(string tenantNId, string? search, string? purpose, string? ownerUserNId, string? scanStatus, bool? restricted, int page, int pageSize, CancellationToken cancellationToken)
    {
        var normalizedPage = Math.Max(page, 1);
        var normalizedPageSize = Math.Clamp(pageSize, 1, 200);
        var result = await ListFilesAsync(tenantNId, search, 1, 200, cancellationToken);
        var filtered = result.Items.Where(item =>
            (string.IsNullOrWhiteSpace(purpose) || string.Equals(item.Purpose, purpose.Trim(), StringComparison.OrdinalIgnoreCase))
            && (string.IsNullOrWhiteSpace(ownerUserNId) || string.Equals(item.OwnerUserNId, ownerUserNId.Trim(), StringComparison.Ordinal))
            && (string.IsNullOrWhiteSpace(scanStatus) || string.Equals(item.ScanStatus, scanStatus.Trim(), StringComparison.OrdinalIgnoreCase))
            && (restricted is null || item.Restricted == restricted.Value)).ToArray();
        return new FilePageV1 { Items = filtered.Skip((normalizedPage - 1) * normalizedPageSize).Take(normalizedPageSize).ToArray(), Page = normalizedPage, PageSize = normalizedPageSize, Total = filtered.Length };
    }
    Task<FileObjectRecord?> CompleteSessionAsync(FileUploadSessionRecord completedSession, FileObjectRecord file, long expectedOffset, int expectedEpoch, CancellationToken cancellationToken);
    Task AddScanAttemptAsync(string tenantNId, string fileNId, string status, string detail, CancellationToken cancellationToken);
    Task<FileReferenceRecord?> GetReferenceAsync(string tenantNId, string referenceNId, CancellationToken cancellationToken);
    Task<FileReferenceRecord?> GetReferenceForFileAsync(string tenantNId, string fileNId, string ownerUserNId, CancellationToken cancellationToken);
    Task<bool> HasActiveReferencesAsync(string tenantNId, string fileNId, CancellationToken cancellationToken);
    Task<bool> HasActiveLegalHoldsAsync(string tenantNId, string fileNId, CancellationToken cancellationToken) => Task.FromResult(false);
    async Task<bool> TryRequestDeletionAsync(FileObjectRecord file, CancellationToken cancellationToken)
    {
        if (await HasActiveLegalHoldsAsync(file.TenantNId, file.FileNId, cancellationToken)
            || await HasActiveReferencesAsync(file.TenantNId, file.FileNId, cancellationToken)) return false;
        await UpdateFileAsync(file, cancellationToken);
        return true;
    }
    Task InsertReferenceAsync(FileReferenceRecord reference, CancellationToken cancellationToken);
    Task DeleteReferenceAsync(string tenantNId, string referenceNId, DateTimeOffset deletedOn, CancellationToken cancellationToken);
    Task<FileBusinessReferenceRecord?> GetBusinessReferenceAsync(string tenantNId, string referenceNId, CancellationToken cancellationToken) => Task.FromResult<FileBusinessReferenceRecord?>(null);
    Task<FileBusinessReferenceRecord?> GetBusinessReferenceForAttachmentAsync(string tenantNId, string attachmentNId, string purpose, CancellationToken cancellationToken) => Task.FromResult<FileBusinessReferenceRecord?>(null);
    Task InsertBusinessReferenceAsync(FileBusinessReferenceRecord reference, CancellationToken cancellationToken) => Task.FromException(new NotSupportedException("业务文件引用持久化未配置。"));
    Task<bool> ReleaseBusinessReferenceAsync(string tenantNId, string fileNId, string referenceNId, long expectedVersion, DateTimeOffset releasedOn, CancellationToken cancellationToken) => Task.FromResult(false);
    Task<FileHoldRecord?> GetLegalHoldAsync(string tenantNId, string caseNId, string fileNId, CancellationToken cancellationToken) => Task.FromResult<FileHoldRecord?>(null);
    Task InsertLegalHoldAsync(FileHoldRecord hold, CancellationToken cancellationToken) => Task.FromException(new NotSupportedException("文件 Legal Hold 持久化未配置。"));
    Task<bool> ReleaseLegalHoldAsync(string tenantNId, string caseNId, string fileNId, string scopeChecksum, long caseRevision, DateTimeOffset releasedOn, CancellationToken cancellationToken) => Task.FromResult(false);
    Task UpdateFileAsync(FileObjectRecord file, CancellationToken cancellationToken);
}

public interface IFileContentStore
{
    Task<long> AppendAsync(string storageKey, long expectedOffset, Stream content, CancellationToken cancellationToken);
    Task<long> EnsureLengthAsync(string storageKey, long expectedLength, CancellationToken cancellationToken) => Task.FromResult(expectedLength);
    Task<Stream> OpenReadAsync(string storageKey, CancellationToken cancellationToken);
    Task<string> ComputeSha256Async(string storageKey, CancellationToken cancellationToken);
    Task DeleteAsync(string storageKey, CancellationToken cancellationToken);
}

public interface IFileService
{
    Task<UploadSessionV1> CreateSessionAsync(string tenantNId, string userNId, CreateUploadSessionRequest request, CancellationToken cancellationToken);
    Task<FileUploadDiscoveryV1> DiscoverAsync(string tenantNId, string userNId, DiscoverUploadSessionRequest request, CancellationToken cancellationToken);
    Task<UploadSessionV1> GetSessionAsync(string tenantNId, string sessionNId, CancellationToken cancellationToken);
    Task<UploadSessionV1> GetSessionAsync(string tenantNId, string userNId, string sessionNId, CancellationToken cancellationToken);
    Task<UploadSessionV1> GetSessionByTransportAsync(string tenantNId, string transportId, CancellationToken cancellationToken);
    Task<UploadSessionV1> GetSessionByTransportAsync(string tenantNId, string userNId, string transportId, CancellationToken cancellationToken);
    Task<UploadSessionV1> SetContentHashAsync(string tenantNId, string sessionNId, string userNId, ContentHashRequest request, CancellationToken cancellationToken);
    Task<UploadSessionV1> ResumeProofAsync(string tenantNId, string sessionNId, string userNId, ResumeProofRequest request, CancellationToken cancellationToken);
    Task<UploadSessionV1> TakeoverAsync(string tenantNId, string sessionNId, string userNId, TakeoverUploadRequest request, CancellationToken cancellationToken);
    Task<UploadSessionV1> PauseAsync(string tenantNId, string sessionNId, string userNId, CancellationToken cancellationToken);
    Task<UploadSessionV1> ResumeAsync(string tenantNId, string sessionNId, string userNId, ResumeProofRequest request, CancellationToken cancellationToken);
    Task<UploadSessionV1> CancelAsync(string tenantNId, string sessionNId, string userNId, SetUploadStateRequest request, CancellationToken cancellationToken);
    Task<UploadSessionV1> AppendAsync(string tenantNId, string transportId, string userNId, long expectedOffset, int epoch, Stream content, CancellationToken cancellationToken);
    Task<UploadSessionV1> AppendAsync(string tenantNId, string transportId, string userNId, long expectedOffset, int epoch, Stream content, string? resumeTicket, CancellationToken cancellationToken);
    Task<FileObjectV1> CompleteAsync(string tenantNId, string sessionNId, string userNId, CancellationToken cancellationToken);
    Task<FileObjectV1?> GetFileAsync(string tenantNId, string fileNId, CancellationToken cancellationToken);
    Task<FileObjectV1?> GetFileForReconciliationAsync(string tenantNId, string fileNId, CancellationToken cancellationToken);
    Task<Stream> OpenFileContentAsync(string tenantNId, string userNId, string fileNId, string? referenceNId, CancellationToken cancellationToken);
    Task<FilePageV1> ListFilesAsync(string tenantNId, string? search, int page, int pageSize, CancellationToken cancellationToken);
    Task<FilePageV1> ListFilesPageAsync(string tenantNId, string? search, string? purpose, string? ownerUserNId, string? scanStatus, bool? restricted, int page, int pageSize, CancellationToken cancellationToken);
    Task<FileReferenceRecord> AddReferenceAsync(string tenantNId, string userNId, string fileNId, FileReferenceRequest request, CancellationToken cancellationToken);
    Task<FileBindingV1> BindReferenceAsync(string tenantNId, string userNId, string referenceNId, FileBindingRequest request, CancellationToken cancellationToken);
    Task<FileBindingV1> ReleaseReferenceAsync(string tenantNId, string userNId, string fileNId, string referenceNId, FileBindingReleaseRequest request, CancellationToken cancellationToken);
    Task<FileHoldV1> PutLegalHoldAsync(string tenantNId, string userNId, string caseNId, string fileNId, FileHoldRequest request, CancellationToken cancellationToken);
    Task<FileHoldV1> ReleaseLegalHoldAsync(string tenantNId, string userNId, string caseNId, string fileNId, FileHoldRequest request, CancellationToken cancellationToken);
    Task<FileHoldV1?> GetLegalHoldAsync(string tenantNId, string userNId, string caseNId, string fileNId, CancellationToken cancellationToken);
    Task DeleteReferenceAsync(string tenantNId, string userNId, string referenceNId, CancellationToken cancellationToken);
    Task DeleteReferenceAsync(string tenantNId, string userNId, string fileNId, string referenceNId, CancellationToken cancellationToken);
    Task<FileObjectV1> RequestDeletionAsync(string tenantNId, string userNId, string fileNId, CancellationToken cancellationToken);
    Task<FileObjectV1> SetRestrictionAsync(string tenantNId, string userNId, string fileNId, FileRestrictionRequest request, CancellationToken cancellationToken);
}
