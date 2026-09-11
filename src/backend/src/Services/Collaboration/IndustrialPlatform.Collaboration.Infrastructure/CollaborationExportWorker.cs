using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IndustrialPlatform.Collaboration.Application;
using IndustrialPlatform.Collaboration.Contracts;
using Microsoft.Extensions.Hosting;

namespace IndustrialPlatform.Collaboration.Infrastructure;

/// <summary>
/// Executes approved compliance exports outside the request thread.  The
/// worker owns only the Collaboration state transition; file storage and
/// scanning remain SystemData responsibilities.
/// </summary>
public sealed class CollaborationExportWorker : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(2);
    private const long FrozenCaseScopeRevision = 1;
    private static readonly JsonSerializerOptions ExportJsonOptions = new()
    {
        WriteIndented = false,
        PropertyNameCaseInsensitive = true,
    };
    private readonly ICollaborationRepository _repository;
    private readonly ICollaborationFilePort _files;
    private readonly ICollaborationAuditPort _audit;

    public CollaborationExportWorker(
        ICollaborationRepository repository,
        ICollaborationFilePort files,
        ICollaborationAuditPort audit)
    {
        _repository = repository;
        _files = files;
        _audit = audit;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(PollInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    await ProcessOnceAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch
                {
                    // A failed poll must not terminate the host.  The next
                    // tick retries the durable export state.
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    public async Task ProcessOnceAsync(CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        await ProcessLegalHoldsOnceAsync(cancellationToken);
        foreach (var tenantNId in await _repository.ListExportTenantsAsync(cancellationToken))
        {
            var exports = await _repository.ListExportsAsync(tenantNId, null, 1, 100, cancellationToken);
            foreach (var exportRecord in exports)
            {
                if (exportRecord.State == "Succeeded")
                {
                    if (exportRecord.ExpiresOn is not null && exportRecord.ExpiresOn <= now)
                        await ExpireAsync(exportRecord, now, cancellationToken);
                    continue;
                }

                if (exportRecord.State == "Queued")
                {
                    var claimed = await TryClaimAsync(exportRecord, now, cancellationToken);
                    if (claimed is not null)
                        await StartQueuedAsync(claimed, now, cancellationToken);
                    continue;
                }

                if (exportRecord.State == "Running")
                {
                    var claimed = await TryClaimAsync(exportRecord, now, cancellationToken);
                    if (claimed is not null)
                        await RunAsync(claimed, now, cancellationToken);
                }
            }
        }
    }

    private Task<ComplianceExportRecord?> TryClaimAsync(ComplianceExportRecord exportRecord, DateTimeOffset now, CancellationToken cancellationToken) =>
        _repository.TryClaimExportAsync(exportRecord.TenantNId, exportRecord.ExportNId, $"WRK-{Convert.ToHexString(RandomNumberGenerator.GetBytes(12)).ToLowerInvariant()}", now.Add(LeaseDuration), now, cancellationToken);

    private async Task ProcessLegalHoldsOnceAsync(CancellationToken cancellationToken)
    {
        foreach (var tenantNId in await _repository.ListLegalHoldTenantsAsync(cancellationToken))
        {
            var holds = await _repository.ListLegalHoldsAsync(tenantNId, null, 1, 10000, cancellationToken);
            foreach (var hold in holds)
            {
                if (hold.FileSyncState == "Synchronized")
                    continue;

                try
                {
                    var scope = JsonSerializer.Deserialize<ComplianceScopeDto>(hold.ScopeJson)
                        ?? throw new InvalidOperationException("Legal hold scope is invalid.");
                    var messages = await _repository.SearchComplianceMessagesAsync(tenantNId, scope, null, 1, 10000, cancellationToken);
                    var files = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var message in messages)
                    {
                        if (message.AttachmentNId is null)
                            continue;
                        var attachment = await _repository.GetAttachmentByIdAsync(tenantNId, message.AttachmentNId, cancellationToken);
                        if (attachment?.FileNId is { Length: > 0 } fileNId)
                            files.Add(fileNId);
                    }

                    foreach (var fileNId in files)
                    {
                        if (hold.State == "ReleasePendingFileSync")
                            await _files.ReleaseLegalHoldReferenceAsync(tenantNId, hold.CreatedByUserNId, fileNId, hold.HoldCaseNId, hold.ReleaseRequestNId ?? hold.RequestNId ?? $"release-{hold.HoldCaseNId}", hold.ScopeChecksum, FrozenCaseScopeRevision, cancellationToken);
                        else
                            await _files.AddLegalHoldReferenceAsync(tenantNId, hold.CreatedByUserNId, fileNId, hold.HoldCaseNId, hold.RequestNId ?? $"hold-{hold.HoldCaseNId}", hold.ScopeChecksum, FrozenCaseScopeRevision, cancellationToken);
                    }

                    if (!string.Equals(hold.FileSyncState, "Synchronized", StringComparison.Ordinal))
                    {
                        var synchronizedHold = hold with
                        {
                            State = hold.State == "ReleasePendingFileSync" ? "Released" : hold.State,
                            ReleasedOn = hold.State == "ReleasePendingFileSync" ? DateTimeOffset.UtcNow : hold.ReleasedOn,
                            FileSyncState = "Synchronized",
                            OptimisticVersion = hold.OptimisticVersion + 1,
                            ConcurrencyVersion = Guid.NewGuid(),
                        };
                        await _repository.UpdateLegalHoldAsync(synchronizedHold, cancellationToken);
                        await _audit.WriteAsync(tenantNId, null, hold.CreatedByUserNId, "compliance.legal-hold.file-sync", "legal-hold", hold.HoldCaseNId, new { hold.ScopeChecksum, state = hold.State }, cancellationToken);
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch
                {
                    if (!string.Equals(hold.FileSyncState, "Failed", StringComparison.Ordinal))
                    {
                        var failedHold = hold with
                        {
                            FileSyncState = "Failed",
                            OptimisticVersion = hold.OptimisticVersion + 1,
                            ConcurrencyVersion = Guid.NewGuid(),
                        };
                        await _repository.UpdateLegalHoldAsync(failedHold, cancellationToken);
                    }
                }
            }
        }
    }

    private async Task StartQueuedAsync(ComplianceExportRecord exportRecord, DateTimeOffset now, CancellationToken cancellationToken)
    {
        // Self-authorization is written only by the administrator export path;
        // ordinary exports still require another approver and an unexpired approval.
        var administratorAuthorized = !string.IsNullOrWhiteSpace(exportRecord.ApprovedByUserNId)
            && string.Equals(exportRecord.ApprovedByUserNId, exportRecord.CreatedByUserNId, StringComparison.Ordinal);
        if (!administratorAuthorized && (exportRecord.ApprovalExpiresOn is null || exportRecord.ApprovalExpiresOn <= now || string.IsNullOrWhiteSpace(exportRecord.ApprovedByUserNId)))
        {
            var returned = exportRecord with
            {
                State = "PendingApproval",
                ApprovedByUserNId = null,
                ApprovedOn = null,
                ApprovalExpiresOn = null,
                ApprovalConsumedOn = null,
                RunDeadlineOn = null,
                ErrorCode = "COLLAB_EXPORT_APPROVAL_EXPIRED",
                WorkerLeaseNId = null,
                WorkerLeaseUntil = null,
                OptimisticVersion = exportRecord.OptimisticVersion + 1,
                ConcurrencyVersion = Guid.NewGuid(),
            };
            var expiredSave = await SaveWorkerStateAsync(returned, cancellationToken, exportRecord.WorkerLeaseNId);
            if (expiredSave is not null)
                await AuditAsync(expiredSave, "compliance.export.approval-expired", cancellationToken);
            return;
        }

        var running = exportRecord with
        {
            State = "Running",
            ApprovalConsumedOn = now,
            RunDeadlineOn = now.AddMinutes(30),
            ErrorCode = null,
            OptimisticVersion = exportRecord.OptimisticVersion + 1,
            ConcurrencyVersion = Guid.NewGuid(),
        };
        var saved = await SaveWorkerStateAsync(running, cancellationToken, exportRecord.WorkerLeaseNId);
        if (saved is not null)
        {
            await AuditAsync(saved, "compliance.export.start", cancellationToken);
            await RunAsync(saved, now, cancellationToken);
        }
    }

    private async Task RunAsync(ComplianceExportRecord exportRecord, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (exportRecord.RunDeadlineOn is null || exportRecord.RunDeadlineOn <= now)
        {
            await FailAsync(exportRecord, "COLLAB_EXPORT_EXECUTION_EXPIRED", now, cancellationToken);
            return;
        }

        try
        {
            var snapshot = ParseSnapshot(exportRecord);
            if (snapshot.Scope is null || snapshot.MessageVersions.Count == 0)
            {
                await FailAsync(exportRecord, "COLLAB_EXPORT_SNAPSHOT_INVALID", now, cancellationToken);
                return;
            }

            var current = await _repository.SearchComplianceMessagesAsync(exportRecord.TenantNId, snapshot.Scope, null, 1, 10000, cancellationToken);
            var byMessage = current.ToDictionary(item => item.MessageNId, StringComparer.Ordinal);
            var frozen = new List<MessageRecord>(snapshot.MessageVersions.Count);
            foreach (var version in snapshot.MessageVersions)
            {
                if (!byMessage.TryGetValue(version.MessageNId, out var message)
                    || message.MessageStateVersion != version.MessageStateVersion)
                {
                    await FailAsync(exportRecord, "COLLAB_EXPORT_APPROVAL_STALE", now, cancellationToken);
                    return;
                }

                frozen.Add(message);
            }

            var artifactFileNId = ExtractFileNId(exportRecord.ArtifactReference);
            if (artifactFileNId is null)
            {
                var json = BuildExportJson(frozen, snapshot.Fields);
                await using var content = new MemoryStream(Encoding.UTF8.GetBytes(json), writable: false);
                var artifact = await _files.CreateExportArtifactAsync(
                    exportRecord.TenantNId,
                    exportRecord.CreatedByUserNId,
                    exportRecord.ExportNId,
                    $"collaboration-export-{exportRecord.ExportNId}.json",
                    "application/json",
                    content,
                    cancellationToken);
                artifactFileNId = artifact.FileNId;
                exportRecord = exportRecord with
                {
                    ArtifactReference = artifactFileNId,
                    OptimisticVersion = exportRecord.OptimisticVersion + 1,
                    ConcurrencyVersion = Guid.NewGuid(),
                };
                var artifactSave = await SaveWorkerStateAsync(exportRecord, cancellationToken, exportRecord.WorkerLeaseNId);
                if (artifactSave is null)
                    return;
                exportRecord = artifactSave;
            }

            var file = await _files.GetAsync(exportRecord.TenantNId, exportRecord.CreatedByUserNId, artifactFileNId, cancellationToken);
            if (file is null)
            {
                await FailAsync(exportRecord, "COLLAB_EXPORT_FILE_UNAVAILABLE", now, cancellationToken);
                return;
            }

            if (file.ScanStatus is "Pending" or "PendingScan" or "Scanning" or "Unknown")
                return;
            if (file.ScanStatus is "Malicious" or "Error")
            {
                await FailAsync(exportRecord, "COLLAB_EXPORT_FILE_REJECTED", now, cancellationToken);
                return;
            }
            if (!string.Equals(file.ScanStatus, "Clean", StringComparison.Ordinal))
            {
                await FailAsync(exportRecord, "COLLAB_EXPORT_FILE_UNAVAILABLE", now, cancellationToken);
                return;
            }

            var referenceNId = $"REF-{exportRecord.ExportNId}";
            await _files.BindExportReferenceAsync(
                exportRecord.TenantNId,
                exportRecord.CreatedByUserNId,
                artifactFileNId,
                referenceNId,
                snapshot.Scope.ConversationNId ?? exportRecord.ExportNId,
                exportRecord.ExportNId,
                cancellationToken);
            var retentionHours = exportRecord.ExportRetentionHours is < 1 or > 168 ? 24 : exportRecord.ExportRetentionHours;
            var completed = exportRecord with
            {
                State = "Succeeded",
                ArtifactReference = $"{artifactFileNId}|{referenceNId}",
                CompletedOn = now,
                ExpiresOn = now.AddHours(retentionHours),
                WorkerLeaseNId = null,
                WorkerLeaseUntil = null,
                OptimisticVersion = exportRecord.OptimisticVersion + 1,
                ConcurrencyVersion = Guid.NewGuid(),
            };
            await _audit.WriteAsync(completed.TenantNId, null, completed.CreatedByUserNId, "compliance.export.complete", "export", completed.ExportNId, new { completed.ScopeChecksum }, cancellationToken);
            var saved = await SaveWorkerStateAsync(completed, cancellationToken, exportRecord.WorkerLeaseNId);
            if (saved is null)
                return;
        }
        catch (CollaborationException exception) when (exception.Code is "COLLAB_EXPORT_APPROVAL_STALE" or "COLLAB_EXPORT_FILE_REJECTED")
        {
            await FailAsync(exportRecord, exception.Code, now, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            await FailAsync(exportRecord, "COLLAB_EXPORT_EXECUTION_FAILED", now, cancellationToken);
        }
    }

    private async Task FailAsync(ComplianceExportRecord exportRecord, string errorCode, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (exportRecord.State == "Failed")
            return;
        var failed = exportRecord with
        {
            State = "Failed",
            CompletedOn = now,
            ErrorCode = errorCode,
            WorkerLeaseNId = null,
            WorkerLeaseUntil = null,
            OptimisticVersion = exportRecord.OptimisticVersion + 1,
            ConcurrencyVersion = Guid.NewGuid(),
        };
        var saved = await SaveWorkerStateAsync(failed, cancellationToken, exportRecord.WorkerLeaseNId);
        if (saved is not null)
            await AuditAsync(saved, "compliance.export.failed", cancellationToken);
    }

    private async Task ExpireAsync(ComplianceExportRecord exportRecord, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var parts = exportRecord.ArtifactReference?.Split('|', 2, StringSplitOptions.TrimEntries) ?? [];
        if (parts.Length == 2)
            await _files.ReleaseExportReferenceAsync(exportRecord.TenantNId, exportRecord.CreatedByUserNId, parts[0], parts[1], cancellationToken);

        var expired = exportRecord with
        {
            State = "Expired",
            ErrorCode = null,
            WorkerLeaseNId = null,
            WorkerLeaseUntil = null,
            OptimisticVersion = exportRecord.OptimisticVersion + 1,
            ConcurrencyVersion = Guid.NewGuid(),
        };
        await _repository.UpdateExportAsync(expired, cancellationToken);
        await AuditAsync(expired, "compliance.export.expired", cancellationToken);
    }

    private Task AuditAsync(ComplianceExportRecord exportRecord, string action, CancellationToken cancellationToken) =>
        _audit.WriteAsync(exportRecord.TenantNId, null, exportRecord.CreatedByUserNId, action, "export", exportRecord.ExportNId, new { exportRecord.State, exportRecord.ErrorCode, exportRecord.ScopeChecksum }, cancellationToken);

    private async Task<ComplianceExportRecord?> SaveWorkerStateAsync(ComplianceExportRecord exportRecord, CancellationToken cancellationToken, string? expectedWorkerLeaseNId = null)
    {
        var workerLeaseNId = expectedWorkerLeaseNId ?? exportRecord.WorkerLeaseNId;
        if (string.IsNullOrWhiteSpace(workerLeaseNId))
            return await _repository.UpdateExportAsync(exportRecord, cancellationToken);

        return await _repository.UpdateExportIfOwnedAsync(
            exportRecord,
            workerLeaseNId,
            Math.Max(0, exportRecord.OptimisticVersion - 1),
            cancellationToken);
    }

    private static ExportSnapshot ParseSnapshot(ComplianceExportRecord exportRecord)
    {
        using var document = JsonDocument.Parse(exportRecord.ScopeJson);
        var root = document.RootElement;
        var scope = root.TryGetProperty("scope", out var scopeElement)
            ? scopeElement.Deserialize<ComplianceScopeDto>(ExportJsonOptions)
            : JsonSerializer.Deserialize<ComplianceScopeDto>(exportRecord.ScopeJson, ExportJsonOptions);
        var fields = root.TryGetProperty("fields", out var fieldsElement)
            ? fieldsElement.Deserialize<string[]>(ExportJsonOptions) ?? []
            : exportRecord.FieldsJson is null ? [] : JsonSerializer.Deserialize<string[]>(exportRecord.FieldsJson, ExportJsonOptions) ?? [];
        var versions = root.TryGetProperty("messageVersions", out var versionsElement)
            ? versionsElement.Deserialize<ExportMessageVersion[]>(ExportJsonOptions) ?? []
            : [];
        var retention = root.TryGetProperty("exportRetentionHours", out var retentionElement) && retentionElement.TryGetInt32(out var value)
            ? value
            : exportRecord.ExportRetentionHours;
        return new ExportSnapshot(scope, fields, versions, retention);
    }

    private static string BuildExportJson(IReadOnlyList<MessageRecord> messages, IReadOnlyList<string> fields)
    {
        var rows = messages.OrderBy(item => item.Sequence).Select(message =>
        {
            var row = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (var field in fields)
            {
                row[field] = field switch
                {
                    "messageNId" => message.MessageNId,
                    "sequence" => message.Sequence,
                    "senderUserNId" => message.SenderUserNId,
                    "acceptedOn" => message.AcceptedOn,
                    "messageType" => message.MessageType,
                    "textContent" => message.RetractedOn is null ? message.TextContent : null,
                    "attachmentMetadata" => message.RetractedOn is null && message.AttachmentNId is not null
                        ? new { attachmentNId = message.AttachmentNId }
                        : null,
                    _ => null,
                };
            }
            return row;
        });
        return JsonSerializer.Serialize(rows, ExportJsonOptions);
    }

    private static string? ExtractFileNId(string? artifactReference)
    {
        if (string.IsNullOrWhiteSpace(artifactReference))
            return null;
        var value = artifactReference.Split('|', 2, StringSplitOptions.TrimEntries)[0];
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private sealed record ExportSnapshot(
        ComplianceScopeDto? Scope,
        IReadOnlyList<string> Fields,
        IReadOnlyList<ExportMessageVersion> MessageVersions,
        int RetentionHours);

    private sealed record ExportMessageVersion(string MessageNId, int MessageStateVersion);
}
