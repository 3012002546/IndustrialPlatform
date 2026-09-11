using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text;
using IndustrialPlatform.Collaboration.Application;

namespace IndustrialPlatform.Collaboration.Infrastructure;

/// <summary>
/// Small dependency-free Prometheus exporter for the collaboration signals. It
/// attaches a MeterListener and ActivityListener to the same Meter/ActivitySource
/// used by the application, so the signals are actually connected to an export path.
/// </summary>
public sealed class CollaborationMetricsCollector : IDisposable
{
    private readonly MeterListener _meterListener;
    private readonly ActivityListener _activityListener;
    private readonly ConcurrentDictionary<string, double> _values = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, long> _counts = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> _types = new(StringComparer.Ordinal);
    private long _activitiesStarted;

    public CollaborationMetricsCollector()
    {
        _meterListener = new MeterListener
        {
            InstrumentPublished = (instrument, listener) =>
            {
                if (ReferenceEquals(instrument.Meter, CollaborationDiagnostics.Meter))
                {
                    _types[instrument.Name] = instrument switch
                    {
                        Histogram<double> => "histogram",
                        _ => "counter",
                    };
                    listener.EnableMeasurementEvents(instrument);
                }
            },
        };
        _meterListener.SetMeasurementEventCallback<long>((instrument, measurement, _, _) => Record(instrument, measurement));
        _meterListener.SetMeasurementEventCallback<double>((instrument, measurement, _, _) => Record(instrument, measurement));
        _meterListener.Start();

        _activityListener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == CollaborationDiagnostics.ActivitySource.Name,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStarted = _ => Interlocked.Increment(ref _activitiesStarted),
        };
        ActivitySource.AddActivityListener(_activityListener);
    }

    public async Task<string> RenderPrometheusAsync(ICollaborationRepository? repository, CancellationToken cancellationToken)
    {
        var output = new StringBuilder();
        foreach (var name in _values.Keys.OrderBy(value => value, StringComparer.Ordinal))
        {
            var metricName = Sanitize(name);
            var type = _types.GetValueOrDefault(name, "counter");
            if (type == "histogram")
            {
                output.AppendLine(FormattableString.Invariant($"# TYPE {metricName} histogram"));
                output.AppendLine(FormattableString.Invariant($"{metricName}_sum {_values.GetValueOrDefault(name):R}"));
                output.AppendLine(FormattableString.Invariant($"{metricName}_count {_counts.GetValueOrDefault(name)}"));
            }
            else
            {
                output.AppendLine(FormattableString.Invariant($"# TYPE {metricName} counter"));
                output.AppendLine(FormattableString.Invariant($"{metricName} {_values.GetValueOrDefault(name):R}"));
            }
        }

        output.AppendLine("# TYPE collaboration_activity_started_total counter");
        output.AppendLine(FormattableString.Invariant($"collaboration_activity_started_total {Interlocked.Read(ref _activitiesStarted)}"));
        if (repository is null)
        {
            output.AppendLine("# TYPE collaboration_outbox_health_available gauge");
            output.AppendLine("collaboration_outbox_health_available 0");
            return output.ToString();
        }

        try
        {
            var health = await repository.GetOutboxHealthAsync(DateTimeOffset.UtcNow, cancellationToken);
            output.AppendLine("# TYPE collaboration_outbox_pending gauge");
            output.AppendLine(FormattableString.Invariant($"collaboration_outbox_pending {health?.PendingCount ?? 0}"));
            output.AppendLine("# TYPE collaboration_outbox_dead_lettered gauge");
            output.AppendLine(FormattableString.Invariant($"collaboration_outbox_dead_lettered {health?.DeadLetterCount ?? 0}"));
            output.AppendLine("# TYPE collaboration_outbox_health_available gauge");
            output.AppendLine(FormattableString.Invariant($"collaboration_outbox_health_available {(health is null ? 0 : 1)}"));
        }
        catch
        {
            output.AppendLine("# TYPE collaboration_outbox_health_available gauge");
            output.AppendLine("collaboration_outbox_health_available 0");
        }

        return output.ToString();
    }

    public void Dispose()
    {
        _meterListener.Dispose();
        _activityListener.Dispose();
    }

    private void Record<T>(Instrument instrument, T measurement) where T : struct, IConvertible
    {
        _values.AddOrUpdate(instrument.Name, measurement.ToDouble(System.Globalization.CultureInfo.InvariantCulture), (_, current) => current + measurement.ToDouble(System.Globalization.CultureInfo.InvariantCulture));
        if (_types.GetValueOrDefault(instrument.Name) == "histogram")
            _counts.AddOrUpdate(instrument.Name, 1, (_, current) => current + 1);
    }

    private static string Sanitize(string value) => value.Replace('.', '_').Replace('-', '_');
}
