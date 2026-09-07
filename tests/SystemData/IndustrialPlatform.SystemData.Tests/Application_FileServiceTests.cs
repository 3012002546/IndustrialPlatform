using System.Security.Cryptography;
using IndustrialPlatform.SystemData.Application.Files;
using IndustrialPlatform.SystemData.Application.Pf04;
using IndustrialPlatform.SystemData.Contracts.Files;

namespace IndustrialPlatform.SystemData.Tests;

public sealed class FileServiceTests
{
    [Fact]
    public async Task Pause_blocks_append_and_resume_ticket_is_offset_bound()
    {
        var store = new FileStoreStub();
        var content = new MemoryContentStore();
        var service = new FileService(store, content, TimeProvider.System);
        var session = await service.CreateSessionAsync("tenant-1", "user-1", new CreateUploadSessionRequest { SessionNId = "session-1", TransportId = "transport-1", FileName = "a.txt", Length = 2, Sha256 = Sha256("ab") }, CancellationToken.None);
        await service.SetContentHashAsync("tenant-1", "session-1", "user-1", new ContentHashRequest { Sha256 = Sha256("ab") }, CancellationToken.None);
        var ready = await service.ResumeProofAsync("tenant-1", "session-1", "user-1", new ResumeProofRequest { WriterEpoch = 1, Proof = Sha256("ab") }, CancellationToken.None);

        var afterFirstChunk = await service.AppendAsync("tenant-1", "transport-1", "user-1", 0, 1, new MemoryStream("a"u8.ToArray()), ready.ResumeTicket, CancellationToken.None);
        var staleTicket = ready.ResumeTicket!;
        var invalid = await Assert.ThrowsAsync<Pf04ServiceException>(() => service.AppendAsync("tenant-1", "transport-1", "user-1", 1, 1, new MemoryStream("b"u8.ToArray()), staleTicket, CancellationToken.None));
        Assert.Equal("FILE_UPLOAD_RESUME_TICKET_INVALID", invalid.Code);

        await service.PauseAsync("tenant-1", "session-1", "user-1", CancellationToken.None);
        var paused = await Assert.ThrowsAsync<Pf04ServiceException>(() => service.AppendAsync("tenant-1", "transport-1", "user-1", afterFirstChunk.Offset, 1, new MemoryStream("b"u8.ToArray()), afterFirstChunk.ResumeTicket, CancellationToken.None));
        Assert.Equal("FILE_UPLOAD_PROOF_REQUIRED", paused.Code);
    }

    [Fact]
    public async Task Takeover_requires_epoch_and_moves_writer_authorization()
    {
        var store = new FileStoreStub();
        var service = new FileService(store, new MemoryContentStore(), TimeProvider.System);
        await service.CreateSessionAsync("tenant-1", "user-1", new CreateUploadSessionRequest { SessionNId = "session-2", TransportId = "transport-2", FileName = "a.txt", Length = 0, Sha256 = Sha256(string.Empty) }, CancellationToken.None);
        await service.SetContentHashAsync("tenant-1", "session-2", "user-1", new ContentHashRequest { Sha256 = Sha256(string.Empty) }, CancellationToken.None);

        var missingEpoch = await Assert.ThrowsAsync<Pf04ServiceException>(() => service.TakeoverAsync("tenant-1", "session-2", "user-2", new TakeoverUploadRequest { IdempotencyKey = "takeover-1", Proof = Sha256(string.Empty) }, CancellationToken.None));
        Assert.Equal("FILE_UPLOAD_EPOCH_REQUIRED", missingEpoch.Code);

        var missingKey = await Assert.ThrowsAsync<Pf04ServiceException>(() => service.TakeoverAsync("tenant-1", "session-2", "user-2", new TakeoverUploadRequest { ExpectedWriterEpoch = 1, Proof = Sha256(string.Empty) }, CancellationToken.None));
        Assert.Equal("FILE_UPLOAD_IDEMPOTENCY_REQUIRED", missingKey.Code);
        var takeover = await service.TakeoverAsync("tenant-1", "session-2", "user-2", new TakeoverUploadRequest { ExpectedWriterEpoch = 1, IdempotencyKey = "takeover-1", Proof = Sha256(string.Empty) }, CancellationToken.None);
        Assert.Equal(2, takeover.WriterEpoch);
        await Assert.ThrowsAsync<Pf04ServiceException>(() => service.GetSessionAsync("tenant-1", "user-1", "session-2", CancellationToken.None));
        var newOwner = await service.GetSessionAsync("tenant-1", "user-2", "session-2", CancellationToken.None);
        Assert.Equal(2, newOwner.WriterEpoch);
        Assert.NotNull(newOwner.ResumeTicket);
        var retry = await service.TakeoverAsync("tenant-1", "session-2", "user-2", new TakeoverUploadRequest { ExpectedWriterEpoch = 1, IdempotencyKey = "takeover-1", Proof = Sha256(string.Empty) }, CancellationToken.None);
        Assert.Equal(takeover.WriterEpoch, retry.WriterEpoch);
    }

