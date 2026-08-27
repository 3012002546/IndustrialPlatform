using IndustrialPlatform.Identity.Domain.Identities;
using IndustrialPlatform.Identity.Domain.Permissions;

namespace IndustrialPlatform.Identity.Domain.Tests;

/// <summary>
/// 系统权限目录测试:第一批 Permission.NId 数量、内容与格式(§9.2)。
/// </summary>
public sealed class PermissionCatalogTests
{
    private static readonly string[] ExpectedFirstBatchNIds =
    [
        "identity.user.view",
        "identity.user.create",
        "identity.user.update",
        "identity.user.status",
        "identity.user.assign-role",
        "identity.user.delete",
        "identity.user.restore",
        "identity.role.view",
        "identity.role.create",
        "identity.role.update",
        "identity.role.assign-permission",
        "identity.user-group.delete",
        "identity.user-group.restore",
        "identity.user-group.view",
        "identity.user-group.create",
        "identity.user-group.update",
        "identity.user-group.status",
        "identity.user-group.assign-member",
        "identity.user-group.assign-role",
        "identity.user.reset-password",
        "identity.permission.view",
        "identity.audit.login.view",
        "identity.sso.view",
        "identity.sso.manage",
        "identity.sso.test",
        "platform.home.view",
        "platform.pda.view",
        "platform.mobile.view",
        "identity.bootstrap.view",
        "identity.bootstrap.recover",
        "systemdata.organization.view",
        "systemdata.organization.create",
        "systemdata.organization.update",
        "systemdata.organization.move",
        "systemdata.organization.status",
        "systemdata.position.view",
        "systemdata.position.create",
        "systemdata.position.update",
        "systemdata.position.status",
        "systemdata.assignment.view",
        "systemdata.assignment.manage",
        "systemdata.resource.view",
        "systemdata.navigation.view",
        "systemdata.navigation.manage",
        "systemdata.navigation.publish",
        "systemdata.navigation.rollback",
        "systemdata.feature.view",
        "systemdata.feature.manage",
        "systemdata.service-catalog.view",
        "systemdata.service-catalog.manage",
        "systemdata.theme-policy.view",
        "systemdata.theme-policy.manage",
        "systemdata.database-orchestration.view",
        "systemdata.database-orchestration.register",
        "systemdata.database-orchestration.plan",
        "systemdata.database-orchestration.apply",
        "systemdata.database-orchestration.approve",
        "systemdata.database-orchestration.backup",
        "systemdata.database-orchestration.cancel",
        "systemdata.service-initialization.view",
        "systemdata.service-initialization.register",
        "systemdata.service-initialization.plan",
        "systemdata.service-initialization.apply",
        "systemdata.service-initialization.approve",
        "systemdata.service-initialization.backup",
        "systemdata.service-initialization.cancel",
    ];

    [Fact]
    public void FirstBatch_HasIdentityAndSystemDataPermissions()
    {
        Assert.Equal(66, PermissionCatalog.FirstBatchNIds.Count);
    }

    [Fact]
    public void FirstBatch_MatchesDocumentedCatalog()
    {
        Assert.Equal(ExpectedFirstBatchNIds, PermissionCatalog.FirstBatchNIds);
    }

    [Fact]
    public void FirstBatch_AllPassNIdValidation()
    {
        foreach (var nId in PermissionCatalog.FirstBatchNIds)
        {
            NId.Create(nId);
        }
    }

    [Fact]
    public void FirstBatch_HasNoDuplicates()
    {
        Assert.Equal(
            PermissionCatalog.FirstBatchNIds.Count,
            PermissionCatalog.FirstBatchNIds.Distinct().Count());
    }
}
