using System.Text.Json;
using IndustrialPlatform.PlatformStudio.Application;
using IndustrialPlatform.PlatformStudio.Domain;
using IndustrialPlatform.SystemData.Application.Auditing;
using IndustrialPlatform.SystemData.Contracts.Auditing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace IndustrialPlatform.PlatformStudio.Infrastructure.Integration.Embedded;

/// <summary>Platform与Embedded共用公开Audit端口；唯一事实位置由装配决定。</summary>
public sealed class StudioAuditDelivery(IAuditService audit) : IStudioAudit
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public Task IngestAsync(StudioAuditOutbox item, CancellationToken cancellationToken)
    {
        var request = JsonSerializer.Deserialize<AuditFactIngestRequest>(item.PayloadJson, Json) ?? throw new StudioException("STUDIO_AUDIT_INVALID", "审计载荷无效。", 422);
        if (request.ProducerServiceKey != "platform-studio" || request.AuditEventNId != item.AuditEventNId || string.IsNullOrWhiteSpace(request.ActorUserNId))
            throw new StudioException("STUDIO_AUDIT_INVALID", "审计生产者或事件不匹配。", 422);
        return audit.IngestAsync(item.TenantNId, request, cancellationToken);
    }
}

/// <summary>持久Outbox重试；接收成功后才Sent，重启后继续投递，相同eventId保持幂等。</summary>
public sealed partial class StudioAuditDispatcher(IServiceScopeFactory scopes, TimeProvider clock, ILogger<StudioAuditDispatcher> logger) : BackgroundService
{
    public async Task DispatchOnceAsync(CancellationToken cancellationToken)
    {
        using var scope = scopes.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IStudioResourceStore>();
        var audit = scope.ServiceProvider.GetRequiredService<IStudioAudit>();
        foreach (var item in await store.PendingAuditsAsync(100, cancellationToken))
        {
            try { await audit.IngestAsync(item, cancellationToken); await store.MarkAuditSentAsync(item.Id, clock.GetUtcNow(), cancellationToken); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception exception)
            {
                var code = exception is IndustrialPlatform.SystemData.Application.Operations.SystemDataOperationException operation ? operation.Code : "STUDIO_AUDIT_DELIVERY_FAILED";
                await store.MarkAuditFailedAsync(item.Id, item.AttemptCount + 1, clock.GetUtcNow().AddSeconds(Math.Min(300, Math.Pow(2, Math.Min(item.AttemptCount + 1, 8)))), code, cancellationToken);
                LogDeliveryFailure(logger, item.AuditEventNId, code);
            }
        }
    }
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await DispatchOnceAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception) { LogOutboxFailure(logger, exception); }
            try { if (!await timer.WaitForNextTickAsync(stoppingToken)) break; }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }
    [LoggerMessage(EventId = 801, Level = LogLevel.Warning, Message = "Studio audit {EventNId} delivery failed: {Code}")]
    private static partial void LogDeliveryFailure(ILogger logger, string eventNId, string code);
    [LoggerMessage(EventId = 802, Level = LogLevel.Error, Message = "Studio audit outbox unavailable")]
    private static partial void LogOutboxFailure(ILogger logger, Exception exception);
}