    [Fact]
    public async Task Failed_database_cas_truncates_the_physical_tail_before_a_new_writer_can_take_over()
    {
        var store = new FileStoreStub { FailNextAppendCas = true };
        var content = new MemoryContentStore();
        var service = new FileService(store, content, TimeProvider.System);
        await service.CreateSessionAsync("tenant-1", "user-1", new CreateUploadSessionRequest { SessionNId = "session-3", TransportId = "transport-3", FileName = "a.txt", Length = 2, Sha256 = Sha256("ab") }, CancellationToken.None);
        await service.SetContentHashAsync("tenant-1", "session-3", "user-1", new ContentHashRequest { Sha256 = Sha256("ab") }, CancellationToken.None);
        var ready = await service.ResumeProofAsync("tenant-1", "session-3", "user-1", new ResumeProofRequest { WriterEpoch = 1, Proof = Sha256("ab") }, CancellationToken.None);

        var failure = await Assert.ThrowsAsync<Pf04ServiceException>(() => service.AppendAsync("tenant-1", "transport-3", "user-1", 0, 1, new MemoryStream("ab"u8.ToArray()), ready.ResumeTicket, CancellationToken.None));

        Assert.Equal("FILE_UPLOAD_WRITER_REPLACED", failure.Code);
        Assert.Equal(0, content.GetLength("tenant-1/session-3.bin"));
        var takeover = await service.TakeoverAsync("tenant-1", "session-3", "user-2", new TakeoverUploadRequest { ExpectedWriterEpoch = 1, IdempotencyKey = "takeover-3", Proof = Sha256("ab") }, CancellationToken.None);
        Assert.Equal(2, takeover.WriterEpoch);
    }

    [Fact]
    public async Task Complete_rejects_content_when_hash_does_not_match_the_upload_proof()
    {
        var store = new FileStoreStub();
        var content = new MemoryContentStore();
        var service = new FileService(store, content, TimeProvider.System);
        await service.CreateSessionAsync("tenant-1", "user-1", new CreateUploadSessionRequest { SessionNId = "session-hash", TransportId = "transport-hash", FileName = "a.txt", Length = 2, Sha256 = Sha256("ab") }, CancellationToken.None);
        await service.SetContentHashAsync("tenant-1", "session-hash", "user-1", new ContentHashRequest { Sha256 = Sha256("ab") }, CancellationToken.None);
        var ready = await service.ResumeProofAsync("tenant-1", "session-hash", "user-1", new ResumeProofRequest { WriterEpoch = 1, Proof = Sha256("ab") }, CancellationToken.None);
        await service.AppendAsync("tenant-1", "transport-hash", "user-1", 0, 1, new MemoryStream("ax"u8.ToArray()), ready.ResumeTicket, CancellationToken.None);

        var exception = await Assert.ThrowsAsync<Pf04ServiceException>(() => service.CompleteAsync("tenant-1", "session-hash", "user-1", CancellationToken.None));

        Assert.Equal("FILE_UPLOAD_CONTENT_MISMATCH", exception.Code);
    }

    [Fact]
    public async Task Complete_is_idempotent_after_the_session_has_been_persisted()
    {
        var store = new FileStoreStub();
        var service = new FileService(store, new MemoryContentStore(), TimeProvider.System);
        await service.CreateSessionAsync("tenant-1", "user-1", new CreateUploadSessionRequest { SessionNId = "session-complete", TransportId = "transport-complete", FileName = "a.txt", Length = 0, Sha256 = Sha256(string.Empty) }, CancellationToken.None);
        await service.SetContentHashAsync("tenant-1", "session-complete", "user-1", new ContentHashRequest { Sha256 = Sha256(string.Empty) }, CancellationToken.None);
        await service.ResumeProofAsync("tenant-1", "session-complete", "user-1", new ResumeProofRequest { WriterEpoch = 1, Proof = Sha256(string.Empty) }, CancellationToken.None);

        var first = await service.CompleteAsync("tenant-1", "session-complete", "user-1", CancellationToken.None);
        var retry = await service.CompleteAsync("tenant-1", "session-complete", "user-1", CancellationToken.None);

        Assert.Equal(first.FileNId, retry.FileNId);
    }

