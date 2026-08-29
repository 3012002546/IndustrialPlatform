using IndustrialPlatform.SystemData.Api.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace IndustrialPlatform.SystemData.Api.Tests;

/// <summary>
/// SystemData 权限策略目录契约：操作策略必须与 Identity 种子和前端生成目录保持稳定 NId。
/// </summary>
public sealed class PermissionCatalogContractTests
{
    private static readonly string[] ExpectedNIds =
    [
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
    public async Task RegisteredPolicies_MatchStableSystemDataCatalog()
    {
        var options = new AuthorizationOptions();
        SystemDataPermissionPolicies.AddPermissionPolicies(options);
        var provider = new DefaultAuthorizationPolicyProvider(Options.Create(options));

        Assert.Equal(ExpectedNIds.Length, ExpectedNIds.Distinct(StringComparer.Ordinal).Count());
        foreach (var nId in ExpectedNIds)
        {
            Assert.NotNull(await provider.GetPolicyAsync(SystemDataPermissionPolicies.PolicyName(nId)));
        }
    }
}
