using IndustrialPlatform.Identity.Application.Management;
using IndustrialPlatform.Identity.Infrastructure.Persistence.Entities;
using IndustrialPlatform.Infrastructure.Database;
using SqlSugar;

namespace IndustrialPlatform.Identity.Infrastructure.Management;

/// <summary>
/// 写请求幂等存储实现(§29A.5):identity_write_idempotency 表读写。
/// 同键不同请求哈希抛 <see cref="IdempotencyConflictException"/>;同键同哈希且已完成返回 Replay。
/// </summary>
public sealed class IdempotencyStore : IIdempotencyStore
{
    private readonly SqlSugarDbContext _dbContext;

    public IdempotencyStore(SqlSugarDbContext dbContext)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        _dbContext = dbContext;
    }

    /// <inheritdoc />
    public async Task<IdempotencyDecision> TryReserveAsync(
        string tenantNId,
        string actorUserNId,
        string idempotencyKey,
        string requestHash,
        CancellationToken cancellationToken = default)
    {
        var trimmedKey = idempotencyKey.Trim();
        var existing = await _dbContext.SqlSugar.Queryable<IdentityWriteIdempotencyTable>()
            .Where(t => t.TenantNId == tenantNId
                && t.ActorUserNId == actorUserNId
                && t.IdempotencyKey == trimmedKey
                && !t.IsDeleted)
            .FirstAsync(cancellationToken);

        if (existing is null)
        {
            var now = DateTimeOffset.UtcNow;
            await _dbContext.SqlSugar.Insertable(new IdentityWriteIdempotencyTable
            {
                Id = Guid.NewGuid(),
                IsFrozen = false,
                IsLocked = false,
                IsDeleted = false,
                EntityType = typeof(IdentityWriteIdempotencyTable).FullName ?? typeof(IdentityWriteIdempotencyTable).Name,
                CreatedOn = now,
                LastUpdatedOn = now,
                OptimisticVersion = 0,
                ConcurrencyVersion = Guid.NewGuid(),
                TenantNId = tenantNId,
                ActorUserNId = actorUserNId,
                IdempotencyKey = trimmedKey,
                RequestHash = requestHash,
                Completed = false,
                CompletedOn = null,
            }).ExecuteCommandAsync(cancellationToken);
            return IdempotencyDecision.Proceed;
        }

        if (!string.Equals(existing.RequestHash, requestHash, StringComparison.Ordinal))
        {
            throw new IdempotencyConflictException();
        }

        return existing.Completed ? IdempotencyDecision.Replay : IdempotencyDecision.Proceed;
    }

    /// <inheritdoc />
    public async Task MarkCompletedAsync(
        string tenantNId,
        string actorUserNId,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        await _dbContext.SqlSugar.Updateable<IdentityWriteIdempotencyTable>()
            .SetColumns(t => new IdentityWriteIdempotencyTable
            {
                Completed = true,
                CompletedOn = now,
                LastUpdatedOn = now,
                OptimisticVersion = t.OptimisticVersion + 1,
                ConcurrencyVersion = Guid.NewGuid(),
            })
            .Where(t => t.TenantNId == tenantNId
                && t.ActorUserNId == actorUserNId
                && t.IdempotencyKey == idempotencyKey.Trim()
                && !t.IsDeleted)
            .ExecuteCommandAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task ReleaseAsync(
        string tenantNId,
        string actorUserNId,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        await _dbContext.SqlSugar.Updateable<IdentityWriteIdempotencyTable>()
            .SetColumns(t => new IdentityWriteIdempotencyTable
            {
                IsDeleted = true,
                LastUpdatedOn = now,
                OptimisticVersion = t.OptimisticVersion + 1,
                ConcurrencyVersion = Guid.NewGuid(),
            })
            .Where(t => t.TenantNId == tenantNId
                && t.ActorUserNId == actorUserNId
                && t.IdempotencyKey == idempotencyKey.Trim()
                && !t.IsDeleted
                && !t.Completed)
            .ExecuteCommandAsync(cancellationToken);
    }
}