    [Theory]
    [InlineData("PendingScan", "FILE_SCAN_PENDING")]
    [InlineData("Malicious", "FILE_MALICIOUS")]
    [InlineData("Unknown", "FILE_SCAN_PENDING")]
    public async Task Download_is_blocked_until_the_file_has_a_clean_scan(string scanStatus, string errorCode)
    {
        var store = new FileStoreStub
        {
            File = new FileObjectRecord("tenant-1", "file-1", "session-1", "a.txt", "text/plain", 2, Sha256("ab"), "tenant-1/session-1.bin", scanStatus, false, "Active", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null, null, "user-1")
        };
        var service = new FileService(store, new MemoryContentStore(), TimeProvider.System);

        var exception = await Assert.ThrowsAsync<Pf04ServiceException>(() => service.OpenFileContentAsync("tenant-1", "user-1", "file-1", null, CancellationToken.None));

        Assert.Equal(errorCode, exception.Code);
    }

    [Fact]
    public async Task Deletion_is_blocked_by_retention_and_active_references()
    {
        var now = DateTimeOffset.UtcNow;
        var store = new FileStoreStub
        {
            File = new FileObjectRecord("tenant-1", "file-1", "session-1", "a.txt", "text/plain", 2, Sha256("ab"), "tenant-1/session-1.bin", "Clean", false, "Active", now, now, null, now.AddHours(1), "user-1"),
            ActiveReference = new FileReferenceRecord("tenant-1", "ref-1", "file-1", "user-2", "report", now, null)
        };
        var service = new FileService(store, new MemoryContentStore(), TimeProvider.System);

        var retained = await Assert.ThrowsAsync<Pf04ServiceException>(() => service.RequestDeletionAsync("tenant-1", "user-1", "file-1", CancellationToken.None));
        Assert.Equal("FILE_RETENTION_ACTIVE", retained.Code);
        store.File = store.File! with { RetentionUntil = null };
        var referenced = await Assert.ThrowsAsync<Pf04ServiceException>(() => service.RequestDeletionAsync("tenant-1", "user-1", "file-1", CancellationToken.None));
        Assert.Equal("FILE_REFERENCED", referenced.Code);
    }

    private static string Sha256(string value) => Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private sealed class MemoryContentStore : IFileContentStore
    {
        private readonly Dictionary<string, byte[]> _files = new(StringComparer.Ordinal);

        public async Task<long> AppendAsync(string storageKey, long expectedOffset, Stream content, CancellationToken cancellationToken)
        {
            await using var buffer = new MemoryStream();
            await content.CopyToAsync(buffer, cancellationToken);
            var existing = _files.GetValueOrDefault(storageKey) ?? [];
            if (existing.LongLength != expectedOffset) throw new InvalidDataException();
            _files[storageKey] = existing.Concat(buffer.ToArray()).ToArray();
            return _files[storageKey].LongLength;
        }

        public Task<Stream> OpenReadAsync(string storageKey, CancellationToken cancellationToken) => Task.FromResult<Stream>(new MemoryStream(_files.GetValueOrDefault(storageKey) ?? []));
        public Task<long> EnsureLengthAsync(string storageKey, long expectedLength, CancellationToken cancellationToken)
        {
            var existing = _files.GetValueOrDefault(storageKey) ?? [];
            if (existing.LongLength < expectedLength) throw new InvalidDataException();
            _files[storageKey] = existing.Take((int)expectedLength).ToArray();
            return Task.FromResult(expectedLength);
        }
        public Task<string> ComputeSha256Async(string storageKey, CancellationToken cancellationToken) => Task.FromResult(Convert.ToHexString(SHA256.HashData(_files.GetValueOrDefault(storageKey) ?? [])).ToLowerInvariant());
        public Task DeleteAsync(string storageKey, CancellationToken cancellationToken) { _files.Remove(storageKey); return Task.CompletedTask; }
        public long GetLength(string storageKey) => _files.GetValueOrDefault(storageKey)?.LongLength ?? 0;
    }

