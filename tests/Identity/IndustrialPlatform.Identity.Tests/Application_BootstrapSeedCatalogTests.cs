using IndustrialPlatform.Identity.Application.Bootstrap;
using IndustrialPlatform.Identity.Domain.Permissions;

namespace IndustrialPlatform.Identity.Tests;

public sealed class Application_BootstrapSeedCatalogTests
{
    [Fact]
    public void System_catalog_seed_contains_remote_assistance_permissions_at_current_version()
    {
        Assert.Equal("1.9.0", BootstrapSeedCatalog.SeedVersion);
        var ids = BootstrapSeedCatalog.Permissions.Select(item => item.NId).ToHashSet(StringComparer.Ordinal);

        Assert.Contains(PermissionCatalog.RemoteAssistanceSessionShare, ids);
        Assert.Contains(PermissionCatalog.RemoteAssistanceSessionJoin, ids);
        Assert.Contains(PermissionCatalog.RemoteAssistanceVoiceCall, ids);
        Assert.Equal(195, ids.Count);
        Assert.Contains("studio.lowcode.design", ids);
        Assert.Contains("studio.audit.view", ids);
        Assert.DoesNotContain("studio.studio.", ids);
    }
}
