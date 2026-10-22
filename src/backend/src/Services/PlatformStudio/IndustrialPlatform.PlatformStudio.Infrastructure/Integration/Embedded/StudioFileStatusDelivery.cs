using System.Text.Json;
using IndustrialPlatform.SystemData.Application.Auditing;
using IndustrialPlatform.SystemData.Application.Files;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace IndustrialPlatform.PlatformStudio.Infrastructure.Integration.Embedded;

/// <summary>本地持久文件状态事件消费端口。消费者按EventId幂等，全部成功后才确认Outbox。</summary>
public interface IStudioFileStatusConsumer
{
    Task ConsumeAsync(FileStatusOutboxRecord item, CancellationToken cancellationToken);
}

/// <summary>扫描/隔离/删除事实可靠进入本地审计；业务查询以同库文件状态为准。</summary>
public sealed class StudioFileStatusAuditConsumer(IAuditService audit) : IStudioFileStatusConsumer
{
    public Task ConsumeAsync(FileStatusOutboxRecord item, CancellationToken cancellationToken) => audit.IngestAsync(item.TenantNId, new()
    {
        ProducerServiceKey = "platform-studio", AuditEventNId = "file-status-" + item.EventId.ToString("N"), OccurredOn = item.ObservedOn,
        ActorUserNId = "file-lifecycle", Action = "file.status.observed", ObjectType = "File", ObjectNId = item.FileNId,
        PayloadJson = JsonSerializer.Serialize(new { item.ScanStatus, item.Restricted, item.DeletionStatus }), Severity = "Info",
    }, cancellationToken);
}

public sealed partial class StudioFileStatusDispatcher(IServiceScopeFactory scopes, TimeProvider clock, ILogger<StudioFileStatusDispatcher> logger) : BackgroundService
{
    public async Task DispatchOnceAsync(CancellationToken cancellationToken)
    {
        using var scope = scopes.CreateScope();
        var outbox = scope.ServiceProvider.GetRequiredService<IFileStatusOutbox>();
        var consumers = scope.ServiceProvider.GetServices<IStudioFileStatusConsumer>().ToArray();
        if (consumers.Length == 0) throw new InvalidOperationException("Embedded文件状态消费者未注册。");
        foreach (var item in await outbox.GetPendingAsync(clock.GetUtcNow(), 100, cancellationToken))
        {
            try
            {
                foreach (var consumer in consumers) await consumer.ConsumeAsync(item, cancellationToken);
                await outbox.MarkPublishedAsync(item.EventId, clock.GetUtcNow(), cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception exception)
            {
                await outbox.RecordFailureAsync(item.EventId, item.RetryCount + 1, "STUDIO_FILE_STATUS_DELIVERY_FAILED", item.RetryCount >= 19, clock.GetUtcNow().AddMinutes(1), cancellationToken);
                LogFailure(logger, exception);
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
            catch (Exception exception) { LogFailure(logger, exception); }
            try { if (!await timer.WaitForNextTickAsync(stoppingToken)) break; }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }
    [LoggerMessage(EventId = 803, Level = LogLevel.Warning, Message = "Embedded file status delivery failed")]
    private static partial void LogFailure(ILogger logger, Exception exception);
}