    private sealed class FileStoreStub : IFileStore
    {
        private FileUploadSessionRecord? _session;
        private FileObjectRecord? _file;
        public FileObjectRecord? File { get => _file; set => _file = value; }
        public FileReferenceRecord? ActiveReference { get; set; }
        public bool FailNextAppendCas { get; set; }
        public Task<FileUploadSessionRecord?> GetSessionAsync(string tenantNId, string sessionNId, CancellationToken cancellationToken) => Task.FromResult(_session is { TenantNId: var t, SessionNId: var s } && t == tenantNId && s == sessionNId ? _session : null);
        public Task<FileUploadSessionRecord?> GetSessionByTransportAsync(string tenantNId, string transportId, CancellationToken cancellationToken) => Task.FromResult(_session is { TenantNId: var t, TransportId: var tr } && t == tenantNId && tr == transportId ? _session : null);
        public Task<IReadOnlyList<FileUploadSessionRecord>> ExpireSessionsAsync(DateTimeOffset now, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<FileUploadSessionRecord>>([]);
        public Task<IReadOnlyList<FileUploadSessionRecord>> FindCandidatesAsync(string tenantNId, string uploaderUserNId, string purpose, string sampleFingerprint, long length, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<FileUploadSessionRecord>>([]);
        public Task InsertSessionAsync(FileUploadSessionRecord session, CancellationToken cancellationToken) { _session = session; return Task.CompletedTask; }
        public Task<bool> UpdateSessionAsync(FileUploadSessionRecord session, long expectedOffset, int expectedEpoch, CancellationToken cancellationToken)
        {
            if (_session is null || _session.CurrentOffset != expectedOffset || _session.WriterEpoch != expectedEpoch) return Task.FromResult(false);
            _session = session;
            return Task.FromResult(true);
        }
        public Task<bool> UpdateAppendAsync(FileUploadSessionRecord session, long expectedOffset, int expectedEpoch, string expectedStatus, CancellationToken cancellationToken)
        {
            if (FailNextAppendCas)
            {
                FailNextAppendCas = false;
                return Task.FromResult(false);
            }
            return UpdateSessionAsync(session, expectedOffset, expectedEpoch, cancellationToken);
        }
        public Task<FileObjectRecord?> GetFileAsync(string tenantNId, string fileNId, CancellationToken cancellationToken) => Task.FromResult(_file is { TenantNId: var t, FileNId: var f } && t == tenantNId && f == fileNId ? _file : null);
        public Task<IReadOnlyList<FileObjectRecord>> ListPendingScanAsync(int limit, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<FileObjectRecord>>([]);
        public Task<IReadOnlyList<FileObjectRecord>> ListDeletionCandidatesAsync(DateTimeOffset now, int limit, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<FileObjectRecord>>([]);
        public Task MarkFileDeletedAsync(FileObjectRecord file, DateTimeOffset deletedOn, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<FilePageV1> ListFilesAsync(string tenantNId, string? search, int page, int pageSize, CancellationToken cancellationToken) => Task.FromResult(new FilePageV1());
        public Task<FileObjectRecord?> CompleteSessionAsync(FileUploadSessionRecord completedSession, FileObjectRecord file, long expectedOffset, int expectedEpoch, CancellationToken cancellationToken)
        {
            if (_session is { Status: "Completed", FileNId: not null }) return Task.FromResult(_file);
            _session = completedSession;
            _file = file;
            return Task.FromResult<FileObjectRecord?>(file);
        }
        public Task AddScanAttemptAsync(string tenantNId, string fileNId, string status, string detail, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<FileReferenceRecord?> GetReferenceAsync(string tenantNId, string referenceNId, CancellationToken cancellationToken) => Task.FromResult(ActiveReference is { TenantNId: var t, ReferenceNId: var r } && t == tenantNId && r == referenceNId ? ActiveReference : null);
        public Task<FileReferenceRecord?> GetReferenceForFileAsync(string tenantNId, string fileNId, string ownerUserNId, CancellationToken cancellationToken) => Task.FromResult(ActiveReference is { TenantNId: var t, FileNId: var f, OwnerUserNId: var o } && t == tenantNId && f == fileNId && o == ownerUserNId ? ActiveReference : null);
        public Task<bool> HasActiveReferencesAsync(string tenantNId, string fileNId, CancellationToken cancellationToken) => Task.FromResult(ActiveReference is { TenantNId: var t, FileNId: var f } && t == tenantNId && f == fileNId);
        public Task InsertReferenceAsync(FileReferenceRecord reference, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task DeleteReferenceAsync(string tenantNId, string referenceNId, DateTimeOffset deletedOn, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task UpdateFileAsync(FileObjectRecord file, CancellationToken cancellationToken) { _file = file; return Task.CompletedTask; }
    }
}
