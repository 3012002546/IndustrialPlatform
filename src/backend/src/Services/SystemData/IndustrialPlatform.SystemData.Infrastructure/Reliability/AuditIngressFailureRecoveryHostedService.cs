using IndustrialPlatform.SystemData.Application.Auditing;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace IndustrialPlatform.SystemData.Infrastructure.Reliability;

/// <summary>Replays independently spooled audit-ingress failures after the primary store recovers.</summary>
public sealed partial class AuditIngressFailureRecoveryHostedService : BackgroundService
{
    private readonly IAuditFailureSink _failureSink;
    private readonly IAuditStore _store;
    private readonly ILogger<AuditIngressFailureRecoveryHostedService> _logger;

    public AuditIngressFailureRecoveryHostedService(IAuditFailureSink failureSink, IAuditStore store, ILogger<AuditIngressFailureRecoveryHostedService> logger)
    {
        _failureSink = failureSink;
        _store = store;
        _logger = logger;
    }

    internal async Task<int> RecoverPendingAsync(CancellationToken cancellationToken)
    {
        var recovered = 0;
        await foreach (var failure in _failureSink.ReadPendingAsync(cancellationToken))
        {
            try
            {
                await _store.RecordIngressFailureAsync(failure.TenantNId, failure.ProducerServiceKey, failure.AuditEventNId, failure.ErrorCode, failure.ErrorSummary, failure.PayloadHash, cancellationToken);
                await _failureSink.AcknowledgeAsync(failure, cancellationToken);
                recovered++;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                LogReplayFailed(_logger, failure.FailureNId, exception);
            }
        }
        return recovered;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try { await RecoverPendingAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
            catch (Exception exception) { LogReplayLoopFailed(_logger, exception); }
        }
    }

    [LoggerMessage(EventId = 404, Level = LogLevel.Warning, Message = "审计失败事实补投失败: {FailureNId}。")]
    private static partial void LogReplayFailed(ILogger logger, string failureNId, Exception exception);

    [LoggerMessage(EventId = 405, Level = LogLevel.Warning, Message = "审计失败事实补投循环失败。")]
    private static partial void LogReplayLoopFailed(ILogger logger, Exception exception);
}
