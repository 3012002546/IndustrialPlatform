using IndustrialPlatform.Identity.Domain.Permissions;
using IndustrialPlatform.SharedKernel.Exceptions;

namespace IndustrialPlatform.Identity.Domain.Tests;

/// <summary>
/// Permission 聚合测试:创建(根/带父)、Type、Status、NId 校验与资料变更。
/// </summary>
public sealed class PermissionTests
{
    private static Permission CreateRootPermission() =>
        Permission.Create("identity.user.view", "查看用户", PermissionType.Action, null, null);

    [Fact]
    public void Create_SetsAllFields()
    {
        var permission = CreateRootPermission();

        Assert.Equal("identity.user.view", permission.NId);
        Assert.Equal("IDENTITY.USER.VIEW", permission.NormalizedNId);
        Assert.Equal("查看用户", permission.Name);
        Assert.Equal(PermissionType.Action, permission.Type);
        Assert.Null(permission.ParentPermissionNId);
        Assert.Null(permission.Description);
        Assert.Equal(PermissionStatus.Active, permission.Status);
    }

    [Fact]
    public void Create_NormalizesNId()
    {
        var permission = Permission.Create("  Identity.User.View ", "查看用户", PermissionType.Action, null, null);

        Assert.Equal("Identity.User.View", permission.NId);
        Assert.Equal("IDENTITY.USER.VIEW", permission.NormalizedNId);
    }

    [Fact]
    public void Create_WithParentPermissionNId_StoresTrimmedValue()
    {
        var permission = Permission.Create("identity.sso.test", "SSO 测试", PermissionType.Action, "  identity.sso.view ", null);

        Assert.Equal("identity.sso.view", permission.ParentPermissionNId);
    }

    [Fact]
    public void Create_WithWhitespaceParentPermissionNId_BecomesNull()
    {
        var permission = Permission.Create("identity.sso.test", "SSO 测试", PermissionType.Action, "   ", null);

        Assert.Null(permission.ParentPermissionNId);
    }

    [Fact]
    public void Create_InvalidNId_ThrowsValidationException()
    {
        Assert.Throws<ValidationException>(() =>
            Permission.Create("..bad", "查看用户", PermissionType.Action, null, null));
    }

    [Fact]
    public void Create_InvalidParentPermissionNId_ThrowsValidationException()
    {
        Assert.Throws<ValidationException>(() =>
            Permission.Create("identity.sso.test", "SSO 测试", PermissionType.Action, "..bad", null));
    }

    [Fact]
    public void Create_EmptyName_ThrowsValidationException()
    {
        Assert.Throws<ValidationException>(() =>
            Permission.Create("identity.user.view", "  ", PermissionType.Action, null, null));
    }

    [Fact]
    public void Create_TrimsDescription()
    {
        var permission = Permission.Create("identity.user.view", "查看用户", PermissionType.Action, null, "  描述  ");

        Assert.Equal("描述", permission.Description);
    }

    [Fact]
    public void Create_UndefinedType_ThrowsValidationException()
    {
        Assert.Throws<ValidationException>(() =>
            Permission.Create("identity.user.view", "查看用户", (PermissionType)99, null, null));
    }

    [Fact]
    public void ChangeProfile_UpdatesNameAndDescription()
    {
        var permission = CreateRootPermission();

        permission.ChangeProfile("新名称", "新描述");

        Assert.Equal("新名称", permission.Name);
        Assert.Equal("新描述", permission.Description);
    }

    [Fact]
    public void ChangeProfile_DoesNotChangeNIdOrPublishEvents()
    {
        var permission = CreateRootPermission();

        permission.ChangeProfile("新名称", null);

        Assert.Equal("identity.user.view", permission.NId);
        Assert.Equal("IDENTITY.USER.VIEW", permission.NormalizedNId);
        Assert.Empty(permission.DomainEvents);
    }
}
