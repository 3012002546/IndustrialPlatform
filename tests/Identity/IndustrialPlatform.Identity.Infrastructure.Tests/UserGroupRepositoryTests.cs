using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IndustrialPlatform.Identity.Application.Management;
using IndustrialPlatform.Identity.Domain.Roles;
using IndustrialPlatform.Identity.Domain.Users;
using IndustrialPlatform.Identity.Domain.UserGroups;
using IndustrialPlatform.Identity.Infrastructure.Persistence.Migrations;
using IndustrialPlatform.Identity.Infrastructure.Persistence.Repositories;
using IndustrialPlatform.Infrastructure.Database;
using IndustrialPlatform.SharedKernel.Exceptions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SqlSugar;
using SQLitePCL;
using Xunit;

namespace IndustrialPlatform.Identity.Infrastructure.Tests;

/// <summary>
/// 用户组仓储与迁移集成测试(§29A.2/§11):三张新表创建、复合外键与部分唯一索引、
/// 聚合往返、关系 diff、Outbox 原子写入与组继承角色求值。SQLite 模拟;
/// (PostgreSQL 真实验证标记「待验收」。)
/// </summary>
[Collection(BootstrapEnvironmentTestGroup.Name)]
public sealed class UserGroupRepositoryTests : IDisposable
{
    private const string TenantNId = "group-test";
    private const string PasswordHash = "$2a$12$abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123";

    private const string InsertMembershipSql = """
        INSERT INTO identity_user_group_membership
          (id, is_frozen, is_locked, is_deleted, entity_type, created_on, last_updated_on,
           optimistic_version, concurrency_version,
           tenant_n_id, user_group_id, user_group_is_deleted, user_id, user_is_deleted)
        VALUES
          (@id, 0, 0, 0, @entityType, @createdOn, @lastUpdatedOn, 0, @concurrencyVersion,
           @tenantNId, @userGroupId, @userGroupIsDeleted, @userId, @userIsDeleted)
        """;

    static UserGroupRepositoryTests()
    {
        SQLitePCL.Batteries_V2.Init();
    }

    private readonly string _dbPath;
    private readonly SqlSugarDbContext _dbContext;
    private readonly UserGroupRepository _groups;
    private readonly UserRepository _users;
    private readonly RoleRepository _roles;
    private readonly PermissionRepository _permissions;

