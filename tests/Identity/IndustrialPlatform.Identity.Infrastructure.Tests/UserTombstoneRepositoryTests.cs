using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IndustrialPlatform.Identity.Domain.Roles;
using IndustrialPlatform.Identity.Domain.Users;
using IndustrialPlatform.Identity.Domain.UserGroups;
using IndustrialPlatform.Identity.Infrastructure.Persistence.Migrations;
using IndustrialPlatform.Identity.Infrastructure.Persistence.Repositories;
using IndustrialPlatform.Infrastructure.Database;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SqlSugar;
using SQLitePCL;
using Xunit;

namespace IndustrialPlatform.Identity.Infrastructure.Tests;

/// <summary>
/// 用户墓碑删除/恢复持久化测试(§29A.3):删除软删直接角色与组成员关系、推进安全版本,
/// 恢复仅清除墓碑为 Disabled 且关系不复活,墓碑查询(includeDeleted)。SQLite 模拟;
/// (PostgreSQL 真实验证标记「待验收」。)
/// </summary>
[Collection(BootstrapEnvironmentTestGroup.Name)]
public sealed class UserTombstoneRepositoryTests : IDisposable
{
    private const string TenantNId = "tombstone-test";
    private const string PasswordHash = "$2a$12$abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123";

    static UserTombstoneRepositoryTests()
    {
        SQLitePCL.Batteries_V2.Init();
    }

    private readonly string _dbPath;
    private readonly SqlSugarDbContext _dbContext;
    private readonly UserRepository _users;
    private readonly RoleRepository _roles;
    private readonly UserGroupRepository _groups;

    public UserTombstoneRepositoryTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"industrial-platform-user-tombstone-{Guid.NewGuid():N}.db");
        _dbContext = new SqlSugarDbContext(Options.Create(new SqlSugarOptions
        {
            ConnectionString = $"Data Source={_dbPath};Foreign Keys=True",
            DbType = DbType.Sqlite,
        }));

        new SchemaMigrationRunner(_dbContext, IdentitySchemaMigrations.All, NullLogger<SchemaMigrationRunner>.Instance)
            .ApplyPendingAsync()
            .GetAwaiter()
            .GetResult();

        _users = new UserRepository(_dbContext);
        _roles = new RoleRepository(_dbContext);
        _groups = new UserGroupRepository(_dbContext);
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

    /// <summary>种子:用户 alice 持直接角色 editor,且是组 ops(组角色 viewer)的成员。</summary>
    private async Task<(User User, Role Editor, Role Viewer, UserGroup Group)> SeedAsync()
    {
        var alice = User.Create(TenantNId, "user.alice", "alice", "Alice", null, null, PasswordHash);
        var editor = Role.Create(TenantNId, "role.editor", "编辑", null, isSystem: false);
        alice.AssignRole(editor);
        await _roles.AddAsync(editor, CancellationToken.None);
        await _users.AddAsync(alice, CancellationToken.None);

        var viewer = Role.Create(TenantNId, "role.viewer", "查看", null, isSystem: false);
        await _roles.AddAsync(viewer, CancellationToken.None);
        var group = UserGroup.Create(TenantNId, "group.ops", "运维组", null);
        group.AssignMember(alice);
        group.AssignRole(viewer);
        await _groups.AddAsync(group, [], CancellationToken.None);
        return (alice, editor, viewer, group);
    }

    [Fact]
    public async Task TombstoneDelete_SoftDeletesRoleAndMembershipRelations_BumpsAuthVersion()
    {
        var (alice, _, _, _) = await SeedAsync();

        var loaded = await _users.GetByNIdAsync("user.alice", CancellationToken.None);
        Assert.NotNull(loaded);
        var optimistic = loaded!.OptimisticVersion;
        var concurrency = loaded.ConcurrencyVersion;
        loaded.DeleteForTombstone();
        await _users.UpdateAsync(loaded, optimistic, concurrency, [], CancellationToken.None);

        // 用户行:删除(is_deleted=1,SQLite INTEGER)+ AuthVersion 递增
        var row = await _dbContext.SqlSugar.Ado.GetStringAsync(
            "SELECT is_deleted || '|' || auth_version FROM identity_user WHERE n_id = 'user.alice'");
        Assert.Equal("1|1", row);

        // 直接角色关系被软删(自身 IsDeleted=1,而非仅父级影子)
        Assert.Equal(0, await _dbContext.SqlSugar.Ado.GetIntAsync(
            "SELECT COUNT(*) FROM identity_user_role WHERE user_id = @id AND is_deleted = 0",
            new SugarParameter("@id", alice.Id.ToString())));

        // 组成员关系被软删(自身 IsDeleted=1,保证恢复后不复活授权)
        Assert.Equal(0, await _dbContext.SqlSugar.Ado.GetIntAsync(
            "SELECT COUNT(*) FROM identity_user_group_membership WHERE user_id = @id AND is_deleted = 0",
            new SugarParameter("@id", alice.Id.ToString())));
    }

    [Fact]
    public async Task DefaultQuery_ExcludesTombstone_IncludeDeletedFindsIt()
    {
        var (alice, _, _, _) = await SeedAsync();

        var loaded = await _users.GetByNIdAsync("user.alice", CancellationToken.None);
        Assert.NotNull(loaded);
        var optimistic = loaded!.OptimisticVersion;
        var concurrency = loaded.ConcurrencyVersion;
        loaded.DeleteForTombstone();
        await _users.UpdateAsync(loaded, optimistic, concurrency, [], CancellationToken.None);

        Assert.Null(await _users.GetByNIdAsync("user.alice", CancellationToken.None));

        var tombstone = await _users.GetByNIdIncludingDeletedAsync("user.alice", CancellationToken.None);
        Assert.NotNull(tombstone);
        Assert.True(tombstone!.IsDeleted);
    }

    [Fact]
    public async Task TombstoneRestore_KeepsDisabled_RelationsStaySoftDeleted()
    {
        var (alice, _, _, _) = await SeedAsync();

        var loaded = await _users.GetByNIdAsync("user.alice", CancellationToken.None);
        Assert.NotNull(loaded);
        var deleteOptimistic = loaded!.OptimisticVersion;
        var deleteConcurrency = loaded.ConcurrencyVersion;
        loaded.DeleteForTombstone();
        await _users.UpdateAsync(loaded, deleteOptimistic, deleteConcurrency, [], CancellationToken.None);

        // 恢复:仅清除墓碑为 Disabled
        var tombstone = await _users.GetByNIdIncludingDeletedAsync("user.alice", CancellationToken.None);
        Assert.NotNull(tombstone);
        var restoreOptimistic = tombstone!.OptimisticVersion;
        var restoreConcurrency = tombstone.ConcurrencyVersion;
        tombstone.RestoreTombstone();
        await _users.RestoreAsync(tombstone, restoreOptimistic, restoreConcurrency, [], CancellationToken.None);

        var restored = await _users.GetByNIdAsync("user.alice", CancellationToken.None);
        Assert.NotNull(restored);
        Assert.False(restored!.IsDeleted);
        Assert.Equal(UserStatus.Disabled, restored.Status);
        // 直接角色与组成员关系保持软删,不自动恢复(§29A.3)
        Assert.All(restored.UserRoles, r => Assert.True(r.IsDeleted));
        Assert.Equal(0, await _dbContext.SqlSugar.Ado.GetIntAsync(
            "SELECT COUNT(*) FROM identity_user_group_membership WHERE user_id = @id AND is_deleted = 0",
            new SugarParameter("@id", alice.Id.ToString())));
    }

    [Fact]
    public async Task RestoreAsync_StaleVersion_ThrowsConcurrency()
    {
        var (alice, _, _, _) = await SeedAsync();

        var loaded = await _users.GetByNIdAsync("user.alice", CancellationToken.None);
        Assert.NotNull(loaded);
        var deleteOptimistic = loaded!.OptimisticVersion;
        var deleteConcurrency = loaded.ConcurrencyVersion;
        loaded.DeleteForTombstone();
        await _users.UpdateAsync(loaded, deleteOptimistic, deleteConcurrency, [], CancellationToken.None);

        var tombstone = await _users.GetByNIdIncludingDeletedAsync("user.alice", CancellationToken.None);
        Assert.NotNull(tombstone);

        await Assert.ThrowsAsync<SharedKernel.Exceptions.ConcurrencyException>(() =>
            _users.RestoreAsync(tombstone!, 999, Guid.NewGuid(), [], CancellationToken.None));
    }
}
