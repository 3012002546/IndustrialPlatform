using IndustrialPlatform.Collaboration.Infrastructure;

namespace IndustrialPlatform.Collaboration.Tests;

public sealed class Infrastructure_CollaborationMetricsTests
{
    [Fact]
    public async Task Meter_signals_are_exported_as_prometheus_text()
    {
        using var collector = new CollaborationMetricsCollector();
        CollaborationDiagnostics.OutboxPublished.Add(1);
        CollaborationDiagnostics.PublishDuration.Record(12.5);

        var output = await collector.RenderPrometheusAsync(null, CancellationToken.None);

        Assert.Contains("collaboration_outbox_published", output);
        Assert.Contains("collaboration_outbox_publish_duration_sum", output);
        Assert.Contains("collaboration_activity_started_total", output);
    }
}
