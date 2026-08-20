using IndustrialPlatform.Identity.Domain.Permissions;
using IndustrialPlatform.Identity.Domain.Roles;
using IndustrialPlatform.SharedKernel.Exceptions;

namespace IndustrialPlatform.Identity.Domain.Tests;

/// <summary>
/// Role 聚合测试:创建、资料变更、权限分配/解除(含事件与乐观版本)与系统角色删除保护。
/// </summary>
public sealed class RoleTests
{
    private static Role CreateRole(bool isSystem = false, string tenantNId = "tenant-01") =>
        Role.Create(tenantNId, isSystem ? "system-admin" : "editor", isSystem ? "系统管理员" : "编辑", null, isSystem);

    private static Permission CreatePermission() =>
        Permission.Create("identity.user.view", "查看用户", PermissionType.Action, null, null);

    [Fact]
    public void Create_SetsAllFields()
    {
        var role = CreateRole();

        Assert.Equal("tenant-01", role.TenantNId);
        Assert.Equal("editor", role.NId);
        Assert.Equal("EDITOR", role.NormalizedNId);
        Assert.Equal("编辑", role.Name);
        Assert.Null(role.Description);
        Assert.False(role.IsSystem);
        Assert.Empty(role.Permissions);
    }

    [Fact]
    public void Create_NormalizesNId()
    {
        var role = Role.Create("tenant-01", "  Editor ", "编辑", null, false);

        Assert.Equal("Editor", role.NId);
        Assert.Equal("EDITOR", role.NormalizedNId);
    }

    [Fact]
    public void Create_EmptyTenantNId_ThrowsValidationException()
    {
        Assert.Throws<ValidationException>(() =>
            Role.Create("  ", "editor", "编辑", null, false));
    }

    [Fact]
    public void Create_InvalidNId_ThrowsValidationException()
    {
        Assert.Throws<ValidationException>(() =>
            Role.Create("tenant-01", "..bad", "编辑", null, false));
    }

    [Fact]
    public void Create_EmptyName_ThrowsValidationException()
    {
        Assert.Throws<ValidationException>(() =>
            Role.Create("tenant-01", "editor", "  ", null, false));
    }

    [Fact]
    public void Create_EmptyDescription_BecomesNull()
    {
        var role = Role.Create("tenant-01", "editor", "编辑", "   ", false);

        Assert.Null(role.Description);
    }

    [Fact]
    public void ChangeProfile_UpdatesNameAndDescription()
    {
        var role = CreateRole();

        role.ChangeProfile("超级编辑", "可管理内容");

        Assert.Equal("超级编辑", role.Name);
        Assert.Equal("可管理内容", role.Description);
    }

    [Fact]
    public void ChangeProfile_PublishesNoEvents()
    {
        var role = CreateRole();

        role.ChangeProfile("超级编辑", null);

        Assert.Empty(role.DomainEvents);
    }

    [Fact]
    public void AssignPermission_AddsRolePermission()
    {
        var role = CreateRole();
        var permission = CreatePermission();

        role.AssignPermission(permission);

        var relation = Assert.Single(role.Permissions);
        Assert.Equal(role.Id, relation.RoleId);
        Assert.False(relation.RoleIsDeleted);
        Assert.Equal(permission.Id, relation.PermissionId);
        Assert.False(relation.PermissionIsDeleted);
        Assert.False(relation.IsDeleted);
    }

    [Fact]
    public void AssignPermission_PublishesRolePermissionsChangedEvent()
    {
        var role = CreateRole();
        var permission = CreatePermission();

        role.AssignPermission(permission);

        var permissionEvent = Assert.Single(role.DomainEvents.OfType<RolePermissionsChangedEvent>());
        Assert.Equal("tenant-01", permissionEvent.TenantNId);
        Assert.Equal("editor", permissionEvent.RoleNId);
        Assert.Equal("identity.user.view", permissionEvent.PermissionNId);
        Assert.True(permissionEvent.OccurredOn <= DateTimeOffset.UtcNow);
    }

    [Fact]
    public void AssignPermission_DuplicateActiveRelation_ThrowsBusinessException()
    {
        var role = CreateRole();
        var permission = CreatePermission();
        role.AssignPermission(permission);

        Assert.Throws<BusinessException>(() => role.AssignPermission(permission));
    }

    [Fact]
    public void AssignPermission_DeletedPermission_ThrowsBusinessException()
    {
        var role = CreateRole();
        var permission = CreatePermission();
        permission.MarkDeleted();

        Assert.Throws<BusinessException>(() => role.AssignPermission(permission));
        Assert.Empty(role.Permissions);
    }

    [Fact]
    public void AssignPermission_AfterUnassign_CanReassign()
    {
        var role = CreateRole();
        var permission = CreatePermission();
        role.AssignPermission(permission);
        role.UnassignPermission(permission);

        role.AssignPermission(permission);

        var activeRelations = role.Permissions.Where(p => !p.IsDeleted).ToList();
        Assert.Single(activeRelations);
        Assert.Equal(2, role.Permissions.Count);
    }

    [Fact]
    public void AssignPermission_TouchesRole()
    {
        var role = CreateRole();
        var beforeVersion = role.OptimisticVersion;

        role.AssignPermission(CreatePermission());

        Assert.Equal(beforeVersion + 1, role.OptimisticVersion);
    }

    [Fact]
    public void AssignPermission_NullPermission_ThrowsArgumentNullException()
    {
        var role = CreateRole();

        Assert.Throws<ArgumentNullException>(() => role.AssignPermission(null!));
    }

    [Fact]
    public void UnassignPermission_RemovesActiveRelation()
    {
        var role = CreateRole();
        var permission = CreatePermission();
        role.AssignPermission(permission);

        role.UnassignPermission(permission);

        var relation = Assert.Single(role.Permissions);
        Assert.True(relation.IsDeleted);
    }

    [Fact]
    public void UnassignPermission_Nonexistent_IsIdempotent()
    {
        var role = CreateRole();
        var permission = CreatePermission();

        role.UnassignPermission(permission);

        Assert.Empty(role.Permissions);
        Assert.Empty(role.DomainEvents);
    }

    [Fact]
    public void UnassignPermission_PublishesRolePermissionsChangedEvent()
    {
        var role = CreateRole();
        var permission = CreatePermission();
        role.AssignPermission(permission);
        role.ClearDomainEvents();

        role.UnassignPermission(permission);

        var permissionEvent = Assert.Single(role.DomainEvents.OfType<RolePermissionsChangedEvent>());
        Assert.Equal("identity.user.view", permissionEvent.PermissionNId);
    }

    [Fact]
    public void Delete_SystemRole_ThrowsBusinessException()
    {
        var role = CreateRole(isSystem: true);

        Assert.Throws<BusinessException>(() => role.Delete());
        Assert.False(role.IsDeleted);
    }

    [Fact]
    public void Delete_NonSystemRole_MarksDeleted()
    {
        var role = CreateRole();

        role.Delete();

        Assert.True(role.IsDeleted);
    }
}
