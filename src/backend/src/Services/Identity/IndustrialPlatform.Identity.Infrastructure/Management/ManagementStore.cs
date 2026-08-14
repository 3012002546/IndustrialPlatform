using IndustrialPlatform.Identity.Application.Management;
using IndustrialPlatform.Identity.Domain.Identities;
using IndustrialPlatform.Identity.Domain.Permissions;
using IndustrialPlatform.Identity.Domain.Roles;
using IndustrialPlatform.Identity.Domain.Users;
using IndustrialPlatform.Identity.Domain.UserGroups;
using IndustrialPlatform.Identity.Infrastructure.Persistence;
using IndustrialPlatform.Identity.Infrastructure.Persistence.Entities;
using IndustrialPlatform.Identity.Infrastructure.Persistence.Repositories;
using IndustrialPlatform.Infrastructure.Database;
using SqlSugar;

namespace IndustrialPlatform.Identity.Infrastructure.Management;

/// <summary>
/// 管理用例持久化端口实现(§16):组合领域仓储并补充分页/关系投影/持有者统计查询。
/// 按 NId 查询不区分租户,租户隔离由应用层显式校验;含软删除的唯一性检查供 NId 防复用。
/// </summary>
public sealed class ManagementStore : IManagementStore
{
    private readonly SqlSugarDbContext _dbContext;
    private readonly IUserRepository _userRepository;
    private readonly IRoleRepository _roleRepository;
    private readonly IPermissionRepository _permissionRepository;

