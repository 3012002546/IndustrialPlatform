using System.Text;
using IndustrialPlatform.EventBus.Abstractions;
using IndustrialPlatform.EventBus.Events;
using IndustrialPlatform.SystemData.Application.Files;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace IndustrialPlatform.SystemData.Infrastructure.Files;

/// <summary>文件扫描与删除生命周期驱动。下载只允许 Clean 文件，扫描失败会保留为 Error 并重试。</summary>
public sealed partial class FileLifecycleHostedService : BackgroundService
{
    private readonly IFileStore _store;
    private readonly IFileContentStore _contentStore;
    private readonly IFileScanner _scanner;
    private readonly TimeProvider _clock;
    private readonly ILogger<FileLifecycleHostedService> _logger;

    public FileLifecycleHostedService(IFileStore store, IFileContentStore contentStore, IFileScanner scanner, TimeProvider clock, ILogger<FileLifecycleHostedService> logger)
    {
        _store = store;
        _contentStore = contentStore;
        _scanner = scanner;
        _clock = clock;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await ExpireSessionsAsync(stoppingToken);
                await ScanPendingAsync(stoppingToken);
                await DeleteRequestedAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
            catch (Exception exception) { LogLifecycleFailed(_logger, exception); }
        }
    }

    private async Task ExpireSessionsAsync(CancellationToken cancellationToken)
    {
        foreach (var session in await _store.ExpireSessionsAsync(_clock.GetUtcNow(), cancellationToken))
            await _contentStore.DeleteAsync($"{session.TenantNId}/{session.SessionNId}.bin", cancellationToken);
    }

    private async Task ScanPendingAsync(CancellationToken cancellationToken)
    {
        foreach (var file in await _store.ListPendingScanAsync(20, cancellationToken))
        {
            try
            {
                var scanning = file with { ScanStatus = "Scanning", LastUpdatedOn = _clock.GetUtcNow() };
                await _store.UpdateFileAsync(scanning, cancellationToken);
                await using var content = await _contentStore.OpenReadAsync(file.StorageKey, cancellationToken);
                var result = await ScanWithTimeoutAsync(file, content, cancellationToken);
                await _store.AddScanAttemptAsync(file.TenantNId, file.FileNId, result.Status, result.Detail, cancellationToken);
                var updated = file with { ScanStatus = result.Status, LastUpdatedOn = _clock.GetUtcNow() };
                await _store.UpdateFileAsync(updated, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception exception)
            {
                await _store.AddScanAttemptAsync(file.TenantNId, file.FileNId, "Error", exception.Message, cancellationToken);
                var updated = file with { ScanStatus = "Error", LastUpdatedOn = _clock.GetUtcNow() };
                await _store.UpdateFileAsync(updated, cancellationToken);
            }
        }
    }

    private async Task<FileScanResult> ScanWithTimeoutAsync(FileObjectRecord file, Stream content, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            var result = await _scanner.ScanAsync(file, content, timeout.Token);
            return result.Status is "Clean" or "Malicious" or "Error" or "Unknown"
                ? result
                : new FileScanResult("Unknown", "扫描器返回了不受信任的状态。");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new FileScanResult("Unknown", "安全扫描超时，文件保持禁止下载状态。");
        }
    }

    private async Task DeleteRequestedAsync(CancellationToken cancellationToken)
    {
        var now = _clock.GetUtcNow();
        foreach (var file in await _store.ListDeletionCandidatesAsync(now, 20, cancellationToken))
        {
            if (await _store.HasActiveLegalHoldsAsync(file.TenantNId, file.FileNId, cancellationToken)
                || await _store.HasActiveReferencesAsync(file.TenantNId, file.FileNId, cancellationToken)) continue;
            await _contentStore.DeleteAsync(file.StorageKey, cancellationToken);
            await _store.MarkFileDeletedAsync(file, now, cancellationToken);
        }
    }


    [LoggerMessage(EventId = 401, Level = LogLevel.Warning, Message = "文件生命周期处理失败。")]
    private static partial void LogLifecycleFailed(ILogger logger, Exception exception);

}

public sealed class LocalSignatureFileScanner : IFileScanner
{
    private static readonly byte[] MaliciousMarker = Encoding.ASCII.GetBytes("EICAR-STANDARD-ANTIVIRUS-TEST-FILE");

    public async Task<FileScanResult> ScanAsync(FileObjectRecord file, Stream content, CancellationToken cancellationToken)
    {
        var overlap = Array.Empty<byte>();
        var buffer = new byte[64 * 1024];
        int read;
        while ((read = await content.ReadAsync(buffer, cancellationToken)) > 0)
        {
            var combined = new byte[overlap.Length + read];
            overlap.CopyTo(combined, 0);
            Buffer.BlockCopy(buffer, 0, combined, overlap.Length, read);
            if (Contains(combined, MaliciousMarker)) return new FileScanResult("Malicious", "EICAR test signature detected");
            overlap = combined.Length <= MaliciousMarker.Length ? combined : combined[^MaliciousMarker.Length..];
        }
        // A signature-only fallback cannot establish that arbitrary content is
        // clean. Keep the object blocked until a trusted scanner says Clean.
        return new FileScanResult("Unknown", "未配置可信安全扫描器，文件保持禁止下载状态。");
    }

    private static bool Contains(byte[] source, byte[] marker)
    {
        for (var i = 0; i <= source.Length - marker.Length; i++)
        {
            if (source.AsSpan(i, marker.Length).SequenceEqual(marker)) return true;
        }
        return false;
    }
}
