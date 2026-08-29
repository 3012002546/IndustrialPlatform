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
using IndustrialPlatform.Infrastructure.Querying;
using IndustrialPlatform.Querying.Descriptors;
using SqlSugar;

namespace IndustrialPlatform.Identity.Infrastructure.Management;

/// <summary>
/// 管理用例持久化端口实现(§16):组合领域仓储并补充分页/关系投影/持有者统计查询。
/// 按 NId 查询不区分租户,租户隔离由应用层显式校验;含软删除的唯一性检查供 NId 防复用。
/// </summary>
public sealed class ManagementStore : IManagementStore, IUserQueryStore
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
            .Where(t => t.TenantNId == filter.TenantNId && (filter.IncludeDeleted || !t.IsDeleted));

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

        if (!string.IsNullOrWhiteSpace(filter.Keyword))
        {
            var keyword = filter.Keyword.Trim();
            query = query.Where(t => t.NId.Contains(keyword) || t.LoginName.Contains(keyword) || t.Name.Contains(keyword) || (t.Email != null && t.Email.Contains(keyword)) || (t.Phone != null && t.Phone.Contains(keyword)));
        }

        if (!string.IsNullOrWhiteSpace(filter.Email)) query = query.Where(t => t.Email != null && t.Email.Contains(filter.Email.Trim()));
        if (!string.IsNullOrWhiteSpace(filter.Phone)) query = query.Where(t => t.Phone != null && t.Phone.Contains(filter.Phone.Trim()));
        if (filter.MustChangePassword is { } mustChangePassword) query = query.Where(t => t.MustChangePassword == mustChangePassword);
        if (filter.LastLoginFrom is { } lastLoginFrom) query = query.Where(t => t.LastLoginOn >= lastLoginFrom);
        if (filter.LastLoginTo is { } lastLoginTo) query = query.Where(t => t.LastLoginOn <= lastLoginTo);
        if (filter.CreatedFrom is { } createdFrom) query = query.Where(t => t.CreatedOn >= createdFrom);
        if (filter.CreatedTo is { } createdTo) query = query.Where(t => t.CreatedOn <= createdTo);

        // §29A.5:按有效成员过滤(用户组)
        if (!string.IsNullOrWhiteSpace(filter.GroupNId))
        {
            var group = await FindGroupByNIdAsync(filter.GroupNId, cancellationToken);
            if (group is null)
            {
                return new StoredUserPage([], 0);
            }

            var memberUserIds = await _dbContext.SqlSugar.Queryable<UserGroupMembershipTable>()
                .Where(m => m.UserGroupId == group.Id && !m.IsDeleted && !m.UserGroupIsDeleted)
                .Select(m => m.UserId)
                .ToListAsync(cancellationToken);
            query = query.Where(t => memberUserIds.Contains(t.Id));
        }

        // §29A.5:按直接角色过滤
        if (!string.IsNullOrWhiteSpace(filter.RoleNId))
        {
            var role = await FindRoleByNIdAsync(filter.RoleNId, cancellationToken);
            if (role is null)
            {
                return new StoredUserPage([], 0);
            }

            var directUserIds = await _dbContext.SqlSugar.Queryable<UserRoleTable>()
                .Where(ur => ur.RoleId == role.Id && !ur.IsDeleted && !ur.UserIsDeleted && !ur.RoleIsDeleted)
                .Select(ur => ur.UserId)
                .ToListAsync(cancellationToken);
            query = query.Where(t => directUserIds.Contains(t.Id));
        }

        var total = await query.CountAsync(cancellationToken);
        var orderedQuery = filter.SortField?.ToLowerInvariant() switch
        {
            "nid" => filter.SortOrder == "desc" ? query.OrderByDescending(t => t.NId) : query.OrderBy(t => t.NId),
            "loginname" => filter.SortOrder == "desc" ? query.OrderByDescending(t => t.LoginName) : query.OrderBy(t => t.LoginName),
            "name" => filter.SortOrder == "desc" ? query.OrderByDescending(t => t.Name) : query.OrderBy(t => t.Name),
            "status" => filter.SortOrder == "desc" ? query.OrderByDescending(t => t.Status) : query.OrderBy(t => t.Status),
            "lastloginon" => filter.SortOrder == "desc" ? query.OrderByDescending(t => t.LastLoginOn) : query.OrderBy(t => t.LastLoginOn),
            _ => query.OrderBy(t => t.CreatedOn, OrderByType.Desc),
        };
        var rows = await orderedQuery
            .Skip((filter.PageIndex - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .ToListAsync(cancellationToken);

        var roleSource = await BuildUserRoleSourceMapAsync(rows.Select(r => r.Id).ToArray(), filter.TenantNId, cancellationToken);
        var items = rows
            .Select(r => ToStoredUser(r, roleSource.TryGetValue(r.Id, out var source) ? source : RoleSource.Empty))
            .ToList();
        return new StoredUserPage(items, total);
    }

    /// <inheritdoc/>
    public async Task<StoredUserPage> QueryUsersAsync(
        string tenantNId,
        QueryDescriptor descriptor,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantNId);
        ArgumentNullException.ThrowIfNull(descriptor);
        var map = new SqlSugarQueryFieldMap<UserTable>(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["userNId"] = "n_id",
                ["loginName"] = "login_name",
                ["name"] = "name",
                ["email"] = "email",
                ["phone"] = "phone",
                ["status"] = "status",
                ["createdOn"] = "created_on",
                ["lastLoginOn"] = "last_login_on",
                ["mustChangePassword"] = "must_change_password",
            },
            tieBreaker: "userNId");
        var plan = SqlSugarQueryAdapter.BuildPlan(descriptor, map);
        var query = _dbContext.SqlSugar.Queryable<UserTable>()
            .Where(row => row.TenantNId == tenantNId && !row.IsDeleted);
        if (plan.FilterSql.Count > 0)
        {
            query = query.Where(string.Join(" AND ", plan.FilterSql), plan.Parameters);
        }

        var total = await query.CountAsync(cancellationToken);
        var rows = await query
            .OrderBy(string.Join(", ", plan.OrderBySql))
            .Skip(plan.Skip)
            .Take(plan.PageSize)
            .ToListAsync(cancellationToken);
        var roleSource = await BuildUserRoleSourceMapAsync(
            rows.Select(row => row.Id).ToArray(),
            tenantNId,
            cancellationToken);
        var items = rows
            .Select(row => ToStoredUser(
                row,
                roleSource.TryGetValue(row.Id, out var source) ? source : RoleSource.Empty))
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

        var roleSource = await BuildUserRoleSourceMapAsync([row.Id], row.TenantNId, cancellationToken);
        return ToStoredUser(row, roleSource.TryGetValue(row.Id, out var source) ? source : RoleSource.Empty);
    }

    /// <inheritdoc/>
    public async Task<User?> GetUserAggregateAsync(string userNId, CancellationToken cancellationToken)
    {
        var row = await QueryUserRowByNIdAsync(userNId, includeDeleted: false, cancellationToken);
        return row is null ? null : await _userRepository.GetByIdAsync(row.Id, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<User?> GetUserAggregateIncludingDeletedAsync(string userNId, CancellationToken cancellationToken)
    {
        var row = await QueryUserRowByNIdAsync(userNId, includeDeleted: true, cancellationToken);
        return row is null ? null : await _userRepository.GetByNIdIncludingDeletedAsync(userNId, cancellationToken);
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

        if (!string.IsNullOrWhiteSpace(filter.Keyword))
        {
            var keyword = filter.Keyword.Trim();
            query = query.Where(t => t.NId.Contains(keyword) || t.Name.Contains(keyword) || (t.Description != null && t.Description.Contains(keyword)));
        }
        if (!string.IsNullOrWhiteSpace(filter.Description)) query = query.Where(t => t.Description != null && t.Description.Contains(filter.Description.Trim()));
        if (filter.IsSystem is { } isSystem) query = query.Where(t => t.IsSystem == isSystem);

        var total = await query.CountAsync(cancellationToken);
        var orderedQuery = filter.SortField?.ToLowerInvariant() switch
        {
            "nid" => filter.SortOrder == "desc" ? query.OrderByDescending(t => t.NId) : query.OrderBy(t => t.NId),
            "name" => filter.SortOrder == "desc" ? query.OrderByDescending(t => t.Name) : query.OrderBy(t => t.Name),
            _ => query.OrderBy(t => t.CreatedOn, OrderByType.Desc),
        };
        var rows = await orderedQuery
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
    public Task RestoreUserAsync(
        User user,
        long expectedOptimisticVersion,
        Guid expectedConcurrencyVersion,
        IReadOnlyCollection<OutboxEnvelope> outboxEvents,
        CancellationToken cancellationToken)
        => _userRepository.RestoreAsync(user, expectedOptimisticVersion, expectedConcurrencyVersion, outboxEvents, cancellationToken);

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

    private static StoredUser ToStoredUser(UserTable row, RoleSource roleSource) => new(
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
        row.MustChangePassword,
        roleSource.DirectRoleNIds,
        roleSource.GroupRoleNIds,
        roleSource.EffectiveRoleNIds,
        row.OptimisticVersion,
        row.ConcurrencyVersion,
        row.IsDeleted);

    /// <summary>用户角色来源摘要(§29A.5):直接 / 组继承 / 有效并集。</summary>
    private sealed record RoleSource(
        IReadOnlyList<string> DirectRoleNIds,
        IReadOnlyList<string> GroupRoleNIds,
        IReadOnlyList<string> EffectiveRoleNIds)
    {
        public static RoleSource Empty { get; } = new([], [], []);
    }

    /// <summary>批量查询用户的角色来源摘要(直接分配 ∪ 活动用户组继承,§29A.5)。</summary>
    private async Task<Dictionary<Guid, RoleSource>> BuildUserRoleSourceMapAsync(
        Guid[] userIds,
        string tenantNId,
        CancellationToken cancellationToken)
    {
        var map = new Dictionary<Guid, RoleSource>();
        if (userIds.Length == 0)
        {
            return map;
        }

        // 直接分配角色
        var directByUser = await BuildUserRoleNIdsMapAsync(userIds, cancellationToken);

        // 组继承角色:membership → group-role → role NId
        var groupRoleNIdsByUser = await BuildInheritedRoleNIdsMapAsync(userIds, tenantNId, cancellationToken);

        foreach (var userId in userIds)
        {
            var direct = directByUser.TryGetValue(userId, out var d) ? d : [];
            var inherited = groupRoleNIdsByUser.TryGetValue(userId, out var g) ? g : [];
            var effective = direct.Concat(inherited).Distinct(StringComparer.Ordinal).OrderBy(n => n, StringComparer.Ordinal).ToList();
            map[userId] = new RoleSource(
                direct.OrderBy(n => n, StringComparer.Ordinal).ToList(),
                inherited,
                effective);
        }

        return map;
    }

    /// <summary>批量查询用户经活动用户组继承的角色 NId 映射(组/成员/组角色均未删除)。</summary>
    private async Task<Dictionary<Guid, List<string>>> BuildInheritedRoleNIdsMapAsync(
        Guid[] userIds,
        string tenantNId,
        CancellationToken cancellationToken)
    {
        var map = new Dictionary<Guid, List<string>>();
        if (userIds.Length == 0)
        {
            return map;
        }

        var memberships = await _dbContext.SqlSugar.Queryable<UserGroupMembershipTable>()
            .Where(m => userIds.Contains(m.UserId) && !m.IsDeleted && !m.UserGroupIsDeleted)
            .ToListAsync(cancellationToken);
        if (memberships.Count == 0)
        {
            return map;
        }

        var groupIds = memberships.Select(m => m.UserGroupId).Distinct().ToArray();
        var activeGroups = await _dbContext.SqlSugar.Queryable<UserGroupTable>()
            .Where(g => groupIds.Contains(g.Id) && !g.IsDeleted && g.TenantNId == tenantNId)
            .Select(g => g.Id)
            .ToListAsync(cancellationToken);
        if (activeGroups.Count == 0)
        {
            return map;
        }

        var groupRoles = await _dbContext.SqlSugar.Queryable<UserGroupRoleTable>()
            .Where(gr => activeGroups.Contains(gr.UserGroupId) && !gr.IsDeleted && !gr.UserGroupIsDeleted && !gr.RoleIsDeleted)
            .ToListAsync(cancellationToken);
        if (groupRoles.Count == 0)
        {
            return map;
        }

        var roleIds = groupRoles.Select(r => r.RoleId).Distinct().ToArray();
        var roleNIdByRoleId = roleIds.Length == 0
            ? new Dictionary<Guid, string>()
            : await _roleRepository.GetNIdsAsync(roleIds, cancellationToken);

        // 按成员行的组逐条收集继承角色
        var activeGroupIdSet = activeGroups.ToHashSet();
        foreach (var group in memberships.Where(m => activeGroupIdSet.Contains(m.UserGroupId)).GroupBy(m => m.UserId))
        {
            var memberGroupIds = group.Select(m => m.UserGroupId).ToHashSet();
            var nIds = groupRoles
                .Where(gr => memberGroupIds.Contains(gr.UserGroupId))
                .Select(gr => roleNIdByRoleId.TryGetValue(gr.RoleId, out var n) ? n : null)
                .Where(n => n is not null)
                .Select(n => n!)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(n => n, StringComparer.Ordinal)
                .ToList();
            map[group.Key] = nIds;
        }

        return map;
    }

    private async Task<UserGroupTable?> FindGroupByNIdAsync(string groupNId, CancellationToken cancellationToken)
    {
        var normalized = NId.Create(groupNId).Normalized;
        return await _dbContext.SqlSugar.Queryable<UserGroupTable>()
            .Where(g => g.NormalizedNId == normalized && !g.IsDeleted)
            .FirstAsync(cancellationToken);
    }

    private async Task<RoleTable?> FindRoleByNIdAsync(string roleNId, CancellationToken cancellationToken)
    {
        var normalized = NId.Create(roleNId).Normalized;
        return await _dbContext.SqlSugar.Queryable<RoleTable>()
            .Where(r => r.NormalizedNId == normalized && !r.IsDeleted)
            .FirstAsync(cancellationToken);
    }

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