    public UserGroupRepositoryTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"industrial-platform-user-group-{Guid.NewGuid():N}.db");
        _dbContext = new SqlSugarDbContext(Options.Create(new SqlSugarOptions
        {
            ConnectionString = $"Data Source={_dbPath};Foreign Keys=True",
            DbType = DbType.Sqlite,
        }));

        new SchemaMigrationRunner(_dbContext, IdentitySchemaMigrations.All, NullLogger<SchemaMigrationRunner>.Instance)
            .ApplyPendingAsync()
            .GetAwaiter()
            .GetResult();

        _groups = new UserGroupRepository(_dbContext);
        _users = new UserRepository(_dbContext);
        _roles = new RoleRepository(_dbContext);
        _permissions = new PermissionRepository(_dbContext);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        try
        {
            if (File.Exists(_dbPath))
            {
                File.Delete(_dbPath);
            }
        }
        catch (IOException)
        {
            // SqlSugarScope 连接池可能短暂占用文件句柄,忽略清理失败。
        }
    }

    private async Task<User> SeedUserAsync(string nId, string loginName)
    {
        var user = User.Create(TenantNId, nId, loginName, nId, null, null, PasswordHash);
        await _users.AddAsync(user, CancellationToken.None);
        return user;
    }

    private async Task<Role> SeedRoleAsync(string nId, bool isSystem = false)
    {
        var role = Role.Create(TenantNId, nId, nId, null, isSystem);
        await _roles.AddAsync(role, CancellationToken.None);
        return role;
    }

    private async Task<UserGroup> SeedGroupAsync(
        string nId = "group.ops",
        IReadOnlyCollection<User>? members = null,
        IReadOnlyCollection<Role>? roles = null)
    {
        var group = UserGroup.Create(TenantNId, nId, nId, null);
        foreach (var member in members ?? [])
        {
            group.AssignMember(member);
        }

        foreach (var role in roles ?? [])
        {
            group.AssignRole(role);
        }

        await _groups.AddAsync(group, [], CancellationToken.None);
        return group;
    }

    private async Task<int> CountAsync(string sql, params SugarParameter[] parameters) =>
        await _dbContext.SqlSugar.Ado.GetIntAsync(sql, parameters);

    // ---- 迁移 ----

    [Fact]
    public async Task ApplyPending_CreatesUserGroupTablesAndConstraints()
    {
        // 三张新表(sqlite_master 直查,避免 DbMaintenance 陈旧缓存)
        foreach (var table in new[] { "identity_user_group", "identity_user_group_membership", "identity_user_group_role" })
        {
            Assert.Equal(1, await CountAsync(
                "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = @name",
                new SugarParameter("@name", table)));
        }

        // 部分唯一索引(活动关系唯一)
        Assert.Equal(1, await CountAsync(
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'index' AND name = 'ux_user_group_membership_active'"));
        Assert.Equal(1, await CountAsync(
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'index' AND name = 'ux_user_group_role_active'"));

        // 复合外键(成员用户 → identity_user,禁止嵌套由 user_id 外键保证);
        // 复合外键按列展开,按 FK id 去重计数
        var fkCount = await CountAsync(
            "SELECT COUNT(DISTINCT id) FROM pragma_foreign_key_list('identity_user_group_membership') WHERE \"table\" IN ('identity_user_group', 'identity_user')");
        Assert.Equal(2, fkCount);
        var roleFkCount = await CountAsync(
            "SELECT COUNT(DISTINCT id) FROM pragma_foreign_key_list('identity_user_group_role') WHERE \"table\" IN ('identity_user_group', 'identity_role')");
        Assert.Equal(2, roleFkCount);
    }

    [Fact]
    public async Task ApplyPending_RunTwice_IsIdempotent()
    {
        await new SchemaMigrationRunner(_dbContext, IdentitySchemaMigrations.All, NullLogger<SchemaMigrationRunner>.Instance)
            .ApplyPendingAsync();
        await new SchemaMigrationRunner(_dbContext, IdentitySchemaMigrations.All, NullLogger<SchemaMigrationRunner>.Instance)
            .ApplyPendingAsync();

        // 第二次执行不产生重复账本记录
        Assert.Equal(1, await CountAsync(
            "SELECT COUNT(*) FROM identity_schema_migrations WHERE migration_id = 'ID-017-01'"));
    }

    // ---- 聚合往返 ----

    [Fact]
    public async Task AddAsync_PersistsGroupWithMembershipsAndRoles_RoundTrips()
    {
        var user = await SeedUserAsync("user.alice", "alice");
        var role = await SeedRoleAsync("role.editor");
        var group = await SeedGroupAsync("group.ops", [user], [role]);

        var loaded = await _groups.GetByNIdAsync("group.ops", CancellationToken.None);

        Assert.NotNull(loaded);
        Assert.Equal(group.Id, loaded!.Id);
        Assert.Equal(TenantNId, loaded.TenantNId);
        Assert.Equal("GROUP.OPS", loaded.NormalizedNId);
        Assert.Equal(UserGroupStatus.Active, loaded.Status);
        var membership = Assert.Single(loaded.Memberships);
        Assert.Equal(user.Id, membership.UserId);
        Assert.False(membership.IsDeleted);
        Assert.False(membership.UserIsDeleted);
        var groupRole = Assert.Single(loaded.Roles);
        Assert.Equal(role.Id, groupRole.RoleId);
        Assert.False(groupRole.IsDeleted);
    }

    [Fact]
    public async Task GetByNIdAsync_ExcludesDeletedGroup()
    {
        var group = await SeedGroupAsync();

        await _groups.DeleteAsync(group, group.OptimisticVersion, group.ConcurrencyVersion, CancellationToken.None);

        Assert.Null(await _groups.GetByNIdAsync("group.ops", CancellationToken.None));
    }

    [Fact]
    public async Task NIdUniqueness_GroupNormalizedNId_IsUniqueInTenant()
    {
        await SeedGroupAsync("group.ops");

        var duplicate = UserGroup.Create(TenantNId, "GROUP.OPS", "重复", null);
        await Assert.ThrowsAnyAsync<Exception>(() =>
            _groups.AddAsync(duplicate, [], CancellationToken.None));
    }

    // ---- 关系 diff ----

    [Fact]
    public async Task UpdateAsync_SyncsMembershipDiff()
    {
        var alice = await SeedUserAsync("user.alice", "alice");
        var bob = await SeedUserAsync("user.bob", "bob");
        var group = await SeedGroupAsync("group.ops", [alice], []);

        var loaded = await _groups.GetByNIdAsync("group.ops", CancellationToken.None);
        Assert.NotNull(loaded);
        var version = loaded!.OptimisticVersion;
        var concurrency = loaded.ConcurrencyVersion;

        // 最终成员集:移除 alice、加入 bob(幂等收敛)
        loaded.RemoveMember(alice);
        loaded.AssignMember(bob);
        await _groups.UpdateAsync(loaded, version, concurrency, [], CancellationToken.None);

        var after = await _groups.GetByNIdAsync("group.ops", CancellationToken.None);
        Assert.NotNull(after);
        // 仓储只载入活动关系;被移除的 alice 关系在库中保留为软删除行
        var activeMembers = after!.Memberships.ToList();
        Assert.Single(activeMembers);
        Assert.Equal(bob.Id, activeMembers[0].UserId);
        Assert.Equal(1, await CountAsync(
            "SELECT is_deleted FROM identity_user_group_membership WHERE user_id = @userId",
            new SugarParameter("@userId", alice.Id.ToString())));
    }

    [Fact]
    public async Task UpdateAsync_SyncsRoleDiff()
    {
        var editor = await SeedRoleAsync("role.editor");
        var viewer = await SeedRoleAsync("role.viewer");
        var group = await SeedGroupAsync("group.ops", [], [editor]);

        var loaded = await _groups.GetByNIdAsync("group.ops", CancellationToken.None);
        Assert.NotNull(loaded);
        var version = loaded!.OptimisticVersion;
        var concurrency = loaded.ConcurrencyVersion;

        loaded.RemoveRole(editor);
        loaded.AssignRole(viewer);
        await _groups.UpdateAsync(loaded, version, concurrency, [], CancellationToken.None);

        var after = await _groups.GetByNIdAsync("group.ops", CancellationToken.None);
        Assert.NotNull(after);
        var activeRoles = after!.Roles.Where(r => !r.IsDeleted && !r.RoleIsDeleted).ToList();
        Assert.Single(activeRoles);
        Assert.Equal(viewer.Id, activeRoles[0].RoleId);
    }

    [Fact]
    public async Task UpdateAsync_StaleVersion_ThrowsConcurrency()
    {
        var group = await SeedGroupAsync();

        var loaded = await _groups.GetByNIdAsync("group.ops", CancellationToken.None);
        Assert.NotNull(loaded);
        loaded.ChangeProfile("新名称", null);

        await Assert.ThrowsAsync<ConcurrencyException>(() =>
            _groups.UpdateAsync(loaded, 999, Guid.NewGuid(), [], CancellationToken.None));
    }

    [Fact]
    public async Task UpdateAsync_CommitsOutboxAtomically()
    {
        var alice = await SeedUserAsync("user.alice", "alice");
        var group = await SeedGroupAsync("group.ops", [alice], []);

        var loaded = await _groups.GetByNIdAsync("group.ops", CancellationToken.None);
        Assert.NotNull(loaded);
        var version = loaded!.OptimisticVersion;
        var concurrency = loaded.ConcurrencyVersion;

        loaded.RemoveMember(alice);
        var envelope = new OutboxEnvelope(
            Guid.NewGuid(),
            "Identity.UserGroupMembershipChanged.v1",
            1,
            "{}",
            DateTimeOffset.UtcNow);
        await _groups.UpdateAsync(loaded, version, concurrency, [envelope], CancellationToken.None);

        Assert.Equal(1, await CountAsync("SELECT COUNT(*) FROM identity_outbox WHERE event_id = @eventId",
            new SugarParameter("@eventId", envelope.EventId.ToString())));
    }

    [Fact]
    public async Task UpdateAsync_ConcurrencyFailure_RollsBackOutbox()
    {
        var alice = await SeedUserAsync("user.alice", "alice");
        var group = await SeedGroupAsync("group.ops", [alice], []);

        var loaded = await _groups.GetByNIdAsync("group.ops", CancellationToken.None);
        Assert.NotNull(loaded);
        loaded.RemoveMember(alice);
        var envelope = new OutboxEnvelope(
            Guid.NewGuid(),
            "Identity.UserGroupMembershipChanged.v1",
            1,
            "{}",
            DateTimeOffset.UtcNow);

        // 陈旧版本 → 更新失败 → 事务回滚 → Outbox 不落库
        await Assert.ThrowsAsync<ConcurrencyException>(() =>
            _groups.UpdateAsync(loaded, 999, Guid.NewGuid(), [envelope], CancellationToken.None));

        Assert.Equal(0, await CountAsync("SELECT COUNT(*) FROM identity_outbox WHERE event_id = @eventId",
            new SugarParameter("@eventId", envelope.EventId.ToString())));
    }

    // ---- 数据库约束 ----

    [Fact]
    public async Task MismatchedParentShadow_IsRejected_ByCompositeForeignKey()
    {
        var alice = await SeedUserAsync("user.alice", "alice");
        var group = await SeedGroupAsync("group.ops", [], []);

        // 用户当前 (id, is_deleted) = (alice.Id, false);子行 user_is_deleted=1 无父级可引用 → 外键拒绝
        await Assert.ThrowsAnyAsync<Exception>(() =>
            _dbContext.SqlSugar.Ado.ExecuteCommandAsync(
                InsertMembershipSql,
                MembershipParams(group.Id, alice.Id, userIsDeleted: 1)));
    }

    [Fact]
    public async Task NoNesting_GroupIdAsMemberUserId_IsRejected_ByForeignKey()
    {
        var group = await SeedGroupAsync("group.ops", [], []);

        // 成员侧 user_id 只能引用 identity_user;传入用户组主键 → 外键拒绝(禁止嵌套的数据库保证)
        await Assert.ThrowsAnyAsync<Exception>(() =>
            _dbContext.SqlSugar.Ado.ExecuteCommandAsync(
                InsertMembershipSql,
                MembershipParams(group.Id, group.Id, userIsDeleted: 0)));
    }

    [Fact]
    public async Task DuplicateActiveMembership_IsRejected_ByPartialUniqueIndex()
    {
        var alice = await SeedUserAsync("user.alice", "alice");
        var group = await SeedGroupAsync("group.ops", [alice], []);

        await Assert.ThrowsAnyAsync<Exception>(() =>
            _dbContext.SqlSugar.Ado.ExecuteCommandAsync(
                InsertMembershipSql,
                MembershipParams(group.Id, alice.Id, userIsDeleted: 0)));
    }

    [Fact]
    public async Task ParentSoftDelete_CascadesMembershipShadow()
    {
        var alice = await SeedUserAsync("user.alice", "alice");
        var group = await SeedGroupAsync("group.ops", [alice], []);

        await _groups.DeleteAsync(group, group.OptimisticVersion, group.ConcurrencyVersion, CancellationToken.None);

        var shadow = await CountAsync(
            "SELECT user_group_is_deleted FROM identity_user_group_membership WHERE user_id = @userId",
            new SugarParameter("@userId", alice.Id.ToString()));
        Assert.Equal(1, shadow);
    }

    // ---- 组继承角色求值 ----

    [Fact]
    public async Task GetRoleIdsForUserAsync_ReturnsActiveGroupRoles()
    {
        var alice = await SeedUserAsync("user.alice", "alice");
        var editor = await SeedRoleAsync("role.editor");
        var viewer = await SeedRoleAsync("role.viewer");
        await SeedGroupAsync("group.ops", [alice], [editor, viewer]);

        var roleIds = await _groups.GetRoleIdsForUserAsync(alice.Id, TenantNId, CancellationToken.None);

        Assert.Equal(2, roleIds.Count);
        Assert.Contains(editor.Id, roleIds);
        Assert.Contains(viewer.Id, roleIds);
    }

    [Fact]
    public async Task GetRoleIdsForUserAsync_DisabledGroup_Excluded()
    {
        var alice = await SeedUserAsync("user.alice", "alice");
        var editor = await SeedRoleAsync("role.editor");
        var group = await SeedGroupAsync("group.ops", [alice], [editor]);

        var loaded = await _groups.GetByNIdAsync("group.ops", CancellationToken.None);
        Assert.NotNull(loaded);
        var version = loaded!.OptimisticVersion;
        var concurrency = loaded.ConcurrencyVersion;
        loaded.Disable();
        await _groups.UpdateAsync(loaded, version, concurrency, [], CancellationToken.None);

        var roleIds = await _groups.GetRoleIdsForUserAsync(alice.Id, TenantNId, CancellationToken.None);

        Assert.Empty(roleIds);
    }

    [Fact]
    public async Task GetRoleIdsForUserAsync_DeletedMembership_Excluded()
    {
        var alice = await SeedUserAsync("user.alice", "alice");
        var editor = await SeedRoleAsync("role.editor");
        var group = await SeedGroupAsync("group.ops", [alice], [editor]);

        var loaded = await _groups.GetByNIdAsync("group.ops", CancellationToken.None);
        Assert.NotNull(loaded);
        var version = loaded!.OptimisticVersion;
        var concurrency = loaded.ConcurrencyVersion;
        loaded.RemoveMember(alice);
        await _groups.UpdateAsync(loaded, version, concurrency, [], CancellationToken.None);

        var roleIds = await _groups.GetRoleIdsForUserAsync(alice.Id, TenantNId, CancellationToken.None);

        Assert.Empty(roleIds);
    }

    [Fact]
    public async Task GetRoleIdsForUserAsync_DeletedRole_Excluded()
    {
        var alice = await SeedUserAsync("user.alice", "alice");
        var editor = await SeedRoleAsync("role.editor");
        await SeedGroupAsync("group.ops", [alice], [editor]);

        var role = await _roles.GetByIdAsync(editor.Id, CancellationToken.None);
        Assert.NotNull(role);
        await _roles.DeleteAsync(role!, role!.OptimisticVersion, role.ConcurrencyVersion, CancellationToken.None);

        var roleIds = await _groups.GetRoleIdsForUserAsync(alice.Id, TenantNId, CancellationToken.None);

        Assert.Empty(roleIds);
    }

    // ---- 墓碑删除/恢复(§29A.3)----

    [Fact]
    public async Task TombstoneDelete_SoftDeletesMembershipAndRoleRelations()
    {
        var alice = await SeedUserAsync("user.alice", "alice");
        var editor = await SeedRoleAsync("role.editor");
        var group = await SeedGroupAsync("group.ops", [alice], [editor]);

        var loaded = await _groups.GetByNIdAsync("group.ops", CancellationToken.None);
        Assert.NotNull(loaded);
        var version = loaded!.OptimisticVersion;
        var concurrency = loaded.ConcurrencyVersion;
        loaded.DeleteForTombstone();
        await _groups.UpdateAsync(loaded, version, concurrency, [], CancellationToken.None);

        Assert.Null(await _groups.GetByNIdAsync("group.ops", CancellationToken.None));
        // 成员与组角色关系自身被软删(恢复后不复活)
        Assert.Equal(0, await CountAsync(
            "SELECT COUNT(*) FROM identity_user_group_membership WHERE user_group_id = @id AND is_deleted = 0",
            new SugarParameter("@id", group.Id.ToString())));
        Assert.Equal(0, await CountAsync(
            "SELECT COUNT(*) FROM identity_user_group_role WHERE user_group_id = @id AND is_deleted = 0",
            new SugarParameter("@id", group.Id.ToString())));
    }

    [Fact]
    public async Task TombstoneRestore_KeepsDisabled_RelationsStaySoftDeleted()
    {
        var alice = await SeedUserAsync("user.alice", "alice");
        var editor = await SeedRoleAsync("role.editor");
        var group = await SeedGroupAsync("group.ops", [alice], [editor]);

        var loaded = await _groups.GetByNIdAsync("group.ops", CancellationToken.None);
        Assert.NotNull(loaded);
        var deleteVersion = loaded!.OptimisticVersion;
        var deleteConcurrency = loaded.ConcurrencyVersion;
        loaded.DeleteForTombstone();
        await _groups.UpdateAsync(loaded, deleteVersion, deleteConcurrency, [], CancellationToken.None);

        var tombstone = await _groups.GetByNIdIncludingDeletedAsync("group.ops", CancellationToken.None);
        Assert.NotNull(tombstone);
        var restoreVersion = tombstone!.OptimisticVersion;
        var restoreConcurrency = tombstone.ConcurrencyVersion;
        tombstone.RestoreTombstone();
        await _groups.RestoreAsync(tombstone, restoreVersion, restoreConcurrency, CancellationToken.None);

        var restored = await _groups.GetByNIdAsync("group.ops", CancellationToken.None);
        Assert.NotNull(restored);
        Assert.False(restored!.IsDeleted);
        Assert.Equal(UserGroupStatus.Disabled, restored.Status);
        Assert.Empty(restored.Memberships);
        Assert.Empty(restored.Roles);
        // 关系行保持软删
        Assert.Equal(1, await CountAsync(
            "SELECT is_deleted FROM identity_user_group_membership WHERE user_group_id = @id",
            new SugarParameter("@id", group.Id.ToString())));
    }

    [Fact]
    public async Task DefaultQuery_ExcludesTombstone_IncludeDeletedFindsIt()
    {
        var group = await SeedGroupAsync("group.ops", [], []);

        var loaded = await _groups.GetByNIdAsync("group.ops", CancellationToken.None);
        Assert.NotNull(loaded);
        var version = loaded!.OptimisticVersion;
        var concurrency = loaded.ConcurrencyVersion;
        loaded.DeleteForTombstone();
        await _groups.UpdateAsync(loaded, version, concurrency, [], CancellationToken.None);

        Assert.Null(await _groups.GetByNIdAsync("group.ops", CancellationToken.None));
        var tombstone = await _groups.GetByNIdIncludingDeletedAsync("group.ops", CancellationToken.None);
        Assert.NotNull(tombstone);
        Assert.True(tombstone!.IsDeleted);
    }

    private static SugarParameter[] MembershipParams(Guid groupId, Guid userId, int userIsDeleted)
    {
        var now = DateTimeOffset.UtcNow.ToString("O");
        return
        [
            new SugarParameter("@id", Guid.NewGuid().ToString()),
            new SugarParameter("@entityType", "IndustrialPlatform.Identity.Domain.UserGroups.UserGroupMembership"),
            new SugarParameter("@createdOn", now),
            new SugarParameter("@lastUpdatedOn", now),
            new SugarParameter("@concurrencyVersion", Guid.NewGuid().ToString()),
            new SugarParameter("@tenantNId", TenantNId),
            new SugarParameter("@userGroupId", groupId.ToString()),
            new SugarParameter("@userGroupIsDeleted", 0),
            new SugarParameter("@userId", userId.ToString()),
            new SugarParameter("@userIsDeleted", userIsDeleted),
        ];
    }
}
