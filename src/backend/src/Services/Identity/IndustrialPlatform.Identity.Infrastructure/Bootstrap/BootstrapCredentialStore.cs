using IndustrialPlatform.Identity.Application.Bootstrap;
using IndustrialPlatform.Identity.Domain.Users;
using IndustrialPlatform.Identity.Infrastructure.Persistence.Entities;
using IndustrialPlatform.Infrastructure.Database;
using SqlSugar;

namespace IndustrialPlatform.Identity.Infrastructure.Bootstrap;

/// <summary>
/// bootstrap 凭据交付存储实现(§29A.4):identity_bootstrap_credential 表读写与状态机。
/// 只保存引用哈希与状态,明文临时密码绝不入库。
/// </summary>
public sealed class BootstrapCredentialStore : IBootstrapCredentialStore
{
    private readonly SqlSugarDbContext _dbContext;

    public BootstrapCredentialStore(SqlSugarDbContext dbContext)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        _dbContext = dbContext;
    }

    /// <inheritdoc />
    public async Task<BootstrapCredentialDelivery> CreatePendingAsync(
        string tenantNId,
        string userNId,
        string? deliveryReferenceHash,
        string recoveryReferenceHash,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantNId);
        ArgumentException.ThrowIfNullOrWhiteSpace(userNId);
        ArgumentException.ThrowIfNullOrWhiteSpace(recoveryReferenceHash);

        var now = DateTimeOffset.UtcNow;
        var row = new BootstrapCredentialTable
        {
            Id = Guid.NewGuid(),
            IsFrozen = false,
            IsLocked = false,
            IsDeleted = false,
            EntityType = typeof(BootstrapCredentialTable).FullName ?? typeof(BootstrapCredentialTable).Name,
            CreatedOn = now,
            LastUpdatedOn = now,
            OptimisticVersion = 0,
            ConcurrencyVersion = Guid.NewGuid(),
            TenantNId = tenantNId,
            UserNId = userNId,
            State = (int)BootstrapDeliveryState.Pending,
            DeliveryReferenceHash = deliveryReferenceHash,
            RecoveryReferenceHash = recoveryReferenceHash,
            DeliveredOn = null,
            RecoveredOn = null,
        };

        await _dbContext.SqlSugar.Insertable(row).ExecuteCommandAsync(cancellationToken);
        return Map(row);
    }

    /// <inheritdoc />
    public async Task<BootstrapCredentialDelivery?> GetLatestAsync(
        string tenantNId,
        string userNId,
        CancellationToken cancellationToken = default)
    {
        var row = await _dbContext.SqlSugar.Queryable<BootstrapCredentialTable>()
            .Where(t => t.TenantNId == tenantNId && t.UserNId == userNId && !t.IsDeleted)
            .OrderByDescending(t => t.CreatedOn)
            .FirstAsync(cancellationToken);
        return row is null ? null : Map(row);
    }

    /// <inheritdoc />
    public async Task<BootstrapCredentialDelivery> ClaimAsync(Guid deliveryId, CancellationToken cancellationToken = default)
    {
        var row = await _dbContext.SqlSugar.Queryable<BootstrapCredentialTable>()
            .Where(t => t.Id == deliveryId && !t.IsDeleted)
            .FirstAsync(cancellationToken)
            ?? throw new BootstrapCredentialAlreadyRetrievedException();

        if (row.State != (int)BootstrapDeliveryState.Pending)
        {
            // §29A.4:临时密码只能领取一次;再次领取直接拒绝,不泄漏细节。
            throw new BootstrapCredentialAlreadyRetrievedException();
        }

        var now = DateTimeOffset.UtcNow;
        await _dbContext.SqlSugar.Updateable<BootstrapCredentialTable>()
            .SetColumns(t => new BootstrapCredentialTable
            {
                State = (int)BootstrapDeliveryState.Delivered,
                DeliveredOn = now,
                LastUpdatedOn = now,
                OptimisticVersion = t.OptimisticVersion + 1,
                ConcurrencyVersion = Guid.NewGuid(),
            })
            .Where(t => t.Id == deliveryId && t.State == (int)BootstrapDeliveryState.Pending)
            .ExecuteCommandAsync(cancellationToken);

        row.State = (int)BootstrapDeliveryState.Delivered;
        row.DeliveredOn = now;
        return Map(row);
    }

    /// <inheritdoc />
    public async Task<BootstrapCredentialDelivery> MarkRecoveredAsync(Guid deliveryId, CancellationToken cancellationToken = default)
    {
        var row = await _dbContext.SqlSugar.Queryable<BootstrapCredentialTable>()
            .Where(t => t.Id == deliveryId && !t.IsDeleted)
            .FirstAsync(cancellationToken)
            ?? throw new BootstrapRecoveryRejectedException();

        var now = DateTimeOffset.UtcNow;
        await _dbContext.SqlSugar.Updateable<BootstrapCredentialTable>()
            .SetColumns(t => new BootstrapCredentialTable
            {
                State = (int)BootstrapDeliveryState.Recovered,
                RecoveredOn = now,
                LastUpdatedOn = now,
                OptimisticVersion = t.OptimisticVersion + 1,
                ConcurrencyVersion = Guid.NewGuid(),
            })
            .Where(t => t.Id == deliveryId && !t.IsDeleted)
            .ExecuteCommandAsync(cancellationToken);

        row.State = (int)BootstrapDeliveryState.Recovered;
        row.RecoveredOn = now;
        return Map(row);
    }

    private static BootstrapCredentialDelivery Map(BootstrapCredentialTable row) => new(
        row.Id,
        row.TenantNId,
        row.UserNId,
        (BootstrapDeliveryState)row.State,
        row.DeliveryReferenceHash,
        row.RecoveryReferenceHash,
        row.DeliveredOn,
        row.RecoveredOn);
}
