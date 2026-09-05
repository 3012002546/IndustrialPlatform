using System.Collections.Concurrent;
using System.Diagnostics.Metrics;

namespace IndustrialPlatform.ReferenceData.Infrastructure.Outbox;

public static class ReferenceDataMetrics
{
    private static readonly Meter Meter = new("IndustrialPlatform.ReferenceData", "v1");
    private static readonly ConcurrentDictionary<string, long> PendingByModule = new(StringComparer.Ordinal);

    public static readonly Counter<long> CacheHits =
        Meter.CreateCounter<long>("referencedata_cache_hit_total");
    public static readonly Counter<long> CacheMisses =
        Meter.CreateCounter<long>("referencedata_cache_miss_total");
    public static readonly Counter<long> CacheFallbacks =
        Meter.CreateCounter<long>("referencedata_cache_fallback_total");
    public static readonly Counter<long> OutboxPublished =
        Meter.CreateCounter<long>("referencedata_outbox_published_total");
    public static readonly Counter<long> OutboxRetries =
        Meter.CreateCounter<long>("referencedata_outbox_retry_total");
    public static readonly Counter<long> OutboxFailed =
        Meter.CreateCounter<long>("referencedata_outbox_failed_total");
    private static readonly Histogram<double> ApiDuration =
        Meter.CreateHistogram<double>("referencedata_api_duration_ms", "ms");
    private static readonly Counter<long> ApiErrors =
        Meter.CreateCounter<long>("referencedata_api_error_total");
    private static readonly Counter<long> ConcurrencyConflicts =
        Meter.CreateCounter<long>("referencedata_concurrency_conflict_total");
    private static readonly Counter<long> CodingConflicts =
        Meter.CreateCounter<long>("referencedata_coding_conflict_total");
    private static readonly Counter<long> DynamicPublishedRecords =
        Meter.CreateCounter<long>("referencedata_dynamic_property_published_records_total");
    private static readonly Counter<long> DynamicValidationFailures =
        Meter.CreateCounter<long>("referencedata_dynamic_property_validation_failure_total");
    private static readonly Counter<long> DatabaseDegraded =
        Meter.CreateCounter<long>("referencedata_database_degraded_total");

    private static readonly ObservableGauge<long> PendingGauge = Meter.CreateObservableGauge(
        "referencedata_outbox_pending",
        () => PendingByModule.Select(pair => new Measurement<long>(pair.Value,
            new KeyValuePair<string, object?>("module", pair.Key))));

    public static void SetPending(IReadOnlyDictionary<string, long> pending)
    {
        foreach (var module in ModuleKeys)
            PendingByModule[module] = pending.GetValueOrDefault(module);
    }

    public static void RecordApi(string module, string result, double durationMs)
    {
        var moduleTag = new KeyValuePair<string, object?>("module", module);
        ApiDuration.Record(durationMs, moduleTag);
        if (result == "Success") return;
        ApiErrors.Add(1, moduleTag, new KeyValuePair<string, object?>("error", result));
        if (result == "REF-CONCURRENCY-CONFLICT") ConcurrencyConflicts.Add(1, moduleTag);
        if (result == "503") DatabaseDegraded.Add(1, moduleTag);
        if (module == "coding-rules" && result is "REF-IDEMPOTENCY-CONFLICT" or "REF-CONCURRENCY-CONFLICT")
            CodingConflicts.Add(1, moduleTag);
    }

    public static void RecordDynamicPublication(long recordCount) =>
        DynamicPublishedRecords.Add(recordCount);

    public static void RecordDynamicValidationFailure() =>
        DynamicValidationFailures.Add(1);

    private static readonly string[] ModuleKeys =
    [
        "dictionary", "parameter", "dynamic-property", "metadata", "coding-rule", "state-machine",
        "unit-of-measure",
    ];
}
