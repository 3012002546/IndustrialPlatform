using IndustrialPlatform.Infrastructure.Database;
using SqlSugar;

namespace IndustrialPlatform.ReferenceData.Infrastructure.Outbox;

public sealed record ReferenceDataPendingEvent(
    Guid EventId, string ModuleKey, string EventName, string Payload, int AttemptCount);

public readonly record struct ReferenceDataOutboxFailureResult(bool Updated, bool Terminal);

public sealed class ReferenceDataOutboxStore(SqlSugarDbContext context)
{
    private ISqlSugarClient Database => context.SqlSugar;
    private string Table => Database.CurrentConnectionConfig.DbType == DbType.PostgreSQL
        ? "reference_data.outbox_message"
        : "reference_data_outbox_message";

    public async Task<IReadOnlyList<ReferenceDataPendingEvent>> GetPendingAsync(
        DateTimeOffset now, int limit, CancellationToken cancellationToken)
    {
        now = StoredTime(now);
        var rows = await Database.Queryable<ReferenceDataOutboxRow>().AS(Table)
            .Where(row => row.Status == "Pending" && row.AttemptCount < 10
                && (row.NextAttemptOn == null || row.NextAttemptOn <= now))
            .OrderBy(row => row.CreatedTime).OrderBy(row => row.EventId)
            .Take(Math.Clamp(limit, 1, 100)).ToListAsync(cancellationToken);
        return rows.Select(row => new ReferenceDataPendingEvent(row.EventId, row.ModuleKey, row.EventName,
            row.Payload, row.AttemptCount)).ToArray();
    }

    public async Task<bool> MarkPublishedAsync(Guid eventId, DateTimeOffset publishedOn,
        CancellationToken cancellationToken)
    {
        var affected = await Database.Updateable<ReferenceDataOutboxRow>().AS(Table)
            .SetColumns(row => new ReferenceDataOutboxRow
            {
                Status = "Published",
                PublishedOn = StoredTime(publishedOn),
                NextAttemptOn = null,
                LastError = null,
            })
            .Where(row => row.EventId == eventId
                && (row.Status == "Pending" || row.Status == "Failed"))
            .ExecuteCommandAsync(cancellationToken);
        return affected == 1;
    }

    public async Task<ReferenceDataOutboxFailureResult> RecordFailureAsync(
        Guid eventId, int currentAttempt, string failure,
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        var attempt = Math.Min(currentAttempt + 1, 10);
        var failed = attempt == 10;
        var safe = failure.Length <= 500 ? failure : failure[..500];
        DateTimeOffset? next = failed ? null : StoredTime(now.AddSeconds(BackoffSeconds(attempt)));
        var affected = await Database.Updateable<ReferenceDataOutboxRow>().AS(Table)
            .SetColumns(row => new ReferenceDataOutboxRow
            {
                Status = failed ? "Failed" : "Pending",
                AttemptCount = attempt,
                NextAttemptOn = next,
                LastError = safe,
            })
            .Where(row => row.EventId == eventId && row.Status == "Pending"
                && row.AttemptCount == currentAttempt)
            .ExecuteCommandAsync(cancellationToken);
        return new(affected == 1, affected == 1 && failed);
    }

    public async Task<IReadOnlyDictionary<string, long>> CountPendingByModuleAsync(
        CancellationToken cancellationToken)
    {
        var rows = await Database.Queryable<ReferenceDataOutboxRow>().AS(Table)
            .Where(row => row.Status == "Pending")
            .GroupBy(row => row.ModuleKey)
            .Select(row => new ModuleCount { ModuleKey = row.ModuleKey, Count = SqlFunc.AggregateCount(row.EventId) })
            .ToListAsync(cancellationToken);
        return rows.ToDictionary(row => row.ModuleKey, row => row.Count, StringComparer.Ordinal);
    }

    private static int BackoffSeconds(int attempt) => attempt switch
    {
        1 => 1,
        2 => 2,
        3 => 4,
        4 => 8,
        5 => 16,
        6 => 30,
        7 => 60,
        8 => 120,
        _ => 300,
    };

    private DateTimeOffset StoredTime(DateTimeOffset value) =>
        Database.CurrentConnectionConfig.DbType == DbType.PostgreSQL
            ? value.ToUniversalTime()
            : value.ToLocalTime();

    private sealed class ModuleCount
    {
        public string ModuleKey { get; set; } = string.Empty;
        public long Count { get; set; }
    }
}
