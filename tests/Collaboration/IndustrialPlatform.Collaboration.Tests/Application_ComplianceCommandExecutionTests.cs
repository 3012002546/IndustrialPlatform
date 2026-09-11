using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using IndustrialPlatform.Collaboration.Application;
using IndustrialPlatform.Collaboration.Contracts;
using IndustrialPlatform.Collaboration.Infrastructure;
using IndustrialPlatform.Security;
using IndustrialPlatform.SystemData.Contracts.Files;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace IndustrialPlatform.Collaboration.Tests;

public sealed class ComplianceCommandExecutionTests
{
    private static readonly string[] ExportFields = ["messageNId", "textContent"];
    private static readonly string[] ExportMessages = ["M-1"];

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Sensitive_export_preparation_freezes_the_full_command_and_final_execution_uses_it(bool isAdministrator)
    {
        var repository = new FakeRepository();
        var service = CreateService(repository, permissions: new AllowPermissions { IsAdministrator = isAdministrator });
        var scope = new ComplianceScopeDto
        {
            ScopeType = "TimeRange",
            FromOn = DateTimeOffset.UtcNow.AddHours(-1),
            ToOn = DateTimeOffset.UtcNow,
        };
        var contextRequest = new StepUpContextRequest
        {
            Action = "compliance.export.request",
            RequestNId = "REQ-export-001",
            Scope = scope,
            Fields = ["textContent", "messageNId"],
            Reason = "incident review",
            CaseReference = "CASE-001",
        };

        await service.CreateStepUpContextAsync("T-1", "U-1", contextRequest, "SID-1", "7", CancellationToken.None);

        Assert.NotNull(repository.Preparation);
        var snapshot = JsonSerializer.Deserialize<ComplianceCommandSnapshot>(repository.Preparation!.CommandJson);
        Assert.NotNull(snapshot);
        Assert.Equal("compliance.read-original", snapshot!.Action);
        Assert.True(snapshot.ReadOriginal);
        Assert.Equal("CASE-001", snapshot.CaseReference);
        Assert.Equal(["messageNId", "textContent"], snapshot.Fields);
        Assert.Equal(repository.Preparation.RequestHash, ComplianceCommandCanonicalizer.Hash(snapshot));

        var export = await service.PrepareExportAsync(
            "T-1",
            "U-1",
            new PrepareComplianceExportRequest
            {
                RequestNId = "REQ-export-001",
                Scope = scope,
                Fields = ["textContent", "messageNId"],
                Reason = "incident review",
                CaseReference = "CASE-001",
            },
            "proof",
            "REQ-export-001",
            "SID-1",
            "7",
            CancellationToken.None);

        Assert.Equal("EXP-1", export.ExportNId);
        Assert.NotNull(repository.CreatedExport);
        Assert.Equal(isAdministrator ? "Queued" : "PendingApproval", repository.CreatedExport!.State);
        Assert.Equal(isAdministrator ? "U-1" : null, repository.CreatedExport.ApprovedByUserNId);

        await Assert.ThrowsAsync<CollaborationException>(() => service.PrepareExportAsync(
            "T-1",
            "U-1",
            new PrepareComplianceExportRequest
            {
                RequestNId = "REQ-export-001",
                Scope = scope,
                Fields = ["textContent", "messageNId"],
                Reason = "tampered reason",
                CaseReference = "CASE-001",
            },
            "proof",
            "REQ-export-001",
            "SID-1",
            "7",
            CancellationToken.None));
    }

