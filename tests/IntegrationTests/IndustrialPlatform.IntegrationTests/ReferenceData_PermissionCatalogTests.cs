using IndustrialPlatform.Identity.Application.Bootstrap;
using IndustrialPlatform.Identity.Domain.Permissions;
using IndustrialPlatform.ReferenceData.Contracts;

namespace IndustrialPlatform.IntegrationTests;

public sealed class ReferenceDataPermissionCatalogTests
{
    [Fact]
    public void ReferenceData_actions_are_available_to_identity_seeding_without_duplicates()
    {
        foreach (var permission in ReferenceDataPermissions.All)
        {
            Assert.Contains(permission, PermissionCatalog.FirstBatchNIds);
            Assert.Single(BootstrapSeedCatalog.Permissions, item => item.NId == permission);
        }
        Assert.Contains(ReferenceDataPermissions.PlatformManage, PermissionCatalog.FirstBatchNIds);
    }
}