    /// <summary>初始化管理存储。</summary>
    public ManagementStore(
        SqlSugarDbContext dbContext,
        IUserRepository userRepository,
        IRoleRepository roleRepository,
        IPermissionRepository permissionRepository)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentNullException.ThrowIfNull(userRepository);
        ArgumentNullException.ThrowIfNull(roleRepository);
        ArgumentNullException.ThrowIfNull(permissionRepository);
        _dbContext = dbContext;
        _userRepository = userRepository;
        _roleRepository = roleRepository;
        _permissionRepository = permissionRepository;
    }

    /// <inheritdoc/>
    public async Task<StoredUserPage> QueryUsersAsync(UserListFilter filter, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var query = _dbContext.SqlSugar.Queryable<UserTable>()
            .Where(t => t.TenantNId == filter.TenantNId && !t.IsDeleted);

        if (!string.IsNullOrWhiteSpace(filter.NId))
        {
            query = query.Where(t => t.NId.Contains(filter.NId.Trim()));
        }

        if (!string.IsNullOrWhiteSpace(filter.LoginName))
        {
            query = query.Where(t => t.LoginName.Contains(filter.LoginName.Trim()));
        }

        if (!string.IsNullOrWhiteSpace(filter.Name))
        {
            query = query.Where(t => t.Name.Contains(filter.Name.Trim()));
        }

        if (filter.Status is { } status)
        {
            query = query.Where(t => t.Status == status);
        }

        var total = await query.CountAsync(cancellationToken);
        var rows = await query
            .OrderBy(t => t.CreatedOn, OrderByType.Desc)
            .Skip((filter.PageIndex - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .ToListAsync(cancellationToken);

        var roleNIdsByUser = await BuildUserRoleNIdsMapAsync(rows.Select(r => r.Id).ToArray(), cancellationToken);
        var items = rows
            .Select(r => ToStoredUser(r, roleNIdsByUser.TryGetValue(r.Id, out var nIds) ? nIds : []))
            .ToList();
        return new StoredUserPage(items, total);
    }

    /// <inheritdoc/>
    public async Task<StoredUser?> GetUserAsync(string userNId, CancellationToken cancellationToken)
    {
        var row = await QueryUserRowByNIdAsync(userNId, includeDeleted: false, cancellationToken);
        if (row is null)
        {
            return null;
        }

        var roleNIds = await GetUserRoleNIdsAsync(row.Id, cancellationToken);
        return ToStoredUser(row, roleNIds);
    }

    /// <inheritdoc/>
    public async Task<User?> GetUserAggregateAsync(string userNId, CancellationToken cancellationToken)
    {
        var row = await QueryUserRowByNIdAsync(userNId, includeDeleted: false, cancellationToken);
        return row is null ? null : await _userRepository.GetByIdAsync(row.Id, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<bool> UserExistsByNIdAsync(string userNId, CancellationToken cancellationToken)
    {
        var normalized = NId.Create(userNId).Normalized;
        return await _dbContext.SqlSugar.Queryable<UserTable>()
            .Where(t => t.NormalizedNId == normalized)
            .AnyAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<bool> LoginNameExistsAsync(string tenantNId, string loginName, CancellationToken cancellationToken)
    {
        var normalized = loginName.Trim().ToUpperInvariant();
        return await _dbContext.SqlSugar.Queryable<UserTable>()
            .Where(t => t.TenantNId == tenantNId && t.NormalizedLoginName == normalized && !t.IsDeleted)
            .AnyAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<StoredRolePage> QueryRolesAsync(RoleListFilter filter, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var query = _dbContext.SqlSugar.Queryable<RoleTable>()
            .Where(t => t.TenantNId == filter.TenantNId && !t.IsDeleted);

        if (!string.IsNullOrWhiteSpace(filter.NId))
        {
            query = query.Where(t => t.NId.Contains(filter.NId.Trim()));
        }

        if (!string.IsNullOrWhiteSpace(filter.Name))
        {
            query = query.Where(t => t.Name.Contains(filter.Name.Trim()));
        }

        var total = await query.CountAsync(cancellationToken);
        var rows = await query
            .OrderBy(t => t.CreatedOn, OrderByType.Desc)
            .Skip((filter.PageIndex - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .ToListAsync(cancellationToken);

        var permissionNIdsByRole = await BuildRolePermissionNIdsMapAsync(rows.Select(r => r.Id).ToArray(), cancellationToken);
        var items = rows
            .Select(r => ToStoredRole(r, permissionNIdsByRole.TryGetValue(r.Id, out var nIds) ? nIds : []))
            .ToList();
        return new StoredRolePage(items, total);
    }

    /// <inheritdoc/>
    public async Task<StoredRole?> GetRoleAsync(string roleNId, CancellationToken cancellationToken)
    {
        var row = await QueryRoleRowByNIdAsync(roleNId, includeDeleted: false, cancellationToken);
        if (row is null)
        {
            return null;
        }

        var permissionNIds = await GetRolePermissionNIdsAsync(row.Id, cancellationToken);
        return ToStoredRole(row, permissionNIds);
    }

    /// <inheritdoc/>
    public async Task<Role?> GetRoleAggregateAsync(string roleNId, CancellationToken cancellationToken)
    {
        var row = await QueryRoleRowByNIdAsync(roleNId, includeDeleted: false, cancellationToken);
        if (row is null)
        {
            return null;
        }

        var loaded = await LoadRoleAggregatesAsync([row], cancellationToken);
        return loaded.Count == 0 ? null : loaded[0];
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<Role>> GetRolesByNIdsAsync(IReadOnlyCollection<string> roleNIds, CancellationToken cancellationToken)
    {
        if (roleNIds.Count == 0)
        {
            return [];
        }

        var normalizedNIds = roleNIds.Select(n => NId.Create(n).Normalized).Distinct(StringComparer.Ordinal).ToArray();
        var rows = await _dbContext.SqlSugar.Queryable<RoleTable>()
            .Where(t => normalizedNIds.Contains(t.NormalizedNId) && !t.IsDeleted)
            .ToListAsync(cancellationToken);
        return await LoadRoleAggregatesAsync(rows, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<Role>> GetRolesByIdsAsync(IReadOnlyCollection<Guid> roleIds, CancellationToken cancellationToken)
    {
        if (roleIds.Count == 0)
        {
            return [];
        }

        var rows = await _dbContext.SqlSugar.Queryable<RoleTable>()
            .Where(t => roleIds.Contains(t.Id) && !t.IsDeleted)
            .ToListAsync(cancellationToken);
        return await LoadRoleAggregatesAsync(rows, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyDictionary<Guid, string>> GetRoleNIdsAsync(IReadOnlyCollection<Guid> roleIds, CancellationToken cancellationToken)
        => await _roleRepository.GetNIdsAsync(roleIds, cancellationToken);

    /// <inheritdoc/>
    public async Task<IReadOnlyList<Permission>> GetPermissionsByNIdsAsync(IReadOnlyCollection<string> permissionNIds, CancellationToken cancellationToken)
    {
        if (permissionNIds.Count == 0)
        {
            return [];
        }

        var normalizedNIds = permissionNIds.Select(n => NId.Create(n).Normalized).Distinct(StringComparer.Ordinal).ToArray();
        var rows = await _dbContext.SqlSugar.Queryable<PermissionTable>()
            .Where(t => normalizedNIds.Contains(t.NormalizedNId) && !t.IsDeleted)
            .ToListAsync(cancellationToken);
        return rows.Select(TableMapper.ToPermission).ToList();
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<Permission>> GetPermissionsByIdsAsync(IReadOnlyCollection<Guid> permissionIds, CancellationToken cancellationToken)
    {
        if (permissionIds.Count == 0)
        {
            return [];
        }

        var rows = await _dbContext.SqlSugar.Queryable<PermissionTable>()
            .Where(t => permissionIds.Contains(t.Id) && !t.IsDeleted)
            .ToListAsync(cancellationToken);
        return rows.Select(TableMapper.ToPermission).ToList();
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<Permission>> GetAllPermissionsAsync(CancellationToken cancellationToken)
        => await _permissionRepository.GetAllAsync(cancellationToken);

    /// <inheritdoc/>
    /// <remarks>权威计数(§29A.3):有效持有者 = 直接分配 ∪ 用户组继承(组未删除且 Active)。</remarks>
    public async Task<int> CountActiveRoleHoldersAsync(Guid roleId, string tenantNId, CancellationToken cancellationToken)
    {
        var directIds = await _dbContext.SqlSugar.Queryable<UserRoleTable>()
            .Where(t => t.RoleId == roleId && !t.IsDeleted && !t.UserIsDeleted && !t.RoleIsDeleted)
            .InnerJoin<UserTable>((ur, u) => ur.UserId == u.Id && u.TenantNId == tenantNId && !u.IsDeleted && u.Status == UserStatus.Active)
            .Select((ur, u) => u.Id)
            .ToListAsync(cancellationToken);

        var groupIds = await _dbContext.SqlSugar.Queryable<UserGroupMembershipTable>()
            .InnerJoin<UserGroupTable>((m, g) => m.UserGroupId == g.Id && g.TenantNId == tenantNId && !g.IsDeleted && g.Status == UserGroupStatus.Active)
            .InnerJoin<UserGroupRoleTable>((m, g, r) => m.UserGroupId == r.UserGroupId && r.RoleId == roleId && !r.IsDeleted && !r.UserGroupIsDeleted && !r.RoleIsDeleted)
            .InnerJoin<UserTable>((m, g, r, u) => m.UserId == u.Id && u.TenantNId == tenantNId && !u.IsDeleted && u.Status == UserStatus.Active)
            .Where((m, g, r, u) => m.TenantNId == tenantNId && !m.IsDeleted && !m.UserIsDeleted && !m.UserGroupIsDeleted)
            .Select((m, g, r, u) => u.Id)
            .ToListAsync(cancellationToken);

        return directIds.Concat(groupIds).Distinct().Count();
    }

    /// <inheritdoc/>
    /// <remarks>有效持有者 = 直接分配 ∪ 用户组继承(§29A.2),供角色权限变化后的缓存失效。</remarks>
    public async Task<IReadOnlyList<string>> GetUserNIdsForRoleAsync(Guid roleId, string tenantNId, CancellationToken cancellationToken)
    {
        // 多表 OrderBy 存在 SqlSugar 别名限制(Join 别名须与 OrderBy 一致),统一取回后内存排序。
        var directNIds = await _dbContext.SqlSugar.Queryable<UserRoleTable>()
            .Where(t => t.RoleId == roleId && !t.IsDeleted && !t.UserIsDeleted && !t.RoleIsDeleted)
            .InnerJoin<UserTable>((ur, u) => ur.UserId == u.Id && u.TenantNId == tenantNId && !u.IsDeleted)
            .Select((ur, u) => u.NId)
            .ToListAsync(cancellationToken);

        var groupNIds = await _dbContext.SqlSugar.Queryable<UserGroupMembershipTable>()
            .InnerJoin<UserGroupTable>((m, g) => m.UserGroupId == g.Id && g.TenantNId == tenantNId && !g.IsDeleted && g.Status == UserGroupStatus.Active)
            .InnerJoin<UserGroupRoleTable>((m, g, r) => m.UserGroupId == r.UserGroupId && r.RoleId == roleId && !r.IsDeleted && !r.UserGroupIsDeleted && !r.RoleIsDeleted)
            .InnerJoin<UserTable>((m, g, r, u) => m.UserId == u.Id && u.TenantNId == tenantNId && !u.IsDeleted)
            .Where((m, g, r, u) => m.TenantNId == tenantNId && !m.IsDeleted && !m.UserIsDeleted && !m.UserGroupIsDeleted)
            .Select((m, g, r, u) => u.NId)
            .ToListAsync(cancellationToken);

        return directNIds.Concat(groupNIds)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();
    }

    /// <inheritdoc/>
    public Task AddUserAsync(User user, IReadOnlyCollection<OutboxEnvelope> outboxEvents, CancellationToken cancellationToken)
        => _userRepository.AddAsync(user, outboxEvents, cancellationToken);

    /// <inheritdoc/>
    public Task UpdateUserAsync(
        User user,
        long expectedOptimisticVersion,
        Guid expectedConcurrencyVersion,
        IReadOnlyCollection<OutboxEnvelope> outboxEvents,
        CancellationToken cancellationToken)
        => _userRepository.UpdateAsync(user, expectedOptimisticVersion, expectedConcurrencyVersion, outboxEvents, cancellationToken);

    /// <inheritdoc/>
    public Task AddRoleAsync(Role role, IReadOnlyCollection<OutboxEnvelope> outboxEvents, CancellationToken cancellationToken)
        => _roleRepository.AddAsync(role, outboxEvents, cancellationToken);

    /// <inheritdoc/>
    public Task UpdateRoleAsync(
        Role role,
        long expectedOptimisticVersion,
        Guid expectedConcurrencyVersion,
        IReadOnlyCollection<OutboxEnvelope> outboxEvents,
        CancellationToken cancellationToken)
        => _roleRepository.UpdateAsync(role, expectedOptimisticVersion, expectedConcurrencyVersion, outboxEvents, cancellationToken);

    /// <inheritdoc/>
    public async Task<bool> RoleExistsByNIdAsync(string roleNId, CancellationToken cancellationToken)
    {
        var normalized = NId.Create(roleNId).Normalized;
        return await _dbContext.SqlSugar.Queryable<RoleTable>()
            .Where(t => t.NormalizedNId == normalized)
            .AnyAsync(cancellationToken);
    }

    private async Task<UserTable?> QueryUserRowByNIdAsync(string userNId, bool includeDeleted, CancellationToken cancellationToken)
    {
        var normalized = NId.Create(userNId).Normalized;
        var query = _dbContext.SqlSugar.Queryable<UserTable>().Where(t => t.NormalizedNId == normalized);
        if (!includeDeleted)
        {
            query = query.Where(t => !t.IsDeleted);
        }

        return await query.FirstAsync(cancellationToken);
    }

    private async Task<RoleTable?> QueryRoleRowByNIdAsync(string roleNId, bool includeDeleted, CancellationToken cancellationToken)
    {
        var normalized = NId.Create(roleNId).Normalized;
        var query = _dbContext.SqlSugar.Queryable<RoleTable>().Where(t => t.NormalizedNId == normalized);
        if (!includeDeleted)
        {
            query = query.Where(t => !t.IsDeleted);
        }

        return await query.FirstAsync(cancellationToken);
    }

    /// <summary>批量装载角色聚合(含活动权限关系,双重过滤),按传入行顺序返回。</summary>
    private async Task<IReadOnlyList<Role>> LoadRoleAggregatesAsync(List<RoleTable> rows, CancellationToken cancellationToken)
    {
        if (rows.Count == 0)
        {
            return [];
        }

        var roleIds = rows.Select(r => r.Id).ToArray();
        var permissionRows = await _dbContext.SqlSugar.Queryable<RolePermissionTable>()
            .Where(t => roleIds.Contains(t.RoleId) && !t.IsDeleted && !t.RoleIsDeleted)
            .OrderBy(t => t.Id)
            .ToListAsync(cancellationToken);
        var byRole = permissionRows
            .GroupBy(p => p.RoleId)
            .ToDictionary(g => g.Key, g => g.Select(TableMapper.ToRolePermission).ToList());
        return rows
            .Select(r => TableMapper.ToRole(r, byRole.TryGetValue(r.Id, out var perms) ? perms : []))
            .ToList();
    }

    /// <summary>批量查询用户活动角色的 NId 映射(角色自身与父级均未删除),键=用户 Id。</summary>
    private async Task<Dictionary<Guid, List<string>>> BuildUserRoleNIdsMapAsync(Guid[] userIds, CancellationToken cancellationToken)
    {
        var map = new Dictionary<Guid, List<string>>();
        if (userIds.Length == 0)
        {
            return map;
        }

        var roleRows = await _dbContext.SqlSugar.Queryable<UserRoleTable>()
            .Where(t => userIds.Contains(t.UserId) && !t.IsDeleted && !t.UserIsDeleted && !t.RoleIsDeleted)
            .ToListAsync(cancellationToken);
        if (roleRows.Count == 0)
        {
            return map;
        }

        var roleIds = roleRows.Select(r => r.RoleId).Distinct().ToArray();
        var roleNIds = roleIds.Length == 0
            ? new Dictionary<Guid, string>()
            : await _roleRepository.GetNIdsAsync(roleIds, cancellationToken);

        foreach (var group in roleRows.GroupBy(r => r.UserId))
        {
            var nIds = group
                .Select(r => roleNIds.TryGetValue(r.RoleId, out var n) ? n : null)
                .Where(n => n is not null)
                .Select(n => n!)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(n => n, StringComparer.Ordinal)
                .ToList();
            map[group.Key] = nIds;
        }

        return map;
    }

    /// <summary>批量查询角色活动权限的 NId 映射(权限自身与父级均未删除),键=角色 Id。</summary>
    private async Task<Dictionary<Guid, List<string>>> BuildRolePermissionNIdsMapAsync(Guid[] roleIds, CancellationToken cancellationToken)
    {
        var map = new Dictionary<Guid, List<string>>();
        if (roleIds.Length == 0)
        {
            return map;
        }

        var permissionRows = await _dbContext.SqlSugar.Queryable<RolePermissionTable>()
            .Where(t => roleIds.Contains(t.RoleId) && !t.IsDeleted && !t.RoleIsDeleted && !t.PermissionIsDeleted)
            .ToListAsync(cancellationToken);
        if (permissionRows.Count == 0)
        {
            return map;
        }

        var permissionIds = permissionRows.Select(r => r.PermissionId).Distinct().ToArray();
        var permissionRowsById = await _dbContext.SqlSugar.Queryable<PermissionTable>()
            .Where(t => permissionIds.Contains(t.Id) && !t.IsDeleted)
            .Select(t => new { t.Id, t.NId })
            .ToListAsync(cancellationToken);
        var idToNId = permissionRowsById.ToDictionary(p => p.Id, p => p.NId);

        foreach (var group in permissionRows.GroupBy(r => r.RoleId))
        {
            var nIds = group
                .Select(r => idToNId.TryGetValue(r.PermissionId, out var n) ? n : null)
                .Where(n => n is not null)
                .Select(n => n!)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(n => n, StringComparer.Ordinal)
                .ToList();
            map[group.Key] = nIds;
        }

        return map;
    }

    private async Task<IReadOnlyList<string>> GetUserRoleNIdsAsync(Guid userId, CancellationToken cancellationToken)
    {
        var roleIds = await _dbContext.SqlSugar.Queryable<UserRoleTable>()
            .Where(t => t.UserId == userId && !t.IsDeleted && !t.UserIsDeleted && !t.RoleIsDeleted)
            .Select(t => t.RoleId)
            .ToListAsync(cancellationToken);
        if (roleIds.Count == 0)
        {
            return [];
        }

        var roleNIds = await _roleRepository.GetNIdsAsync(roleIds, cancellationToken);
        return roleNIds.Values.Distinct(StringComparer.Ordinal).OrderBy(v => v, StringComparer.Ordinal).ToList();
    }

    private async Task<IReadOnlyList<string>> GetRolePermissionNIdsAsync(Guid roleId, CancellationToken cancellationToken)
    {
        var permissionIds = await _dbContext.SqlSugar.Queryable<RolePermissionTable>()
            .Where(t => t.RoleId == roleId && !t.IsDeleted && !t.RoleIsDeleted && !t.PermissionIsDeleted)
            .Select(t => t.PermissionId)
            .ToListAsync(cancellationToken);
        if (permissionIds.Count == 0)
        {
            return [];
        }

        var nIds = await _dbContext.SqlSugar.Queryable<PermissionTable>()
            .Where(t => permissionIds.Contains(t.Id) && !t.IsDeleted)
            .Select(t => t.NId)
            .ToListAsync(cancellationToken);
        return nIds.OrderBy(n => n, StringComparer.Ordinal).ToList();
    }

    private static StoredUser ToStoredUser(UserTable row, IReadOnlyList<string> roleNIds) => new(
        row.Id,
        row.TenantNId,
        row.NId,
        row.LoginName,
        row.Name,
        row.Email,
        row.Phone,
        row.Status,
        row.CreatedOn,
        row.LastUpdatedOn,
        row.LastLoginOn,
        row.OptimisticVersion,
        row.ConcurrencyVersion,
        roleNIds);

    private static StoredRole ToStoredRole(RoleTable row, IReadOnlyList<string> permissionNIds) => new(
        row.Id,
        row.TenantNId,
        row.NId,
        row.Name,
        row.Description,
        row.IsSystem,
        row.CreatedOn,
        row.LastUpdatedOn,
        row.OptimisticVersion,
        row.ConcurrencyVersion,
        permissionNIds);
}