    [Fact]
    public async Task Export_content_rechecks_original_permission_after_authorization()
    {
        var repository = new FakeRepository
        {
            Export = new ComplianceExportRecord(
                "T-1",
                "EXP-1",
                "Succeeded",
                JsonSerializer.Serialize(new
                {
                    scope = new ComplianceScopeDto { ScopeType = "MessageSet", MessageNIds = ExportMessages },
                    fields = ExportFields,
                }),
                new string('a', 64),
                "incident review",
                "U-1",
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow.AddHours(1),
                "FILE-1|REF-1",
                1,
                Guid.NewGuid(),
                CaseReference: "CASE-001",
                FieldsJson: "[\"messageNId\",\"textContent\"]"),
        };
        var files = new FakeFiles
        {
            File = new CollaborationFileState("FILE-1", "export.json", "application/json", 10, "Clean", false, "Available"),
        };
        var permissions = new AllowPermissions();
        var service = CreateService(repository, files, permissions);
        var requestNId = "REQ-download-001";
        var context = await service.CreateStepUpContextAsync(
            "T-1",
            "U-1",
            new StepUpContextRequest { Action = "compliance.export.download", TargetNId = "EXP-1", RequestNId = requestNId },
            "SID-1",
            "7",
            CancellationToken.None);

        await service.AuthorizeExportDownloadAsync(
            "T-1",
            "U-1",
            "EXP-1",
            new ComplianceDownloadAuthorizationRequest { RequestNId = requestNId },
            "proof",
            "SID-1",
            "7",
            CancellationToken.None);

        permissions.Allowed = false;
        var revoked = await Assert.ThrowsAsync<CollaborationException>(() => service.OpenExportContentAsync(
            "T-1", "U-1", "EXP-1", requestNId, context.Binding, "SID-1", "7", CancellationToken.None));

        Assert.Equal(403, revoked.StatusCode);
        Assert.True(string.Equals("COLLAB_EXPORT_ORIGINAL_PERMISSION_REQUIRED", revoked.Code, StringComparison.Ordinal), revoked.Message);
        Assert.Equal(0, files.OpenContentCalls);

        permissions.Allowed = true;
        var content = await service.OpenExportContentAsync(
            "T-1", "U-1", "EXP-1", requestNId, context.Binding, "SID-1", "7", CancellationToken.None);

        Assert.Equal("export.json", content.FileName);
        Assert.Equal(1, files.OpenContentCalls);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Export_approval_freezes_the_current_concurrency_versions(bool isAdministrator, bool ownExport)
    {
        var concurrencyVersion = Guid.NewGuid();
        var repository = new FakeRepository
        {
            Export = new ComplianceExportRecord(
                "T-1",
                "EXP-1",
                "PendingApproval",
                JsonSerializer.Serialize(new
                {
                    scope = new ComplianceScopeDto { ScopeType = "MessageSet", MessageNIds = ExportMessages },
                    fields = ExportFields,
                }),
                new string('b', 64),
                "incident review",
                ownExport ? "U-1" : "U-2",
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow.AddHours(1),
                null,
                4,
                concurrencyVersion,
                CaseReference: "CASE-001",
                FieldsJson: JsonSerializer.Serialize(ExportFields)),
        };
        var service = CreateService(repository, permissions: new AllowPermissions { IsAdministrator = isAdministrator });
        await service.CreateStepUpContextAsync(
            "T-1",
            "U-1",
            new StepUpContextRequest
            {
                Action = "compliance.export.approve",
                TargetNId = "EXP-1",
                RequestNId = "REQ-approve-001",
                Reason = "approve export",
            },
            "SID-1",
            "7",
            CancellationToken.None);

        if (ownExport && !isAdministrator)
        {
            var denied = await Assert.ThrowsAsync<CollaborationException>(() => service.ApproveExportAsync(
                "T-1", "U-1", "EXP-1", new ApproveComplianceExportRequest { RequestNId = "REQ-approve-001", Reason = "approve export" },
                "proof", "REQ-approve-001", "SID-1", "7", CancellationToken.None));
            Assert.Equal("COLLAB_EXPORT_SELF_APPROVAL_FORBIDDEN", denied.Code);
            return;
        }
        var result = await service.ApproveExportAsync(
            "T-1",
            "U-1",
            "EXP-1",
            new ApproveComplianceExportRequest { RequestNId = "REQ-approve-001", Reason = "approve export" },
            "proof",
            "REQ-approve-001",
            "SID-1",
            "7",
            CancellationToken.None);

        Assert.Equal("Queued", result.State);
        Assert.Equal("U-1", repository.UpdatedExport?.ApprovedByUserNId);
    }

    [Fact]
    public async Task Message_history_before_the_retention_floor_returns_stable_410()
    {
        var repository = new FakeRepository
        {
            Conversation = new ConversationRecord("T-1", "CV-1", "U-1", "U-2", "Active", 12, "M-12", DateTimeOffset.UtcNow, 10, 2, Guid.NewGuid()),
            Member = new ConversationMemberRecord("T-1", "CV-1", "U-1", "Alice", DateTimeOffset.UtcNow, "Visible", null, 0, 0, null, 0, 1, Guid.NewGuid()),
        };
        var service = CreateService(repository);

        var exception = await Assert.ThrowsAsync<CollaborationException>(() => service.GetMessagesAsync(
            "T-1", "U-1", "CV-1", "after", 5, 20, CancellationToken.None));

        Assert.Equal(410, exception.StatusCode);
        Assert.Equal("COLLAB_MESSAGE_HISTORY_EXPIRED", exception.Code);
    }

    [Fact]
    public async Task Retracted_message_is_returned_as_a_tombstone_without_body_or_attachment()
    {
        var repository = new FakeRepository
        {
            Conversation = new ConversationRecord("T-1", "CV-1", "U-1", "U-2", "Active", 1, "M-1", DateTimeOffset.UtcNow, 0, 2, Guid.NewGuid()),
            Member = new ConversationMemberRecord("T-1", "CV-1", "U-1", "Alice", DateTimeOffset.UtcNow, "Visible", null, 0, 0, null, 0, 1, Guid.NewGuid()),
            Messages =
            [
                new MessageRecord("T-1", "CV-1", "M-1", 1, "U-1", "CLIENT-1", "hash", "Text", "secret body", null, "ATT-1", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "U-1", "sender_retract", 2, Guid.NewGuid()),
            ],
        };
        var service = CreateService(repository);

        var page = await service.GetMessagesAsync("T-1", "U-1", "CV-1", "after", 0, 20, CancellationToken.None);

        var item = Assert.Single(page.Items);
        Assert.Equal("Retracted", item.State);
        Assert.Null(item.TextContent);
        Assert.Null(item.Attachment);
    }

    [Fact]
    public async Task Two_workers_claim_one_export_and_do_not_duplicate_the_artifact()
    {
        var repository = WorkerRepository(approvalExpiresOn: DateTimeOffset.UtcNow.AddMinutes(5));
        var files = new FakeFiles();
        var audit = new FakeAudit();
        var worker1 = new CollaborationExportWorker(repository, files, audit);
        var worker2 = new CollaborationExportWorker(repository, files, audit);

        await Task.WhenAll(worker1.ProcessOnceAsync(CancellationToken.None), worker2.ProcessOnceAsync(CancellationToken.None));

        Assert.True(repository.Export!.State == "Succeeded", $"state={repository.Export.State}; error={repository.Export.ErrorCode}; create={files.CreateExportArtifactCalls}; add={files.AddExportReferenceCalls}");
        Assert.Equal(1, repository.ClaimSuccessCount);
        Assert.Equal(1, files.CreateExportArtifactCalls);
        Assert.Equal(1, files.AddExportReferenceCalls);
    }

    [Fact]
    public async Task Worker_takeover_of_expired_lease_respects_the_run_deadline()
    {
        var repository = WorkerRepository(
            approvalExpiresOn: DateTimeOffset.UtcNow.AddMinutes(5),
            state: "Running",
            runDeadlineOn: DateTimeOffset.UtcNow.AddSeconds(-1),
            workerLeaseUntil: DateTimeOffset.UtcNow.AddSeconds(-1));
        var files = new FakeFiles();
        var worker = new CollaborationExportWorker(repository, files, new FakeAudit());

        await worker.ProcessOnceAsync(CancellationToken.None);

        Assert.Equal("Failed", repository.Export!.State);
        Assert.Equal("COLLAB_EXPORT_EXECUTION_EXPIRED", repository.Export.ErrorCode);
        Assert.Equal(1, repository.ClaimSuccessCount);
    }

    [Fact]
    public async Task Worker_does_not_publish_a_non_clean_artifact()
    {
        var repository = WorkerRepository(approvalExpiresOn: DateTimeOffset.UtcNow.AddMinutes(5));
        var files = new FakeFiles { ArtifactScanStatus = "Malicious" };
        var worker = new CollaborationExportWorker(repository, files, new FakeAudit());

        await worker.ProcessOnceAsync(CancellationToken.None);

        Assert.Equal("Failed", repository.Export!.State);
        Assert.True(repository.Export.ErrorCode == "COLLAB_EXPORT_FILE_REJECTED", $"state={repository.Export.State}; error={repository.Export.ErrorCode}; create={files.CreateExportArtifactCalls}; add={files.AddExportReferenceCalls}");
        Assert.Equal(0, files.AddExportReferenceCalls);
    }

    [Fact]
    public async Task Worker_uses_the_frozen_case_revision_when_releasing_one_of_two_legal_holds()
    {
        var scope = new ComplianceScopeDto { ScopeType = "MessageSet", MessageNIds = ExportMessages };
        var repository = new FakeRepository
        {
            Messages = [new MessageRecord("T-1", "CV-1", "M-1", 1, "U-1", "CLIENT-1", "hash", "Text", "body", null, "ATT-1", DateTimeOffset.UtcNow, null, null, null, 1, Guid.NewGuid())],
        };
        repository.Attachments["ATT-1"] = new AttachmentRecord("T-1", "CV-1", "ATT-1", "U-1", "FILE-1", "a.txt", "text/plain", 2, "CollaborationMessageAttachment", "Available", 1, DateTimeOffset.UtcNow, "Bound", "Active", "M-1", "REQ-ATT", "hash", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        repository.LegalHolds.AddRange(
        [
            LegalHold("HLD-A", scope, "a", optimisticVersion: 1),
            LegalHold("HLD-B", scope, "b", optimisticVersion: 1),
        ]);
        var files = new FakeFiles();
        var worker = new CollaborationExportWorker(repository, files, new FakeAudit());

        await worker.ProcessOnceAsync(CancellationToken.None);

        Assert.Equal(2, files.LegalHoldReferences.Count);
        Assert.All(files.LegalHoldReferences.Values, reference => Assert.Equal(1, reference.CaseRevision));
        var holdA = repository.LegalHolds.Single(hold => hold.HoldCaseNId == "HLD-A");
        repository.ReplaceLegalHold(holdA with
        {
            State = "ReleasePendingFileSync",
            FileSyncState = "Pending",
            OptimisticVersion = 4,
            ReleaseRequestNId = "REQ-RELEASE-A",
        });

        await worker.ProcessOnceAsync(CancellationToken.None);

        Assert.Equal("Released", files.LegalHoldReferences["HLD-A:FILE-1"].Status);
        Assert.Equal("Active", files.LegalHoldReferences["HLD-B:FILE-1"].Status);
        Assert.Equal(1, files.LegalHoldReferences["HLD-A:FILE-1"].CaseRevision);
        Assert.Equal("Released", repository.LegalHolds.Single(hold => hold.HoldCaseNId == "HLD-A").State);
        Assert.Equal("ActiveReviewed", repository.LegalHolds.Single(hold => hold.HoldCaseNId == "HLD-B").State);
    }

    [Fact]
    public async Task Worker_does_not_expose_a_succeeded_export_when_completion_audit_fails()
    {
        var repository = WorkerRepository(approvalExpiresOn: DateTimeOffset.UtcNow.AddMinutes(5));
        var files = new FakeFiles();
        var audit = new FailingAudit("compliance.export.complete");

        await new CollaborationExportWorker(repository, files, audit).ProcessOnceAsync(CancellationToken.None);

        Assert.Equal("Failed", repository.Export!.State);
        Assert.Equal("COLLAB_EXPORT_EXECUTION_FAILED", repository.Export.ErrorCode);
        Assert.Contains("compliance.export.complete", audit.Actions);
        Assert.Equal(1, files.AddExportReferenceCalls);
    }

    [Fact]
    public async Task Worker_over_http_receiver_binds_clean_artifact_before_completion_audit_and_allows_download()
    {
        var repository = WorkerRepository(approvalExpiresOn: DateTimeOffset.UtcNow.AddMinutes(5));
        var signer = new ContractRecordingSigner();
        var receiver = new ExportReceiverHandler();
        var files = CreateHttpFilePort(receiver, signer);
        var audit = new CompletionOrderingAudit(repository);

        await new CollaborationExportWorker(repository, files, audit).ProcessOnceAsync(CancellationToken.None);

        Assert.Equal("Succeeded", repository.Export!.State);
        Assert.Equal("FILE-EXPORT|REF-EXP-1", repository.Export.ArtifactReference);
        Assert.Contains("compliance.export.complete", audit.Actions);
        Assert.Equal(["file.export-artifact.create", "file.read", "file.reference.bind"], receiver.Actions);
        Assert.True(receiver.ReferenceBound);

        await using var content = await files.OpenContentAsync("T-1", "U-1", "FILE-EXPORT", "REF-EXP-1", CancellationToken.None);
        using var reader = new StreamReader(content, Encoding.UTF8);
        var downloaded = await reader.ReadToEndAsync();

        Assert.Contains("M-1", downloaded, StringComparison.Ordinal);
        Assert.Equal(1, receiver.DownloadCalls);
        Assert.Equal("file.download", signer.Actions[^1]);
    }

    private static HttpSystemDataFilePort CreateHttpFilePort(HttpMessageHandler handler, ITrustedServiceCallSigner signer)
    {
        var http = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([
                new Claim(ClaimConstants.UserNId, "U-1"),
                new Claim(ClaimConstants.TenantId, "T-1"),
                new Claim(ClaimConstants.SessionId, "SID-1"),
                new Claim(ClaimConstants.AuthVersion, "7"),
            ], "test")),
        };
        return new HttpSystemDataFilePort(
            new StaticHttpClientFactory(new HttpClient(handler)),
            signer,
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Collaboration:SystemData:BaseUrl"] = "http://systemdata.test",
            }).Build(),
            new HttpContextAccessor { HttpContext = http });
    }

    private static LegalHoldRecord LegalHold(string holdCaseNId, ComplianceScopeDto scope, string suffix, long optimisticVersion) =>
        new("T-1", holdCaseNId, "ActiveReviewed", JsonSerializer.Serialize(scope), new string(suffix[0], 64), "review", "U-1", DateTimeOffset.UtcNow, null, optimisticVersion, Guid.NewGuid(), "REQ-" + holdCaseNId, "hash-" + holdCaseNId, FileSyncState: "Pending");

    private static FakeRepository WorkerRepository(
        DateTimeOffset approvalExpiresOn,
        string state = "Queued",
        DateTimeOffset? runDeadlineOn = null,
        DateTimeOffset? workerLeaseUntil = null) => new()
    {
        Export = new ComplianceExportRecord(
            "T-1", "EXP-1", state,
            JsonSerializer.Serialize(new
            {
                scope = new ComplianceScopeDto { ScopeType = "MessageSet", MessageNIds = ExportMessages },
                fields = ExportFields,
                messageVersions = new[] { new { messageNId = "M-1", messageStateVersion = 1 } },
            }),
            new string('e', 64), "incident review", "U-1", DateTimeOffset.UtcNow, null, null, 1, Guid.NewGuid(),
            CaseReference: "CASE-001", ApprovedByUserNId: "U-2", ApprovedOn: DateTimeOffset.UtcNow,
            ApprovalExpiresOn: approvalExpiresOn, RunDeadlineOn: runDeadlineOn,
            ExportRetentionHours: 24, WorkerLeaseNId: "OLD-WORKER", WorkerLeaseUntil: workerLeaseUntil),
        Messages = [new MessageRecord("T-1", "CV-1", "M-1", 1, "U-1", "CLIENT-1", "hash", "Text", "body", null, null, DateTimeOffset.UtcNow, null, null, null, 1, Guid.NewGuid())],
    };

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Worker_requires_an_unexpired_approval_only_for_non_administrator_exports(bool administratorAuthorized)
    {
        var repository = WorkerRepository(approvalExpiresOn: DateTimeOffset.UtcNow.AddMinutes(-1));
        if (administratorAuthorized)
            repository.Export = repository.Export! with { ApprovedByUserNId = repository.Export!.CreatedByUserNId, ApprovalExpiresOn = null };
        var files = new FakeFiles();
        await new CollaborationExportWorker(repository, files, new FakeAudit()).ProcessOnceAsync(CancellationToken.None);
        Assert.Equal(administratorAuthorized ? "Succeeded" : "PendingApproval", repository.Export!.State);
        Assert.Equal(administratorAuthorized ? 1 : 0, files.CreateExportArtifactCalls);
    }

    private static CollaborationService CreateService(
        FakeRepository repository,
        FakeFiles? files = null,
        AllowPermissions? permissions = null) => new(
        repository,
        new FakeDirectory(),
        files ?? new FakeFiles(),
        new FakeAudit(),
        new FakePresence(),
        new FakeStepUp(),
        new FakeBindingIssuer(),
        permissions ?? new AllowPermissions(),
        new PageCursorCodec("test-signing-key", TimeSpan.FromMinutes(5)));

    private sealed class FakeDirectory : ICollaborationIdentityDirectory
    {
        public Task<DirectoryUser?> GetAsync(string tenantNId, string userNId, CancellationToken cancellationToken) => Task.FromResult<DirectoryUser?>(null);
        public Task<DirectorySearchPage> SearchAsync(string tenantNId, string actorUserNId, string keyword, string? cursor, int pageSize, CancellationToken cancellationToken) => Task.FromResult(new DirectorySearchPage([], null));
    }

    private sealed class FakeFiles : ICollaborationFilePort
    {
        public CollaborationFileState? File { get; set; }
        public string ArtifactScanStatus { get; init; } = "Clean";
        public int CreateExportArtifactCalls { get; private set; }
        public int AddExportReferenceCalls { get; private set; }
        public int OpenContentCalls { get; private set; }
        public Dictionary<string, LegalHoldFileReference> LegalHoldReferences { get; } = new(StringComparer.Ordinal);
        private static Task<T> Unsupported<T>() => Task.FromException<T>(new NotSupportedException());
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
        public Task<string?> AddReferenceAsync(string tenantNId, string userNId, string fileNId, string referenceNId, CancellationToken cancellationToken, string? purpose = null) => Task.FromResult<string?>(null);
        public Task<ExportArtifactResult> CreateExportArtifactAsync(string tenantNId, string userNId, string exportNId, string fileName, string contentType, Stream content, CancellationToken cancellationToken)
        {
            CreateExportArtifactCalls++;
            File = new CollaborationFileState("FILE-EXPORT", fileName, contentType, content.Length, ArtifactScanStatus, false, "Available");
            return Task.FromResult(new ExportArtifactResult("FILE-EXPORT", fileName, contentType, content.Length, ArtifactScanStatus));
        }
        public Task<string?> AddExportReferenceAsync(string tenantNId, string userNId, string fileNId, string referenceNId, CancellationToken cancellationToken)
        {
            AddExportReferenceCalls++;
            return Task.FromResult<string?>(referenceNId);
        }
        public Task AddLegalHoldReferenceAsync(string tenantNId, string userNId, string fileNId, string holdCaseNId, string requestNId, string scopeChecksum, long caseRevision, CancellationToken cancellationToken)
        {
            LegalHoldReferences[$"{holdCaseNId}:{fileNId}"] = new(scopeChecksum, caseRevision, "Active");
            return Task.CompletedTask;
        }
        public Task ReleaseLegalHoldReferenceAsync(string tenantNId, string userNId, string fileNId, string holdCaseNId, string requestNId, string scopeChecksum, long caseRevision, CancellationToken cancellationToken)
        {
            var key = $"{holdCaseNId}:{fileNId}";
            if (!LegalHoldReferences.TryGetValue(key, out var reference)
                || reference.Status != "Active"
                || reference.CaseRevision != caseRevision
                || reference.ScopeChecksum != scopeChecksum)
                return Task.FromException(new InvalidOperationException("legal hold case revision mismatch"));
            LegalHoldReferences[key] = reference with { Status = "Released" };
            return Task.CompletedTask;
        }
        public Task<CollaborationFileState?> GetAsync(string tenantNId, string userNId, string fileNId, CancellationToken cancellationToken) => Task.FromResult(File);
        public Task<Stream> OpenContentAsync(string tenantNId, string userNId, string fileNId, string referenceNId, CancellationToken cancellationToken)
        {
            OpenContentCalls++;
            return Task.FromResult<Stream>(new MemoryStream("content"u8.ToArray()));
        }
    }

    private sealed record LegalHoldFileReference(string ScopeChecksum, long CaseRevision, string Status);

    private sealed class StaticHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class ContractRecordingSigner : ITrustedServiceCallSigner
    {
        public List<string> Actions { get; } = [];

        public string Sign(HttpMethod method, string pathAndQuery, ReadOnlyMemory<byte> body, string audience, string tenantNId, string actorUserNId, string actorSessionNId, string actorSecurityVersion, string action, string requestNId)
        {
            Actions.Add(action);
            return $"test-assertion:{action}";
        }
    }

    private sealed class ExportReceiverHandler : HttpMessageHandler
    {
        private const string Prefix = "/internal/pf05/systemdata/files/";
        private byte[] _content = [];

        public List<string> Actions { get; } = [];
        public bool ReferenceBound { get; private set; }
        public int DownloadCalls { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var relative = request.RequestUri?.AbsolutePath.StartsWith(Prefix, StringComparison.Ordinal) == true
                ? request.RequestUri.AbsolutePath[Prefix.Length..]
                : string.Empty;
            var expectedAction = request.Method == HttpMethod.Post && relative.StartsWith("exports/", StringComparison.Ordinal)
                ? "file.export-artifact.create"
                : request.Method == HttpMethod.Get && relative.EndsWith("/content", StringComparison.Ordinal)
                    ? "file.download"
                    : request.Method == HttpMethod.Put && relative.StartsWith("references/", StringComparison.Ordinal)
                        ? "file.reference.bind"
                        : request.Method == HttpMethod.Get && string.Equals(relative, "FILE-EXPORT", StringComparison.Ordinal)
                            ? "file.read"
                            : null;
            if (expectedAction is null)
                return new HttpResponseMessage(HttpStatusCode.NotFound);

            var assertion = request.Headers.TryGetValues(TrustedServiceCallValidator.HeaderName, out var values)
                ? values.SingleOrDefault()
                : null;
            if (!string.Equals(assertion, $"test-assertion:{expectedAction}", StringComparison.Ordinal))
                return new HttpResponseMessage(HttpStatusCode.Unauthorized);

            Actions.Add(expectedAction);
            if (expectedAction == "file.export-artifact.create")
            {
                _content = await request.Content!.ReadAsByteArrayAsync(cancellationToken);
                return Json(new ExportArtifactResult("FILE-EXPORT", "collaboration-export-EXP-1.json", "application/json", _content.LongLength, "Clean"));
            }

            if (expectedAction == "file.reference.bind")
            {
                var binding = await request.Content!.ReadFromJsonAsync<FileBindingRequest>(cancellationToken: cancellationToken);
                if (binding?.Purpose != CollaborationServiceConstants.ComplianceExportPurpose)
                    return new HttpResponseMessage(HttpStatusCode.UnprocessableEntity);
                ReferenceBound = true;
                return Json(new FileBindingV1 { TenantNId = "T-1", ReferenceNId = "REF-EXP-1", FileNId = "FILE-EXPORT", Status = "Active", Version = 0 });
            }

            if (expectedAction == "file.download")
            {
                if (!ReferenceBound || request.RequestUri?.Query != "?referenceNId=REF-EXP-1")
                    return new HttpResponseMessage(HttpStatusCode.Forbidden);
                DownloadCalls++;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(_content),
                };
            }

            return Json(CurrentFile());
        }

        private FileObjectV1 CurrentFile() => new()
        {
            TenantNId = "T-1",
            FileNId = "FILE-EXPORT",
            FileName = "collaboration-export-EXP-1.json",
            ContentType = "application/json",
            Length = _content.LongLength,
            ScanStatus = "Clean",
            Restricted = false,
            Purpose = CollaborationServiceConstants.ComplianceExportPurpose,
            OwnerUserNId = "U-1",
            ReferenceCount = ReferenceBound ? 1 : 0,
            ReferenceSummary = ReferenceBound
                ? [new FileReferenceSummaryV1 { ReferenceNId = "REF-EXP-1", OwnerUserNId = "U-1", Purpose = CollaborationServiceConstants.ComplianceExportPurpose }]
                : [],
            DeletionStatus = "Active",
            CreatedOn = DateTimeOffset.UtcNow,
            LastUpdatedOn = DateTimeOffset.UtcNow,
        };

        private static HttpResponseMessage Json(object data) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(new { success = true, data }), Encoding.UTF8, "application/json"),
        };
    }

    private sealed class CompletionOrderingAudit(FakeRepository repository) : FakeAudit
    {
        public override Task WriteAsync(string tenantNId, TrustedCollaborationCall? serviceCall, string actorUserNId, string action, string objectType, string objectNId, object payload, CancellationToken cancellationToken)
        {
            if (action == "compliance.export.complete")
                Assert.NotEqual("Succeeded", repository.Export?.State);
            return base.WriteAsync(tenantNId, serviceCall, actorUserNId, action, objectType, objectNId, payload, cancellationToken);
        }
    }

    private class FakeAudit : ICollaborationAuditPort
    {
        public List<string> Actions { get; } = [];

        public virtual Task WriteAsync(string tenantNId, TrustedCollaborationCall? serviceCall, string actorUserNId, string action, string objectType, string objectNId, object payload, CancellationToken cancellationToken)
        {
            Actions.Add(action);
            return Task.CompletedTask;
        }
    }

    private sealed class FailingAudit(string actionToFail) : FakeAudit
    {
        public override Task WriteAsync(string tenantNId, TrustedCollaborationCall? serviceCall, string actorUserNId, string action, string objectType, string objectNId, object payload, CancellationToken cancellationToken)
        {
            Actions.Add(action);
            return action == actionToFail
                ? Task.FromException(new InvalidOperationException("audit unavailable"))
                : Task.CompletedTask;
        }
    }

    private sealed class FakePresence : ICollaborationPresence
    {
        public PresenceDto SetPresence(string tenantNId, string userNId, string state, string? connectionNId = null) => new() { UserNId = userNId, State = state };
        public PresenceDto GetPresence(string tenantNId, string userNId) => new() { UserNId = userNId };
        public void RemovePresence(string tenantNId, string userNId, string connectionNId) { }
    }

    private sealed class FakeStepUp : IComplianceStepUpVerifier
    {
        public Task EnsureValidAsync(string proof, string tenantNId, string actorUserNId, string actorSessionNId, string actorSecurityVersion, string action, string requestNId, string scopeChecksum, string requestHash, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FakeBindingIssuer : IStepUpBindingIssuer
    {
        public string Issue(string tenantNId, string actorUserNId, string actorSessionNId, string actorSecurityVersion, string action, string requestNId, string scopeChecksum, string requestHash, DateTimeOffset issuedOn, TimeSpan lifetime) => "binding";
    }

    private sealed class AllowPermissions : ICollaborationPermissionEvaluator
    {
        public bool IsAdministrator { get; set; }
        public Task<bool> IsSystemAdministratorAsync(string tenantNId, string actorUserNId, string actorSessionNId, string actorSecurityVersion, CancellationToken cancellationToken) => Task.FromResult(IsAdministrator);
        public bool Allowed { get; set; } = true;
        public Task<bool> HasPermissionAsync(string permission, string tenantNId, string actorUserNId, string actorSessionNId, string actorSecurityVersion, CancellationToken cancellationToken) => Task.FromResult(Allowed);
    }

    private sealed class FakeRepository : ICollaborationRepository
    {
        public CompliancePreparationRecord? Preparation { get; private set; }
        public ComplianceExportRecord? CreatedExport { get; private set; }
        public ComplianceExportRecord? Export { get; set; }
        public ComplianceExportRecord? UpdatedExport { get; private set; }
        public ComplianceCommandRecord? Command { get; private set; }
        public ConversationRecord? Conversation { get; init; }
        public ConversationMemberRecord? Member { get; init; }
        public IReadOnlyList<MessageRecord> Messages { get; init; } = [];
        public List<LegalHoldRecord> LegalHolds { get; } = [];
        public Dictionary<string, AttachmentRecord> Attachments { get; } = new(StringComparer.Ordinal);
        public int ClaimSuccessCount { get; private set; }
        private readonly object _claimGate = new();

        private static Task<T> Unsupported<T>() => Task.FromException<T>(new NotSupportedException());
        private static Task Unsupported() => Task.FromException(new NotSupportedException());

        public Task<ConversationRecord?> GetConversationAsync(string tenantNId, string conversationNId, CancellationToken cancellationToken) => Task.FromResult(Conversation);
        public Task<ConversationRecord?> FindConversationByPairAsync(string tenantNId, string lowUserNId, string highUserNId, CancellationToken cancellationToken) => Unsupported<ConversationRecord?>();
        public Task<IReadOnlyList<ConversationRecord>> ListConversationsAsync(string tenantNId, string userNId, int page, int pageSize, CancellationToken cancellationToken) => Unsupported<IReadOnlyList<ConversationRecord>>();
        public Task<ConversationRecord> CreateConversationAsync(ConversationRecord conversation, ConversationMemberRecord currentMember, ConversationMemberRecord peerMember, CancellationToken cancellationToken) => Unsupported<ConversationRecord>();
        public Task<ConversationMemberRecord?> GetMemberAsync(string tenantNId, string conversationNId, string userNId, CancellationToken cancellationToken) => Task.FromResult(Member);
        public Task<IReadOnlyList<ConversationMemberRecord>> GetMembersAsync(string tenantNId, string conversationNId, CancellationToken cancellationToken) => Unsupported<IReadOnlyList<ConversationMemberRecord>>();
        public Task<MessageRecord?> FindMessageByClientAsync(string tenantNId, string senderUserNId, string clientMessageNId, CancellationToken cancellationToken) => Unsupported<MessageRecord?>();
        public Task<MessageRecord?> GetMessageAsync(string tenantNId, string conversationNId, string messageNId, CancellationToken cancellationToken) => Unsupported<MessageRecord?>();
        public Task<MessageRecord?> GetMessageByIdAsync(string tenantNId, string messageNId, CancellationToken cancellationToken) => Unsupported<MessageRecord?>();
        public Task<MessageRecord?> GetMessageByAttachmentAsync(string tenantNId, string attachmentNId, CancellationToken cancellationToken) => Unsupported<MessageRecord?>();
        public Task<MessageRecord> AppendMessageAsync(ConversationRecord conversation, MessageRecord message, CancellationToken cancellationToken) => Unsupported<MessageRecord>();
        public Task<IReadOnlyList<MessageRecord>> GetMessagesAsync(string tenantNId, string conversationNId, string mode, long? sequence, int pageSize, CancellationToken cancellationToken) => Task.FromResult(Messages);
        public Task UpdateReadCursorAsync(string tenantNId, string conversationNId, string userNId, long sequence, CancellationToken cancellationToken) => Unsupported();
        public Task HideMemberAsync(string tenantNId, string conversationNId, string userNId, long throughSequence, CancellationToken cancellationToken) => Unsupported();
        public Task RestoreMemberAsync(string tenantNId, string conversationNId, string userNId, CancellationToken cancellationToken) => Unsupported();
        public Task RetractMessageAsync(string tenantNId, string conversationNId, string messageNId, string userNId, string reason, CancellationToken cancellationToken) => Unsupported();
        public Task<AttachmentRecord?> GetAttachmentAsync(string tenantNId, string conversationNId, string attachmentNId, CancellationToken cancellationToken) => Unsupported<AttachmentRecord?>();
        public Task<AttachmentRecord?> GetAttachmentByIdAsync(string tenantNId, string attachmentNId, CancellationToken cancellationToken) => Task.FromResult<AttachmentRecord?>(Attachments.GetValueOrDefault(attachmentNId));
        public Task<AttachmentRecord> CreateAttachmentAsync(AttachmentRecord attachment, CancellationToken cancellationToken) => Unsupported<AttachmentRecord>();
        public Task<AttachmentRecord> UpdateAttachmentAsync(AttachmentRecord attachment, CancellationToken cancellationToken) => Unsupported<AttachmentRecord>();
        public Task<IReadOnlyList<MessageRecord>> SearchComplianceMessagesAsync(string tenantNId, ComplianceScopeDto scope, string? keyword, int page, int pageSize, CancellationToken cancellationToken) => Task.FromResult(Messages);
        public Task<ComplianceDispositionRecord> CreateDispositionAsync(ComplianceDispositionRecord disposition, CancellationToken cancellationToken) => Unsupported<ComplianceDispositionRecord>();
        public Task<IReadOnlyList<ComplianceDispositionRecord>> ListDispositionsAsync(string tenantNId, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ComplianceDispositionRecord>>([]);
        public Task<LegalHoldRecord> CreateLegalHoldAsync(LegalHoldRecord hold, CancellationToken cancellationToken) => Unsupported<LegalHoldRecord>();
        public Task<IReadOnlyList<LegalHoldRecord>> ListLegalHoldsAsync(string tenantNId, string? status, int page, int pageSize, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<LegalHoldRecord>>(LegalHolds.Where(hold => hold.TenantNId == tenantNId && (string.IsNullOrWhiteSpace(status) || hold.State == status)).ToArray());
        public Task<LegalHoldRecord?> GetLegalHoldAsync(string tenantNId, string holdCaseNId, CancellationToken cancellationToken) => Unsupported<LegalHoldRecord?>();
        public Task<LegalHoldRecord> UpdateLegalHoldAsync(LegalHoldRecord hold, CancellationToken cancellationToken)
        {
            ReplaceLegalHold(hold);
            return Task.FromResult(hold);
        }
        public Task<ComplianceExportRecord> CreateExportAsync(ComplianceExportRecord exportRecord, CancellationToken cancellationToken)
        {
            CreatedExport = exportRecord with { ExportNId = "EXP-1" };
            return Task.FromResult(CreatedExport);
        }
        public Task<ComplianceExportRecord?> GetExportAsync(string tenantNId, string exportNId, CancellationToken cancellationToken) => Task.FromResult(Export);
        public Task<ComplianceExportRecord> UpdateExportAsync(ComplianceExportRecord exportRecord, CancellationToken cancellationToken)
        {
            UpdatedExport = exportRecord;
            Export = exportRecord;
            return Task.FromResult(exportRecord);
        }
        public Task<ComplianceExportRecord?> UpdateExportIfOwnedAsync(ComplianceExportRecord exportRecord, string workerLeaseNId, long expectedOptimisticVersion, CancellationToken cancellationToken)
        {
            lock (_claimGate)
            {
                if (Export is not { } current
                    || current.TenantNId != exportRecord.TenantNId
                    || current.ExportNId != exportRecord.ExportNId
                    || current.State is not ("Queued" or "Running")
                    || current.WorkerLeaseNId != workerLeaseNId
                    || current.OptimisticVersion != expectedOptimisticVersion)
                    return Task.FromResult<ComplianceExportRecord?>(null);
                UpdatedExport = exportRecord;
                Export = exportRecord;
                return Task.FromResult<ComplianceExportRecord?>(exportRecord);
            }
        }
        public Task<IReadOnlyList<string>> ListExportTenantsAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<string>>(Export is null ? [] : [Export.TenantNId]);
        public Task<IReadOnlyList<string>> ListLegalHoldTenantsAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<string>>(LegalHolds.Select(hold => hold.TenantNId).Distinct(StringComparer.Ordinal).ToArray());
        public Task<IReadOnlyList<ComplianceExportRecord>> ListExportsAsync(string tenantNId, string? status, int page, int pageSize, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ComplianceExportRecord>>(Export is { TenantNId: var tenant } record && tenant == tenantNId && (string.IsNullOrWhiteSpace(status) || record.State == status) ? [record] : []);
        public Task<ComplianceExportRecord?> TryClaimExportAsync(string tenantNId, string exportNId, string workerLeaseNId, DateTimeOffset leaseUntil, DateTimeOffset now, CancellationToken cancellationToken)
        {
            lock (_claimGate)
            {
                if (Export is not { } export || export.TenantNId != tenantNId || export.ExportNId != exportNId || export.State is not ("Queued" or "Running")
                    || (export.WorkerLeaseNId is not null && export.WorkerLeaseUntil > now))
                    return Task.FromResult<ComplianceExportRecord?>(null);
                Export = export with { WorkerLeaseNId = workerLeaseNId, WorkerLeaseUntil = leaseUntil };
                ClaimSuccessCount++;
                return Task.FromResult<ComplianceExportRecord?>(Export);
            }
        }
        public Task<RetentionPolicyRecord> GetRetentionPolicyAsync(string tenantNId, CancellationToken cancellationToken) => Task.FromResult(new RetentionPolicyRecord(tenantNId, "retention", 365, 365, 365, true, 1, Guid.NewGuid()));
        public Task<RetentionPolicyRecord> UpdateRetentionPolicyAsync(RetentionPolicyRecord policy, CancellationToken cancellationToken) => Task.FromResult(policy);
        public Task<CompliancePreparationRecord?> GetCompliancePreparationAsync(string tenantNId, string actorUserNId, string requestNId, CancellationToken cancellationToken) => Task.FromResult(Preparation);
        public Task<CompliancePreparationRecord> SaveCompliancePreparationAsync(CompliancePreparationRecord preparation, CancellationToken cancellationToken)
        {
            Preparation = preparation;
            return Task.FromResult(preparation);
        }
        public Task<ComplianceCommandRecord?> GetComplianceCommandAsync(string tenantNId, string actorUserNId, string requestNId, CancellationToken cancellationToken) => Task.FromResult(Command);
        public Task<ComplianceCommandRecord> SaveComplianceCommandAsync(ComplianceCommandRecord command, CancellationToken cancellationToken)
        {
            Command = command;
            return Task.FromResult(command);
        }
        public Task<bool> TryClaimComplianceCommandAsync(string tenantNId, string actorUserNId, string requestNId, string requestHash, DateTimeOffset claimedOn, CancellationToken cancellationToken) => Task.FromResult(true);

        public void ReplaceLegalHold(LegalHoldRecord hold)
        {
            var index = LegalHolds.FindIndex(item => item.TenantNId == hold.TenantNId && item.HoldCaseNId == hold.HoldCaseNId);
            if (index >= 0) LegalHolds[index] = hold;
            else LegalHolds.Add(hold);
        }
    }
}
