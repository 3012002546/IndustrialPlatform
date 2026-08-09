using System.Diagnostics;
using Serilog.Core;
using Serilog.Events;

namespace IndustrialPlatform.Logging.Enrichers;

/// <summary>
/// 从当前 Activity 提取 TraceId 写入日志,保证跨服务链路日志可串联。
/// </summary>
public sealed class TraceIdEnricher : ILogEventEnricher
{
    /// <inheritdoc/>
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        ArgumentNullException.ThrowIfNull(logEvent);
        ArgumentNullException.ThrowIfNull(propertyFactory);

        var traceId = Activity.Current?.TraceId.ToString();
        if (!string.IsNullOrWhiteSpace(traceId))
        {
            logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("TraceId", traceId));
        }
    }
}
