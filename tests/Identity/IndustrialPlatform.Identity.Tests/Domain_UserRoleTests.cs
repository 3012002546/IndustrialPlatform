using IndustrialPlatform.Identity.Domain.Roles;
using IndustrialPlatform.Identity.Domain.Users;
using IndustrialPlatform.SharedKernel.Exceptions;

namespace IndustrialPlatform.Identity.Domain.Tests;

/// <summary>
/// User 聚合下 UserRole 关系测试:角色分配/解除、跨租户与删除角色守卫、
/// 事件发布、乐观版本与最后系统管理员保护。
/// </summary>
public sealed class UserRoleTests
{
    private static User CreateUser() =>
        User.Create("tenant-01", "user-001", "alice", "Alice", null, null, "hashed-password");

    private static Role CreateRole(bool isSystem = false, string tenantNId = "tenant-01") =>
        Role.Create(tenantNId, isSystem ? "system-admin" : "editor", isSystem ? "系统管理员" : "编辑", null, isSystem);

    [Fact]
    public void AssignRole_AddsUserRole()
    {
        var user = CreateUser();
        var role = CreateRole();

        user.AssignRole(role);

        var relation = Assert.Single(user.UserRoles);
        Assert.Equal("tenant-01", relation.TenantNId);
        Assert.Equal(user.Id, relation.UserId);
        Assert.False(relation.UserIsDeleted);
        Assert.Equal(role.Id, relation.RoleId);
        Assert.False(relation.RoleIsDeleted);
        Assert.False(relation.IsDeleted);
    }

    [Fact]
    public void AssignRole_PublishesUserRolesChangedEvent()
    {
        var user = CreateUser();
        var role = CreateRole();

        user.AssignRole(role);

        var rolesEvent = Assert.Single(user.DomainEvents.OfType<UserRolesChangedEvent>());
        Assert.Equal("tenant-01", rolesEvent.TenantNId);
        Assert.Equal("user-001", rolesEvent.UserNId);
        Assert.Equal("editor", rolesEvent.RoleNId);
        Assert.True(rolesEvent.OccurredOn <= DateTimeOffset.UtcNow);
    }

    [Fact]
    public void AssignRole_CrossTenant_ThrowsBusinessException()
    {
        var user = CreateUser();
        var otherTenantRole = CreateRole(tenantNId: "tenant-02");

        Assert.Throws<BusinessException>(() => user.AssignRole(otherTenantRole));
        Assert.Empty(user.UserRoles);
    }

    [Fact]
    public void AssignRole_DeletedRole_ThrowsBusinessException()
    {
        var user = CreateUser();
        var role = CreateRole();
        role.Delete();

        Assert.Throws<BusinessException>(() => user.AssignRole(role));
        Assert.Empty(user.UserRoles);
    }

    [Fact]
    public void AssignRole_DuplicateActiveRole_ThrowsBusinessException()
    {
        var user = CreateUser();
        var role = CreateRole();
        user.AssignRole(role);

        Assert.Throws<BusinessException>(() => user.AssignRole(role));
    }

    [Fact]
    public void AssignRole_NullRole_ThrowsArgumentNullException()
    {
        var user = CreateUser();

        Assert.Throws<ArgumentNullException>(() => user.AssignRole(null!));
    }

    [Fact]
    public void AssignRole_AfterRemove_CanReassign()
    {
        var user = CreateUser();
        var role = CreateRole();
        user.AssignRole(role);
        user.RemoveRole(role, activeHolderCountInTenant: 2);

        user.AssignRole(role);

        var activeRelations = user.UserRoles.Where(ur => !ur.IsDeleted).ToList();
        Assert.Single(activeRelations);
        Assert.Equal(2, user.UserRoles.Count);
    }

    [Fact]
    public void AssignRole_TouchesUser()
    {
        var user = CreateUser();
        var beforeVersion = user.OptimisticVersion;

        user.AssignRole(CreateRole());

        Assert.Equal(beforeVersion + 1, user.OptimisticVersion);
    }

    [Fact]
    public void AssignRole_WhenFrozen_ThrowsBusinessException()
    {
        var user = CreateUser();
        user.Freeze();

        Assert.Throws<BusinessException>(() => user.AssignRole(CreateRole()));
        Assert.Empty(user.UserRoles);
    }

    [Fact]
    public void RemoveRole_RemovesActiveRelation()
    {
        var user = CreateUser();
        var role = CreateRole();
        user.AssignRole(role);

        user.RemoveRole(role, activeHolderCountInTenant: 2);

        var relation = Assert.Single(user.UserRoles);
        Assert.True(relation.IsDeleted);
    }

    [Fact]
    public void RemoveRole_Nonexistent_IsIdempotent()
    {
        var user = CreateUser();
        var role = CreateRole();

        user.RemoveRole(role, activeHolderCountInTenant: 1);

        Assert.Empty(user.UserRoles);
        Assert.DoesNotContain(user.DomainEvents, e => e is UserRolesChangedEvent);
    }

    [Fact]
    public void RemoveRole_PublishesUserRolesChangedEvent()
    {
        var user = CreateUser();
        var role = CreateRole();
        user.AssignRole(role);
        user.ClearDomainEvents();

        user.RemoveRole(role, activeHolderCountInTenant: 2);

        var rolesEvent = Assert.Single(user.DomainEvents.OfType<UserRolesChangedEvent>());
        Assert.Equal("editor", rolesEvent.RoleNId);
    }

    [Fact]
    public void RemoveRole_LastSystemAdmin_ThrowsBusinessException()
    {
        var user = CreateUser();
        var systemRole = CreateRole(isSystem: true);
        user.AssignRole(systemRole);

        Assert.Throws<BusinessException>(() => user.RemoveRole(systemRole, activeHolderCountInTenant: 1));

        var relation = Assert.Single(user.UserRoles);
        Assert.False(relation.IsDeleted);
    }

    [Fact]
    public void RemoveRole_SystemAdminNotLast_Succeeds()
    {
        var user = CreateUser();
        var systemRole = CreateRole(isSystem: true);
        user.AssignRole(systemRole);

        user.RemoveRole(systemRole, activeHolderCountInTenant: 2);

        var relation = Assert.Single(user.UserRoles);
        Assert.True(relation.IsDeleted);
    }

    [Fact]
    public void RemoveRole_NonSystemRole_LastHolderAllowed()
    {
        var user = CreateUser();
        var role = CreateRole();
        user.AssignRole(role);

        user.RemoveRole(role, activeHolderCountInTenant: 1);

        var relation = Assert.Single(user.UserRoles);
        Assert.True(relation.IsDeleted);
    }
}
