using IndustrialPlatform.Collaboration.Application;
using Microsoft.Extensions.Hosting;

namespace IndustrialPlatform.Collaboration.Infrastructure;

/// <summary>
/// Advances ordinary message retention in bounded, restartable batches. File
/// references are released only after the checkpoint has recorded the pending
/// state; a failed File call therefore leaves the message and reference safe
/// for the next retry.
/// </summary>
public sealed class CollaborationRetentionWorker : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(1);
    private readonly ICollaborationRepository _repository;
    private readonly ICollaborationFilePort _files;

    public CollaborationRetentionWorker(ICollaborationRepository repository, ICollaborationFilePort files)
    {
        _repository = repository;
        _files = files;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(PollInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try { await ProcessOnceAsync(stoppingToken); }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
                catch { /* The checkpoint remains recoverable for the next tick. */ }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }

    public async Task ProcessOnceAsync(CancellationToken cancellationToken)
    {
        var workerNId = $"RET-WORKER-{Environment.ProcessId}";
        foreach (var tenantNId in await _repository.ListRetentionTenantsAsync(cancellationToken))
        {
            var result = await _repository.RunRetentionSweepAsync(tenantNId, workerNId, DateTimeOffset.UtcNow, cancellationToken);
            foreach (var file in result.FilesPendingRelease)
            {
                try
                {
                    await _files.ReleaseReferenceAsync(
                        tenantNId,
                        file.UploaderUserNId,
                        file.FileNId,
                        file.ReferenceNId,
                        expectedVersion: 0,
                        requestNId: $"retention-{file.OperationNId}-{file.AttachmentNId}",
                        cancellationToken);
                    await _repository.MarkRetentionAttachmentReleasedAsync(tenantNId, file.AttachmentNId, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch { /* Keep ReleasePending and retry after the next checkpoint lease. */ }
            }
        }
    }
}
