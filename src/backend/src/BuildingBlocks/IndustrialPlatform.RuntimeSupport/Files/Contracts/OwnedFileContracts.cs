namespace IndustrialPlatform.RuntimeSupport.Files;

/// <summary>仅由已验证服务适配器构建；不作为浏览器请求体绑定。</summary>
public sealed record TrustedServiceContext(string TenantNId, string ServiceKey, string ActorUserNId);
public sealed record OwnedFilePrepareV1(string OperationKey, string OwnerService, string OwnerKind, string OwnerNId, string FileName, string ContentType, long ExpectedLength, string ExpectedSha256);
public sealed record OwnedFilePreparedV1(string UploadSessionNId, string ReferenceNId, DateTimeOffset ExpiresOn);
public sealed record OwnedFileCommitV1(string ReferenceNId, string FileNId, string Sha256);
public sealed record OwnedFileStatusV1(string ReferenceNId, string? FileNId, string Status, string Version, DateTimeOffset? ExpiresOn);
public sealed record OwnedFileReadV1(string ReferenceNId, string ActorUserNId, string AuthorizationTicket);
public sealed record OwnedFileReadTicketV1(string AuthorizationTicket, DateTimeOffset ExpiresOn);
public sealed record OwnedFileReleaseV1(string ReferenceNId, string ExpectedVersion, string Reason);
public sealed record OwnedFileRetentionV1(string ReferenceNId, string ExpectedVersion, DateTimeOffset? RetentionUntil, bool LegalHold, string? HoldReason);
public sealed record OwnedFileListV1(IReadOnlyList<OwnedFileStatusV1> Items, string? NextCursor);
public interface IOwnedFileService
{
    Task<OwnedFilePreparedV1> PrepareAsync(TrustedServiceContext context, OwnedFilePrepareV1 request, CancellationToken cancellationToken);
    Task<OwnedFileStatusV1> CommitAsync(TrustedServiceContext context, OwnedFileCommitV1 request, CancellationToken cancellationToken);
    Task<Stream> ReadAsync(TrustedServiceContext context, OwnedFileReadV1 request, CancellationToken cancellationToken);
    Task<OwnedFileReadTicketV1> GetReadTicketAsync(TrustedServiceContext context, string referenceNId, CancellationToken cancellationToken);
    Task<OwnedFileStatusV1> ReleaseAsync(TrustedServiceContext context, OwnedFileReleaseV1 request, CancellationToken cancellationToken);
    Task<OwnedFileStatusV1> GetStatusAsync(TrustedServiceContext context, string referenceNId, CancellationToken cancellationToken);
    Task<OwnedFileStatusV1> SetRetentionAsync(TrustedServiceContext context, OwnedFileRetentionV1 request, CancellationToken cancellationToken);
    Task<OwnedFileListV1> ListByOwnerAsync(TrustedServiceContext context, string ownerKind, string ownerNId, string? cursor, CancellationToken cancellationToken);
}
