using System.Security.Cryptography;
using System.Diagnostics;
using IndustrialPlatform.Collaboration.Application;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace IndustrialPlatform.Collaboration.Infrastructure;

/// <summary>
/// Publishes the safe realtime projection after the message transaction commits.
/// The database lease makes the dispatcher safe across UnifiedHost instances;
/// an acknowledged SignalR duplicate is harmless because clients merge by message id.
/// </summary>
public sealed partial class CollaborationOutboxDispatcher : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromSeconds(30);
    private const int MaxRetries = 10;
    private readonly ICollaborationRepository _repository;
    private readonly ICollaborationRealtimePublisher _publisher;
    private readonly ILogger<CollaborationOutboxDispatcher> _logger;

    public CollaborationOutboxDispatcher(
        ICollaborationRepository repository,
        ICollaborationRealtimePublisher publisher,
        ILogger<CollaborationOutboxDispatcher> logger)
    {
        _repository = repository;
        _publisher = publisher;
        _logger = logger;
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
                catch (Exception exception) { LogPollFailed(exception); }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }

    public async Task ProcessOnceAsync(CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var candidate in await _repository.ListPendingOutboxAsync(now, 100, cancellationToken))
        {
            var leaseNId = $"COLLAB-OUTBOX-{Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant()}";
            var claimed = await _repository.TryClaimOutboxAsync(candidate.EventId, leaseNId, now.Add(LeaseDuration), now, cancellationToken);
            if (claimed is null) continue;

            try
            {
                using var activity = CollaborationDiagnostics.ActivitySource.StartActivity("collaboration.outbox.publish");
                activity?.SetTag("messaging.event.id", claimed.EventId);
                activity?.SetTag("messaging.event.type", claimed.EventType);
                var started = Stopwatch.GetTimestamp();
                await _publisher.PublishAsync(claimed, cancellationToken);
                CollaborationDiagnostics.PublishDuration.Record(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
                CollaborationDiagnostics.OutboxPublished.Add(1, new KeyValuePair<string, object?>("event.type", claimed.EventType));
                await _repository.MarkOutboxPublishedAsync(claimed.EventId, leaseNId, DateTimeOffset.UtcNow, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception exception)
            {
                var retryCount = claimed.RetryCount + 1;
                var deadLetter = retryCount >= MaxRetries;
                CollaborationDiagnostics.OutboxRetried.Add(1);
                if (deadLetter) CollaborationDiagnostics.OutboxDeadLettered.Add(1);
                await _repository.MarkOutboxFailedAsync(
                    claimed.EventId,
                    leaseNId,
                    retryCount,
                    exception.Message,
                    deadLetter,
                    DateTimeOffset.UtcNow,
                    cancellationToken);
                LogPublishFailed(exception, claimed.EventId, retryCount, deadLetter);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Collaboration outbox poll failed.")]
    private partial void LogPollFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Collaboration outbox event {EventId} publish failed (attempt {Attempt}, dead={Dead}).")]
    private partial void LogPublishFailed(Exception exception, Guid eventId, int attempt, bool dead);
}
