using IndustrialPlatform.Identity.Domain.Roles;
using IndustrialPlatform.Identity.Domain.Users;
using IndustrialPlatform.SharedKernel.Exceptions;

namespace IndustrialPlatform.Identity.Domain.Tests;

/// <summary>
/// 用户安全删除(墓碑)与恢复测试(§29A.3):推进安全版本、软删直接角色关系、
/// 删除事件、恢复为 Disabled 且不恢复授权、幂等/守卫边界。
/// </summary>
public sealed class UserTombstoneTests
{
    private const string TenantNId = "tenant-01";

    private static User CreateUserWithRole(out Role role)
    {
        var user = User.Create(TenantNId, "user-001", "alice", "Alice", null, null, "hashed-password");
        role = Role.Create(TenantNId, "role.editor", "编辑", null, isSystem: false);
        user.AssignRole(role);
        return user;
    }

    [Fact]
    public void DeleteForTombstone_MarksDeletedBumpsAuthVersionAndSoftDeletesRelations()
    {
        var user = CreateUserWithRole(out _);
        var relation = Assert.Single(user.UserRoles);
        var authVersionBefore = user.AuthVersion;
        var versionBefore = user.OptimisticVersion;

        user.DeleteForTombstone();

        Assert.True(user.IsDeleted);
        Assert.Equal(authVersionBefore + 1, user.AuthVersion);
        Assert.True(relation.IsDeleted);
        Assert.True(user.OptimisticVersion > versionBefore);
        // 删除事件载荷携带删除后的安全版本
        var deleted = Assert.Single(user.DomainEvents.OfType<UserDeletedEvent>());
        Assert.Equal("user-001", deleted.UserNId);
        Assert.Equal(authVersionBefore + 1, deleted.AuthVersion);
    }

    [Fact]
    public void DeleteForTombstone_OnFrozenUser_Throws()
    {
        var user = CreateUserWithRole(out _);
        user.Freeze();

        Assert.Throws<BusinessException>(() => user.DeleteForTombstone());
        Assert.False(user.IsDeleted);
    }

    [Fact]
    public void RestoreTombstone_ClearsDeletedAndStaysDisabled_PublishesEvent()
    {
        var user = CreateUserWithRole(out _);
        user.DeleteForTombstone();
        user.ClearDomainEvents();
        var authVersion = user.AuthVersion;

        user.RestoreTombstone();

        Assert.False(user.IsDeleted);
        Assert.Equal(UserStatus.Disabled, user.Status);
        // 恢复不推进安全版本(会话/凭据保持失效)
        Assert.Equal(authVersion, user.AuthVersion);
        // 直接角色关系保持软删,不自动恢复
        Assert.All(user.UserRoles, r => Assert.True(r.IsDeleted));
        var restored = Assert.Single(user.DomainEvents.OfType<UserRestoredEvent>());
        Assert.Equal("user-001", restored.UserNId);
    }

    [Fact]
    public void RestoreTombstone_OnActiveUser_Throws()
    {
        var user = CreateUserWithRole(out _);

        Assert.Throws<BusinessException>(() => user.RestoreTombstone());
        Assert.False(user.IsDeleted);
        Assert.Equal(UserStatus.Active, user.Status);
    }

    [Fact]
    public void TombstoneUser_RejectsLogin()
    {
        var user = CreateUserWithRole(out _);
        user.DeleteForTombstone();

        Assert.Throws<UnauthorizedException>(() => user.EnsureLoginAllowed(DateTimeOffset.UtcNow));
    }
}
