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
        "identity.permission.view",
        "identity.audit.login.view",
        "identity.sso.view",
        "identity.sso.manage",
        "identity.sso.test",
        "platform.home.view",
        "platform.pda.view",
        "platform.mobile.view",
    ];

    [Fact]
    public void FirstBatch_HasTwentyOnePermissions()
    {
        Assert.Equal(21, PermissionCatalog.FirstBatchNIds.Count);
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
