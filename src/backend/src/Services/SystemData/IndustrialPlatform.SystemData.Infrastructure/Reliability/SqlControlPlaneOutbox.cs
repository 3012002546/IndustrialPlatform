using IndustrialPlatform.Infrastructure.Database;
using IndustrialPlatform.SystemData.Application.Reliability;
using IndustrialPlatform.SystemData.Infrastructure.Persistence.Entities;

namespace IndustrialPlatform.SystemData.Infrastructure.Reliability;

public sealed class SqlControlPlaneOutbox : IControlPlaneOutbox
{
    private readonly SqlSugarDbContext _dbContext;
    public SqlControlPlaneOutbox(SqlSugarDbContext dbContext) => _dbContext = dbContext;
    public async Task EnqueueAsync(ControlPlaneEvent item, CancellationToken cancellationToken)
    {
        var exists = await _dbContext.SqlSugar.Queryable<SystemDataOutboxTable>().AnyAsync(x => x.EventId == item.EventId, cancellationToken);
        if (exists) return;
        await _dbContext.SqlSugar.Insertable(new SystemDataOutboxTable { EventId = item.EventId, EventType = item.EventType, EventVersion = item.EventVersion, TenantNId = item.TenantNId, Payload = item.Payload, EventCreatedTime = item.CreatedOn, RetryCount = 0 }).ExecuteCommandAsync(cancellationToken);
    }
    public async Task<IReadOnlyCollection<ControlPlaneEvent>> GetPendingAsync(int limit, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var rows = await _dbContext.SqlSugar.Queryable<SystemDataOutboxTable>().Where(x => x.PublishedOn == null && x.RetryCount < 10 && (x.NextAttemptOn == null || x.NextAttemptOn <= now)).OrderBy(x => x.EventCreatedTime).Take(Math.Clamp(limit, 1, 100)).ToListAsync(cancellationToken);
        return rows.Select(x => new ControlPlaneEvent(x.EventId, x.EventType, x.EventVersion, x.TenantNId, x.Payload, x.EventCreatedTime)).ToArray();
    }
    public Task MarkPublishedAsync(Guid eventId, CancellationToken cancellationToken) => _dbContext.SqlSugar.Updateable<SystemDataOutboxTable>().SetColumns(x => new SystemDataOutboxTable { PublishedOn = DateTimeOffset.UtcNow }).Where(x => x.EventId == eventId).ExecuteCommandAsync(cancellationToken);
    public async Task<bool> RecordFailureAsync(Guid eventId, string failureMessage, CancellationToken cancellationToken)
    {
        var safeMessage = failureMessage.Length > 500 ? failureMessage.Substring(0, 500) : failureMessage;
        var rowBefore = await _dbContext.SqlSugar.Queryable<SystemDataOutboxTable>().Where(x => x.EventId == eventId).FirstAsync(cancellationToken);
        if (rowBefore is null) return false;
        var retry = rowBefore.RetryCount + 1;
        var dead = retry >= 10;
        DateTimeOffset? nextAttemptOn = dead ? null : DateTimeOffset.UtcNow.AddSeconds(Math.Min(Math.Pow(2, retry - 1), 900));
        await _dbContext.SqlSugar.Updateable<SystemDataOutboxTable>().SetColumns(x => new SystemDataOutboxTable { RetryCount = retry, LastError = safeMessage, NextAttemptOn = nextAttemptOn, DeadOn = dead ? DateTimeOffset.UtcNow : null }).Where(x => x.EventId == eventId).ExecuteCommandAsync(cancellationToken);
        return dead;
    }
    public async Task<OutboxEventStatus?> GetStatusAsync(Guid eventId, CancellationToken cancellationToken)
    {
        var row = await _dbContext.SqlSugar.Queryable<SystemDataOutboxTable>().Where(x => x.EventId == eventId).FirstAsync(cancellationToken);
        return row is null ? null : new OutboxEventStatus(row.EventId, row.RetryCount, row.PublishedOn, row.LastError, row.NextAttemptOn, row.DeadOn is not null);
    }
}
