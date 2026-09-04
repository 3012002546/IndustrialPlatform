using IndustrialPlatform.Identity.Application.Bootstrap;
using IndustrialPlatform.Identity.Domain.Identities;
using IndustrialPlatform.Identity.Domain.Users;
using IndustrialPlatform.Identity.Infrastructure.Persistence;
using IndustrialPlatform.Identity.Infrastructure.Persistence.Entities;
using IndustrialPlatform.Identity.Infrastructure.Persistence.Migrations;
using IndustrialPlatform.Identity.Infrastructure.Persistence.Repositories;
using IndustrialPlatform.Infrastructure.Database;
using SqlSugar;

namespace IndustrialPlatform.Identity.Infrastructure.Bootstrap;

/// <summary>
/// bootstrap admin 与初始化状态存储实现(§29A.4):admin 快照装载/改密,
/// Schema 版本、种子账本与系统角色完整性读取。
/// </summary>
public sealed class BootstrapStore : IBootstrapStore
{
    private readonly SqlSugarDbContext _dbContext;
    private readonly IUserRepository _userRepository;

    public BootstrapStore(SqlSugarDbContext dbContext, IUserRepository userRepository)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentNullException.ThrowIfNull(userRepository);
        _dbContext = dbContext;
        _userRepository = userRepository;
    }

    /// <inheritdoc />
    public async Task<BootstrapAdminSnapshot> GetAdminIncludingDeletedAsync(
        string tenantNId,
        string userNId,
        CancellationToken cancellationToken = default)
    {
        var normalized = NId.Create(userNId).Normalized;
        var row = await _dbContext.SqlSugar.Queryable<UserTable>()
            .Where(t => t.TenantNId == tenantNId && t.NormalizedNId == normalized)
            .FirstAsync(cancellationToken);

        if (row is null)
        {
            return new BootstrapAdminSnapshot(userNId, Exists: false, IsDeleted: false, IsActive: false, MustChangePassword: false, HasSystemAdminRole: false, AuthVersion: 0);
        }

        var hasSystemRole = await HasSystemAdminRoleAsync(row.Id, cancellationToken);
        return new BootstrapAdminSnapshot(
            row.NId,
            Exists: true,
            IsDeleted: row.IsDeleted,
            IsActive: !row.IsDeleted && row.Status == UserStatus.Active,
            MustChangePassword: row.MustChangePassword,
            HasSystemAdminRole: hasSystemRole,
            AuthVersion: row.AuthVersion);
    }

    /// <inheritdoc />
    public async Task UpdateAdminPasswordAsync(
        string tenantNId,
        string userNId,
        string newPasswordHash,
        CancellationToken cancellationToken = default)
    {
        var normalized = NId.Create(userNId).Normalized;
        var row = await _dbContext.SqlSugar.Queryable<UserTable>()
            .Where(t => t.TenantNId == tenantNId && t.NormalizedNId == normalized && !t.IsDeleted)
            .FirstAsync(cancellationToken)
            ?? throw new BootstrapRecoveryRejectedException();

        var roleRows = await _dbContext.SqlSugar.Queryable<UserRoleTable>()
            .Where(t => t.UserId == row.Id && !t.IsDeleted && !t.UserIsDeleted)
            .OrderBy(t => t.Id)
            .ToListAsync(cancellationToken);
        var user = TableMapper.ToUser(row, roleRows.Select(TableMapper.ToUserRole).ToList());

        var expectedOptimisticVersion = user.OptimisticVersion;
        var expectedConcurrencyVersion = user.ConcurrencyVersion;
        user.ChangePasswordHash(newPasswordHash);
        await _userRepository.UpdateAsync(user, expectedOptimisticVersion, expectedConcurrencyVersion, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<string> GetSchemaVersionAsync(CancellationToken cancellationToken = default)
    {
        var ids = await _dbContext.SqlSugar.Queryable<SchemaMigrationRecord>()
            .Select(r => r.MigrationId)
            .ToListAsync(cancellationToken);
        return ids.Count == 0 ? string.Empty : ids.Max(StringComparer.Ordinal) ?? string.Empty;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SeedVersionStatus>> GetSeedLedgerAsync(
        string tenantNId,
        CancellationToken cancellationToken = default)
    {
        var rows = await _dbContext.SqlSugar.Queryable<SeedLedgerTable>()
            .Where(t => t.TenantNId == tenantNId && !t.IsDeleted)
            .OrderByDescending(t => t.CreatedOn)
            .ToListAsync(cancellationToken);

        // 每个种子键取最新一条(版本升级追加,最新即权威)。
        return rows
            .GroupBy(r => r.SeedNId, StringComparer.Ordinal)
            .Select(g => g.First())
            .Select(r => new SeedVersionStatus(r.SeedNId, r.SeedVersion, r.Status, r.Checksum, r.Scope, r.AppliedOn))
            .OrderBy(s => s.SeedKey, StringComparer.Ordinal)
            .ToList();
    }

    /// <inheritdoc />
    public async Task<bool> IsSystemAdminRoleCompleteAsync(string tenantNId, CancellationToken cancellationToken = default)
    {
        var role = await _dbContext.SqlSugar.Queryable<RoleTable>()
            .Where(r => r.TenantNId == tenantNId && r.NId == BootstrapSeedCatalog.SystemAdminRoleNId && !r.IsDeleted)
            .FirstAsync(cancellationToken);
        if (role is null)
        {
            return false;
        }

        var catalogPermissionIds = await _dbContext.SqlSugar.Queryable<PermissionTable>()
            .Where(p => !p.IsDeleted)
            .Select(p => p.Id)
            .ToListAsync(cancellationToken);
        var rolePermissionIds = await _dbContext.SqlSugar.Queryable<RolePermissionTable>()
            .Where(rp => rp.RoleId == role.Id && !rp.IsDeleted && !rp.RoleIsDeleted)
            .Select(rp => rp.PermissionId)
            .ToListAsync(cancellationToken);

        var expected = catalogPermissionIds.ToHashSet();
        expected.ExceptWith(rolePermissionIds);
        return expected.Count == 0;
    }

    private async Task<bool> HasSystemAdminRoleAsync(Guid userId, CancellationToken cancellationToken)
    {
        var count = await _dbContext.SqlSugar.Queryable<UserRoleTable>()
            .InnerJoin<RoleTable>((ur, r) => ur.RoleId == r.Id && ur.RoleIsDeleted == r.IsDeleted)
            .Where((ur, r) => ur.UserId == userId
                && !ur.IsDeleted
                && !ur.UserIsDeleted
                && !r.IsDeleted
                && r.NId == BootstrapSeedCatalog.SystemAdminRoleNId)
            .CountAsync(cancellationToken);
        return count > 0;
    }
}
