using IndustrialPlatform.Identity.Application.Authentication;
using IndustrialPlatform.Identity.Infrastructure.Persistence.Entities;
using IndustrialPlatform.Identity.Infrastructure.Security;
using IndustrialPlatform.Infrastructure.Database;

namespace IndustrialPlatform.Identity.Infrastructure.Authentication;

/// <summary>
/// 刷新会话持久化实现(§13):登录追加写入、刷新原子旋转(防重放)、单会话/全部注销撤销。
/// 只存 RawToken 的 SHA-256 哈希,原始 Refresh Token 绝不下库/进日志。
/// </summary>
public sealed class RefreshSessionStore : IRefreshSessionStore
{
    private readonly SqlSugarDbContext _dbContext;

    /// <summary>初始化刷新会话存储。</summary>
    public RefreshSessionStore(SqlSugarDbContext dbContext)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        _dbContext = dbContext;
    }

    /// <inheritdoc/>
    public async Task AddAsync(NewRefreshSession session, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);

        var row = ToTable(session);
        await _dbContext.SqlSugar.Insertable(row).ExecuteCommandAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<StoredRefreshSession?> FindByRawTokenAsync(string rawToken, CancellationToken cancellationToken)
    {
        var hash = Hashing.Sha256Hex(rawToken);
        var row = await _dbContext.SqlSugar.Queryable<RefreshSessionTable>()
            .Where(t => t.TokenHash == hash && !t.IsDeleted)
            .FirstAsync(cancellationToken);
        return row is null ? null : ToStored(row);
    }

    /// <inheritdoc/>
    public async Task<RefreshRotationStatus> RotateAsync(
        Guid currentSessionId,
        NewRefreshSession replacement,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(replacement);

        var sugar = _dbContext.SqlSugar;
        var replacementRow = ToTable(replacement);
        var now = DateTimeOffset.UtcNow;

        sugar.Ado.BeginTran();
        try
        {
            // 先插入替代会话:自引用复合外键 (replaced_by_session_id, replaced_by_session_is_deleted)
            // 需要目标行已存在;并发未命中时随事务回滚,不留孤儿行。
            await sugar.Insertable(replacementRow).ExecuteCommandAsync(cancellationToken);

            // 原子守卫:仅未被消费、未被撤销、未删除的当前会话可旋转。
            var affected = await sugar.Updateable<RefreshSessionTable>()
                .SetColumns(t => new RefreshSessionTable
                {
                    UsedOn = now,
                    LastUpdatedOn = now,
                    ReplacedBySessionNId = replacementRow.NId,
                    ReplacedBySessionId = replacementRow.Id,
                    ReplacedBySessionIsDeleted = false,
                })
                .Where(t => t.Id == currentSessionId
                    && t.UsedOn == null
                    && t.RevokedOn == null
                    && !t.IsDeleted)
                .ExecuteCommandAsync(cancellationToken);

            if (affected != 1)
            {
                sugar.Ado.RollbackTran();
                return await ClassifyNonRotatedAsync(currentSessionId, cancellationToken);
            }

            sugar.Ado.CommitTran();
            return RefreshRotationStatus.Rotated;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            sugar.Ado.RollbackTran();
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task RevokeFamilyAsync(string familyNId, string reason, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(familyNId))
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        await _dbContext.SqlSugar.Updateable<RefreshSessionTable>()
            .SetColumns(t => new RefreshSessionTable
            {
                RevokedOn = now,
                RevokeReason = reason,
                LastUpdatedOn = now,
            })
            .Where(t => t.FamilyNId == familyNId && t.RevokedOn == null && !t.IsDeleted)
            .ExecuteCommandAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task RevokeAllForUserAsync(Guid userId, string reason, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        await _dbContext.SqlSugar.Updateable<RefreshSessionTable>()
            .SetColumns(t => new RefreshSessionTable
            {
                RevokedOn = now,
                RevokeReason = reason,
                LastUpdatedOn = now,
            })
            .Where(t => t.UserId == userId && t.RevokedOn == null && !t.IsDeleted)
            .ExecuteCommandAsync(cancellationToken);
    }

    /// <summary>
    /// 旋转原子更新未命中时判定并发结果:当前会话已被消费(并发旋转成功)返回重放,
    /// 已撤销/删除/不存在返回无效。
    /// </summary>
    private async Task<RefreshRotationStatus> ClassifyNonRotatedAsync(Guid currentSessionId, CancellationToken cancellationToken)
    {
        var current = await _dbContext.SqlSugar.Queryable<RefreshSessionTable>()
            .Where(t => t.Id == currentSessionId)
            .FirstAsync(cancellationToken);

        if (current is not null && !current.IsDeleted && current.UsedOn is not null && current.RevokedOn is null)
        {
            return RefreshRotationStatus.Reused;
        }

        return RefreshRotationStatus.Invalid;
    }

    /// <summary>NewRefreshSession → 表模型;只存 Token/IP/UA 的 SHA-256 哈希。</summary>
    private static RefreshSessionTable ToTable(NewRefreshSession session) =>
        new()
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

    private static StoredRefreshSession ToStored(RefreshSessionTable row) =>
        new(
            row.Id,
            row.TenantNId,
            row.NId,
            row.FamilyNId,
            row.UserId,
            row.UserIsDeleted,
            row.ExpiresOn,
            row.UsedOn,
            row.RevokedOn,
            row.RevokeReason,
            row.ReplacedBySessionNId);
}
