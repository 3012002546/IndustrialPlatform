using System.Collections.Concurrent;
using IndustrialPlatform.Collaboration.Application;
using IndustrialPlatform.EventBus.Abstractions;
using IndustrialPlatform.EventBus.Events;
using Microsoft.Extensions.Logging;

namespace IndustrialPlatform.Collaboration.Infrastructure.Files;

/// <summary>Idempotently reconciles SystemData file lifecycle events into chat attachments.</summary>
public sealed partial class CollaborationFileStatusConsumer : IIntegrationEventConsumer<FileStatusChangedIntegrationEvent>
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Locks = new(StringComparer.Ordinal);
    private readonly ICollaborationRepository _repository;
    private readonly ICollaborationFilePort _files;
    private readonly ILogger<CollaborationFileStatusConsumer> _logger;

    public CollaborationFileStatusConsumer(ICollaborationRepository repository, ICollaborationFilePort files, ILogger<CollaborationFileStatusConsumer> logger)
    {
        _repository = repository;
        _files = files;
        _logger = logger;
    }

    public async Task HandleAsync(FileStatusChangedIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        var claim = await _repository.TryClaimEventInboxAsync(integrationEvent.EventId, integrationEvent.TenantNId, integrationEvent.EventType, integrationEvent.ObservedOn, cancellationToken);
        if (claim.Status == EventInboxClaimStatus.AlreadyProcessed)
            return;
        if (claim.Status == EventInboxClaimStatus.Busy)
            throw new EventInboxBusyException("文件状态事件仍由其他 worker 处理，等待消息重投。");
        var leaseNId = claim.LeaseNId ?? throw new CollaborationException(500, "COLLAB_EVENT_INBOX_LEASE_MISSING", "文件状态事件处理租约缺失。");

        var key = $"{integrationEvent.TenantNId}:{integrationEvent.FileNId}";
        var gate = Locks.GetOrAdd(key, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var authoritative = await _files.GetForReconciliationAsync(integrationEvent.TenantNId, integrationEvent.FileNId, cancellationToken)
                ?? throw new CollaborationException(503, "COLLAB_SYSTEMDATA_FILE_UNAVAILABLE", "SystemData 未返回权威文件状态。");
            var attachments = await _repository.ListAttachmentsByFileAsync(integrationEvent.TenantNId, integrationEvent.FileNId, cancellationToken);
            foreach (var attachment in attachments)
            {
                await _repository.UpdateAttachmentAsync(attachment with
                {
                    FileStateProjection = ProjectFileState(authoritative),
                    FileStateVersion = null,
                    FileObservedOn = authoritative.ObservedOn,
                    LastUpdatedOn = authoritative.ObservedOn ?? DateTimeOffset.UtcNow,
                }, cancellationToken);
            }
            if (!await _repository.MarkEventInboxProcessedAsync(integrationEvent.EventId, leaseNId, DateTimeOffset.UtcNow, cancellationToken))
                throw new CollaborationException(409, "COLLAB_EVENT_INBOX_LEASE_LOST", "文件状态事件处理租约已失效。");
        }
        catch (Exception exception)
        {
            CollaborationDiagnostics.FileReconciliationFailed.Add(1);
            await _repository.MarkEventInboxFailedAsync(integrationEvent.EventId, exception.Message, leaseNId, DateTimeOffset.UtcNow, cancellationToken);
            LogReconcileFailed(_logger, integrationEvent.FileNId, exception);
            throw;
        }
        finally
        {
            gate.Release();
        }
    }

    [LoggerMessage(EventId = 430, Level = LogLevel.Warning, Message = "协作附件文件状态回补失败: {FileNId}")]
    private static partial void LogReconcileFailed(ILogger logger, string fileNId, Exception exception);

    private static string ProjectFileState(CollaborationFileState file) =>
        file.DeletionStatus == "Deleted"
            ? "Deleted"
            : file.Restricted || file.DeletionStatus is "Requested" or "DeletionRequested"
                ? "Unavailable"
                : file.ScanStatus;
}
