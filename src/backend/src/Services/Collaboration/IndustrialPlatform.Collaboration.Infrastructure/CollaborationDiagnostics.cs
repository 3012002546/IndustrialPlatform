using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace IndustrialPlatform.Collaboration.Infrastructure;

/// <summary>OpenTelemetry-compatible signals for outbox, realtime and file reconciliation.</summary>
public static class CollaborationDiagnostics
{
    public static readonly ActivitySource ActivitySource = new("IndustrialPlatform.Collaboration");
    public static readonly Meter Meter = new("IndustrialPlatform.Collaboration", "1.0.0");
    public static readonly Counter<long> OutboxPublished = Meter.CreateCounter<long>("collaboration.outbox.published");
    public static readonly Counter<long> OutboxRetried = Meter.CreateCounter<long>("collaboration.outbox.retried");
    public static readonly Counter<long> OutboxDeadLettered = Meter.CreateCounter<long>("collaboration.outbox.dead_lettered");
    public static readonly Counter<long> FileReconciliationFailed = Meter.CreateCounter<long>("collaboration.file.reconciliation.failed");
    public static readonly Histogram<double> PublishDuration = Meter.CreateHistogram<double>("collaboration.outbox.publish.duration", "ms");
}
