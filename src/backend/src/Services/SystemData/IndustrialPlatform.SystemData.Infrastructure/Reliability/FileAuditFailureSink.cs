using System.Text.Json;
using IndustrialPlatform.SystemData.Application.Auditing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace IndustrialPlatform.SystemData.Infrastructure.Reliability;

public sealed class AuditFailureSpoolOptions
{
    public const string SectionName = "SystemData:AuditFailureSpool";
    public string RootPath { get; set; } = "App_Data/SystemData/AuditIngressFailures";
}

/// <summary>
/// Independent append-only failure path. It deliberately does not use the SystemData
/// DbContext, so a failed primary database transaction can still leave durable recovery input.
/// </summary>
public sealed partial class FileAuditFailureSink : IAuditFailureSink
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string _rootPath;
    private readonly string _quarantinePath;
    private readonly ILogger<FileAuditFailureSink> _logger;

    public FileAuditFailureSink(IOptions<AuditFailureSpoolOptions> options, ILogger<FileAuditFailureSink>? logger = null)
    {
        _rootPath = Path.GetFullPath(options.Value.RootPath);
        _quarantinePath = Path.Combine(_rootPath, ".quarantine");
        _logger = logger ?? NullLogger<FileAuditFailureSink>.Instance;
    }

    public async Task EnqueueAsync(AuditIngressFailureRecord failure, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_rootPath);
        var path = Path.Combine(_rootPath, $"{failure.FailureNId}.json");
        var temporaryPath = $"{path}.tmp-{Guid.NewGuid():N}";
        try
        {
            await using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, useAsync: true))
            {
                await JsonSerializer.SerializeAsync(stream, failure, JsonOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
            }

            // Publish the final name only after the complete JSON document is durable.
            File.Move(temporaryPath, path, overwrite: false);
        }
        catch (IOException) when (File.Exists(path))
        {
            // The failure id is the idempotency key for the independent spool.
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    public async IAsyncEnumerable<AuditIngressFailureRecord> ReadPendingAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (!Directory.Exists(_rootPath)) yield break;
        foreach (var temporaryPath in Directory.EnumerateFiles(_rootPath, "*.json.tmp-*", SearchOption.TopDirectoryOnly).OrderBy(value => value, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            Quarantine(temporaryPath, "orphaned temporary audit failure record", 407);
        }

        foreach (var path in Directory.EnumerateFiles(_rootPath, "*.json", SearchOption.TopDirectoryOnly).OrderBy(value => value, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            AuditIngressFailureRecord? failure = null;
            try
            {
                await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 4096, useAsync: true);
                failure = await JsonSerializer.DeserializeAsync<AuditIngressFailureRecord>(stream, JsonOptions, cancellationToken);
            }
            catch (JsonException exception)
            {
                Quarantine(path, "invalid JSON", 406, exception);
            }
            catch (IOException exception)
            {
                Quarantine(path, "unreadable record", 406, exception);
            }
            if (failure is not null) yield return failure;
        }
    }

    public Task AcknowledgeAsync(AuditIngressFailureRecord failure, CancellationToken cancellationToken)
    {
        var path = Path.Combine(_rootPath, $"{failure.FailureNId}.json");
        if (File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }

    private void Quarantine(string path, string reason, int eventId, Exception? exception = null)
    {
        try
        {
            Directory.CreateDirectory(_quarantinePath);
            var quarantinePath = Path.Combine(
                _quarantinePath,
                $"{Path.GetFileName(path)}.{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}.{Guid.NewGuid():N}.corrupt");
            File.Move(path, quarantinePath, overwrite: false);
            LogRecordQuarantined(_logger, path, reason, exception);
        }
        catch (Exception quarantineException) when (quarantineException is IOException or UnauthorizedAccessException)
        {
            LogQuarantineFailed(_logger, path, reason, quarantineException);
        }
    }

    [LoggerMessage(EventId = 406, Level = LogLevel.Warning, Message = "审计失败落盘记录已隔离: {Path}; 原因: {Reason}。")]
    private static partial void LogRecordQuarantined(ILogger logger, string path, string reason, Exception? exception);

    [LoggerMessage(EventId = 408, Level = LogLevel.Error, Message = "审计失败落盘记录隔离失败: {Path}; 原因: {Reason}。")]
    private static partial void LogQuarantineFailed(ILogger logger, string path, string reason, Exception exception);
}
