using IndustrialPlatform.Collaboration.Application;
using IndustrialPlatform.Collaboration.Contracts;
using IndustrialPlatform.Collaboration.Infrastructure;
using IndustrialPlatform.Collaboration.Infrastructure.Persistence;
using IndustrialPlatform.Infrastructure.Database;
using IndustrialPlatform.Security;
using Microsoft.Extensions.Options;
using SqlSugar;

namespace IndustrialPlatform.Collaboration.Tests;

public sealed class Infrastructure_AttachmentMessageBindingTests
{
    [Fact]
    public async Task Scanned_attachment_is_authorized_before_send_then_bound_to_message_and_file_reference()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"pf05-collaboration-attachment-{Guid.NewGuid():N}.db");
        SqlSugarDbContext? context = null;
        try
        {
            context = CreateContext(databasePath);
            foreach (var migration in CollaborationSchemaMigrations.All)
                await migration.Apply(context.SqlSugar, CancellationToken.None);

            var repository = new SqlCollaborationRepository(context);
            var conversation = new ConversationRecord("T-1", "CV-1", "U-1", "U-2", "Active", 0, null, null, 0, 1, Guid.NewGuid());
            var member = new ConversationMemberRecord("T-1", "CV-1", "U-1", "Alice", DateTimeOffset.UtcNow, "Visible", null, 0, 0, null, 0, 1, Guid.NewGuid());
            await repository.CreateConversationAsync(conversation, member, member with { UserNId = "U-2", DisplayNameSnapshot = "Bob" }, CancellationToken.None);
            var attachment = new AttachmentRecord(
                "T-1", "CV-1", "ATT-1", "U-1", "FILE-1", "report.txt", "text/plain", 2,
                "CollaborationMessageAttachment", "Clean", 1, DateTimeOffset.UtcNow, "Pending", "Active", null,
                "REQ-ATT", "intent-hash", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
            await repository.CreateAttachmentAsync(attachment, CancellationToken.None);

            var files = new RecordingFilePort();
            var service = new CollaborationService(
                repository,
                new ActiveDirectory(),
                files,
                new NoopAuditPort(),
                new NoopPresence(),
                new NoopStepUpVerifier(),
                new NoopStepUpBindingIssuer(),
                new AllowAllPermissions(),
                new PageCursorCodec("test-signing-key", TimeSpan.FromMinutes(5)));

            var authorization = await service.AuthorizeAttachmentAsync("T-1", "U-1", "CV-1", "ATT-1", new AttachmentAuthorizationRequest
            {
                RequestNId = "REQ-AUTH",
                Purpose = "Display",
            }, CancellationToken.None);
            var sent = await service.SendMessageAsync("T-1", "U-1", "CV-1", new SendMessageRequest
            {
                ClientMessageNId = "CLIENT-1",
                MessageType = "File",
                AttachmentNId = "ATT-1",
            }, CancellationToken.None);
            var repeated = await service.SendMessageAsync("T-1", "U-1", "CV-1", new SendMessageRequest
            {
                ClientMessageNId = "CLIENT-1",
                MessageType = "File",
                AttachmentNId = "ATT-1",
            }, CancellationToken.None);

            var persistedMessage = await repository.GetMessageByIdAsync("T-1", sent.MessageNId, CancellationToken.None);
            var persistedAttachment = await repository.GetAttachmentByIdAsync("T-1", "ATT-1", CancellationToken.None);

            Assert.NotNull(persistedMessage);
            Assert.Equal("ATT-1", persistedMessage!.AttachmentNId);
            Assert.Equal(sent.MessageNId, repeated.MessageNId);
            Assert.NotNull(persistedAttachment);
            Assert.Equal(sent.MessageNId, persistedAttachment!.BoundMessageNId);
            Assert.Equal("Authorized", persistedAttachment.ReferenceState);
            Assert.Equal(authorization.ReferenceNId, persistedAttachment.ReferenceNId);
            Assert.Equal(2, files.BindReferenceCalls);
            Assert.Equal(("T-1", "CV-1", sent.MessageNId, "ATT-1", "U-1", "CollaborationMessageAttachment"), files.BoundReference);
        }
        finally
        {
            context?.Dispose();
            DeleteDatabase(databasePath);
        }
    }

    private sealed class ActiveDirectory : ICollaborationIdentityDirectory
    {
        public Task<DirectoryUser?> GetAsync(string tenantNId, string userNId, CancellationToken cancellationToken) =>
            Task.FromResult<DirectoryUser?>(new DirectoryUser(userNId, userNId, "Active", "v1"));

        public Task<DirectorySearchPage> SearchAsync(string tenantNId, string actorUserNId, string keyword, string? cursor, int pageSize, CancellationToken cancellationToken) =>
            Task.FromResult(new DirectorySearchPage([], null));
    }

    private sealed class RecordingFilePort : ICollaborationFilePort
    {
        public (string TenantNId, string ConversationNId, string MessageNId, string AttachmentNId, string UploaderUserNId, string Purpose)? BoundReference { get; private set; }
        public int BindReferenceCalls { get; private set; }

        public Task<AttachmentUploadResult> CreateUploadAsync(string tenantNId, string userNId, AttachmentRecord attachment, CancellationToken cancellationToken) => Unsupported<AttachmentUploadResult>();
        public Task<AttachmentUploadResult> GetUploadAsync(string tenantNId, string userNId, string sessionNId, CancellationToken cancellationToken) => Unsupported<AttachmentUploadResult>();
        public Task<AttachmentUploadResult> SetContentHashAsync(string tenantNId, string userNId, string sessionNId, string sha256, CancellationToken cancellationToken) => Unsupported<AttachmentUploadResult>();
        public Task<AttachmentUploadResult> ResumeProofAsync(string tenantNId, string userNId, string sessionNId, int writerEpoch, string proof, CancellationToken cancellationToken) => Unsupported<AttachmentUploadResult>();
        public Task<AttachmentUploadResult> TakeoverAsync(string tenantNId, string userNId, string sessionNId, int expectedWriterEpoch, string? idempotencyKey, string? proof, CancellationToken cancellationToken) => Unsupported<AttachmentUploadResult>();
        public Task<AttachmentUploadResult> PauseAsync(string tenantNId, string userNId, string sessionNId, CancellationToken cancellationToken) => Unsupported<AttachmentUploadResult>();
        public Task<AttachmentUploadResult> ResumeAsync(string tenantNId, string userNId, string sessionNId, int writerEpoch, string proof, CancellationToken cancellationToken) => Unsupported<AttachmentUploadResult>();
        public Task<AttachmentUploadResult> CancelAsync(string tenantNId, string userNId, string sessionNId, string? reason, CancellationToken cancellationToken) => Unsupported<AttachmentUploadResult>();
        public Task<AttachmentUploadResult> AppendAsync(string tenantNId, string userNId, string transportId, long expectedOffset, int writerEpoch, Stream content, string? resumeTicket, CancellationToken cancellationToken) => Unsupported<AttachmentUploadResult>();
        public Task<AttachmentUploadResult> CompleteUploadAsync(string tenantNId, string userNId, string sessionNId, CancellationToken cancellationToken) => Unsupported<AttachmentUploadResult>();
        public Task<string?> AddReferenceAsync(string tenantNId, string userNId, string fileNId, string referenceNId, CancellationToken cancellationToken, string? purpose = null) => Task.FromResult<string?>(referenceNId);

        public Task<string?> BindReferenceAsync(string tenantNId, string userNId, string fileNId, string referenceNId, string conversationNId, string messageNId, string attachmentNId, string uploaderUserNId, string purpose, string requestNId, CancellationToken cancellationToken)
        {
            BindReferenceCalls++;
            BoundReference = (tenantNId, conversationNId, messageNId, attachmentNId, uploaderUserNId, purpose);
            return Task.FromResult<string?>(referenceNId);
        }

        public Task<CollaborationFileState?> GetAsync(string tenantNId, string userNId, string fileNId, CancellationToken cancellationToken) =>
            Task.FromResult<CollaborationFileState?>(new CollaborationFileState(fileNId, "report.txt", "text/plain", 2, "Clean", false, "Available"));

        public Task<Stream> OpenContentAsync(string tenantNId, string userNId, string fileNId, string referenceNId, CancellationToken cancellationToken) =>
            Task.FromResult<Stream>(new MemoryStream());

        private static Task<T> Unsupported<T>() => Task.FromException<T>(new NotSupportedException());
    }

    private sealed class NoopAuditPort : ICollaborationAuditPort
    {
        public Task WriteAsync(string tenantNId, TrustedCollaborationCall? serviceCall, string actorUserNId, string action, string objectType, string objectNId, object payload, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class NoopPresence : ICollaborationPresence
    {
        public PresenceDto SetPresence(string tenantNId, string userNId, string state, string? connectionNId = null) => new();
        public PresenceDto GetPresence(string tenantNId, string userNId) => new();
        public void RemovePresence(string tenantNId, string userNId, string connectionNId) { }
    }

    private sealed class NoopStepUpVerifier : IComplianceStepUpVerifier
    {
        public Task EnsureValidAsync(string proof, string tenantNId, string actorUserNId, string actorSessionNId, string actorSecurityVersion, string action, string requestNId, string scopeChecksum, string requestHash, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class NoopStepUpBindingIssuer : IStepUpBindingIssuer
    {
        public string Issue(string tenantNId, string actorUserNId, string actorSessionNId, string actorSecurityVersion, string action, string requestNId, string scopeChecksum, string requestHash, DateTimeOffset issuedOn, TimeSpan lifetime) => "binding";
    }

    private sealed class AllowAllPermissions : ICollaborationPermissionEvaluator
    {
        public Task<bool> HasPermissionAsync(string permission, string tenantNId, string actorUserNId, string actorSessionNId, string actorSecurityVersion, CancellationToken cancellationToken) => Task.FromResult(true);
    }

    private static SqlSugarDbContext CreateContext(string databasePath) =>
        new(Options.Create(new SqlSugarOptions { DbType = DbType.Sqlite, ConnectionString = $"Data Source={databasePath}", IsAutoCloseConnection = true }));

    private static void DeleteDatabase(string databasePath)
    {
        if (!File.Exists(databasePath))
            return;
        for (var attempt = 0; attempt < 10; attempt++)
        {
            try
            {
                File.Delete(databasePath);
                return;
            }
            catch (IOException) when (attempt < 9)
            {
                Thread.Sleep(25);
            }
            catch (IOException)
            {
                return;
            }
        }
    }
}
