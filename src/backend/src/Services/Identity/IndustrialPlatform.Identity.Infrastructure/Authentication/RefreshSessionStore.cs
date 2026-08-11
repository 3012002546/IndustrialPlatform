using IndustrialPlatform.Identity.Application.Authentication;
using IndustrialPlatform.Identity.Infrastructure.Persistence.Entities;
using IndustrialPlatform.Identity.Infrastructure.Security;
using IndustrialPlatform.Infrastructure.Database;

namespace IndustrialPlatform.Identity.Infrastructure.Authentication;

/// <summary>
/// 刷新会话持久化实现(§13):追加写入 identity_refresh_session。
/// 只存 RawToken 的 SHA-256 哈希,原始 Refresh Token 绝不下库/进日志。
/// </summary>
public sealed class RefreshSessionStore : IRefreshSessionStore
{
    private readonly SqlSugarDbContext _dbContext;

    /// <summary>初始化刷新会话写入器。</summary>
    public RefreshSessionStore(SqlSugarDbContext dbContext)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        _dbContext = dbContext;
    }

    /// <inheritdoc/>
    public async Task AddAsync(NewRefreshSession session, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);

        var row = new RefreshSessionTable
        {
            Id = Guid.NewGuid(),
            IsFrozen = false,
            IsLocked = false,
            IsDeleted = false,
            EntityType = typeof(RefreshSessionTable).FullName ?? typeof(RefreshSessionTable).Name,
            CreatedOn = session.CreatedOn,
            LastUpdatedOn = session.CreatedOn,
            OptimisticVersion = 0,
            ConcurrencyVersion = Guid.NewGuid(),
            TenantNId = session.TenantNId,
            NId = session.NId,
            FamilyNId = session.FamilyNId,
            UserId = session.UserId,
            UserIsDeleted = false,
            TokenHash = Hashing.Sha256Hex(session.RawToken),
            ExpiresOn = session.ExpiresOn,
            UserAgentHash = session.UserAgent is null ? null : Hashing.Sha256Hex(session.UserAgent),
            IpAddressHash = session.IpAddress is null ? null : Hashing.Sha256Hex(session.IpAddress),
        };

        await _dbContext.SqlSugar.Insertable(row).ExecuteCommandAsync(cancellationToken);
    }
}
