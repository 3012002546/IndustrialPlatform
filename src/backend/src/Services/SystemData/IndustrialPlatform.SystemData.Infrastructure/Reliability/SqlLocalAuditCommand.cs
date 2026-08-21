using IndustrialPlatform.Infrastructure.Database;
using IndustrialPlatform.SystemData.Application.Auditing;
using IndustrialPlatform.SystemData.Infrastructure.Persistence.Entities;

namespace IndustrialPlatform.SystemData.Infrastructure.Reliability;

public sealed class SqlLocalAuditCommand : ILocalAuditCommand
{
    private readonly SqlSugarDbContext _dbContext;
    public SqlLocalAuditCommand(SqlSugarDbContext dbContext) => _dbContext = dbContext;
    public async Task RecordAsync(LocalAuditEntry entry, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        await _dbContext.SqlSugar.Insertable(new SystemDataOperationAuditTable
        {
            Id = Guid.NewGuid(), IsFrozen = true, IsLocked = false, IsDeleted = false, EntityType = "SystemData.OperationAudit",
            CreatedOn = DateTimeOffset.UtcNow, LastUpdatedOn = DateTimeOffset.UtcNow, OptimisticVersion = 1, ConcurrencyVersion = Guid.NewGuid(),
            TenantNId = entry.TenantNId, ActorUserNId = entry.ActorUserNId, Action = entry.Action, ObjectType = entry.ObjectType, ObjectNId = entry.ObjectNId,
            Reason = Limit(entry.Reason), BeforeSummary = Limit(entry.BeforeSummary), AfterSummary = Limit(entry.AfterSummary), TraceId = Limit(entry.TraceId) ?? string.Empty,
        }).ExecuteCommandAsync(cancellationToken);
    }
    private static string? Limit(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim()[..Math.Min(value.Trim().Length, 1000)];
}
