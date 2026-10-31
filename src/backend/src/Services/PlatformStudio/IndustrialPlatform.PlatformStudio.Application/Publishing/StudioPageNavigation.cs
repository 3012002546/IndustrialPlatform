using IndustrialPlatform.PlatformStudio.Domain;

namespace IndustrialPlatform.PlatformStudio.Application.Publishing;

public sealed record StudioPublicationCapabilities(string DeploymentMode, bool CanMountMenu, bool CanOpenScreen, string? ReasonCode);
public sealed record StudioMenuLink(string NodeNId, string ParentNodeNId, string Label, string? IconKey, int DisplayOrder, string Path, bool Detached);
public sealed record StudioMenuOptions(string Revision, IReadOnlyList<StudioMenuLink> Links, IReadOnlyDictionary<string, string> Groups);
public sealed record StudioMenuCommand(string? NodeNId, string ParentNodeNId, string Label, string? IconKey, int DisplayOrder, string ExpectedRevision);

/// <summary>Only a native host supplies platform navigation. Independent deployments have no substitute menu.</summary>
public interface IStudioPageNavigationHost
{
    Task<StudioMenuOptions> ReadAsync(StudioPrincipal principal, string resourceNId, CancellationToken ct);
    Task<StudioMenuOptions> MountAsync(StudioPrincipal principal, string resourceNId, StudioMenuCommand command, CancellationToken ct);
    Task<StudioMenuOptions> DetachAsync(StudioPrincipal principal, string resourceNId, string? nodeNId, string? expectedRevision, CancellationToken ct);
}

public interface IStudioPageLifecycle
{
    Task BeforeDeleteAsync(StudioPrincipal principal, string resourceNId, CancellationToken ct);
}

public sealed class StudioPageNavigation(StudioDeploymentProfile profile, IStudioResourceStore store, IStudioAuthorization authorization, IEnumerable<IStudioPageNavigationHost> hosts)
{
    private IStudioPageNavigationHost? Host => profile.DeploymentMode == "PlatformNative" ? hosts.SingleOrDefault() : null;
    public StudioPublicationCapabilities Capabilities(StudioPrincipal principal)
    {
        authorization.RequirePermission(principal, "studio.lowcode.view");
        var reason = profile.DeploymentMode != "PlatformNative" ? "STUDIO_MENU_UNSUPPORTED"
            : Host is null ? "STUDIO_MENU_UNAVAILABLE"
            : !principal.HasPermission("studio.lowcode.publish") || !principal.HasPermission("systemdata.navigation.manage") || !principal.HasPermission("systemdata.navigation.publish") ? "STUDIO_FORBIDDEN" : null;
        return new(profile.DeploymentMode, reason is null, true, reason);
    }
    public async Task<StudioMenuOptions> ReadAsync(StudioPrincipal principal, string nId, CancellationToken ct)
    {
        await RequirePage(principal, nId, false, ct, true);
        return await RequireHost(principal, false).ReadAsync(principal, nId, ct);
    }
    public async Task<StudioMenuOptions> MountAsync(StudioPrincipal principal, string nId, StudioMenuCommand command, CancellationToken ct)
    {
        // Check deployment before accepting any menu intent, even if a host was accidentally registered.
        var host = RequireHost(principal, true);
        var page = await RequirePage(principal, nId, true, ct);
        if (!page.IsEnabled || page.PublishedRevisionId is null) throw new StudioException("STUDIO_NOT_READY", "请先发布并启用页面。", 409);
        return await host.MountAsync(principal, nId, command, ct);
    }
    public async Task<StudioMenuOptions> DetachAsync(StudioPrincipal principal, string nId, string nodeNId, string revision, CancellationToken ct)
    {
        var host = RequireHost(principal, true);
        await RequirePage(principal, nId, true, ct, true);
        return await host.DetachAsync(principal, nId, nodeNId, revision, ct);
    }
    private IStudioPageNavigationHost RequireHost(StudioPrincipal principal, bool write)
    {
        if (profile.DeploymentMode != "PlatformNative") throw new StudioException("STUDIO_MENU_UNSUPPORTED", "独立部署不支持发布到平台菜单，请使用运行或全屏展示入口。", 409);
        var host = Host ?? throw new StudioException("STUDIO_MENU_UNAVAILABLE", "平台导航适配不可用。", 503);
        if (write)
        {
            authorization.RequirePermission(principal, "systemdata.navigation.manage");
            authorization.RequirePermission(principal, "systemdata.navigation.publish");
        }
        return host;
    }
    private async Task<StudioResource> RequirePage(StudioPrincipal principal, string nId, bool publish, CancellationToken ct, bool allowDeleted = false)
    {
        var page = await store.FindResourceAsync(principal.TenantNId, nId, ct);
        if (page is null || page.Kind != "LowCodePage" || page.IsDeleted && !allowDeleted || !profile.IsKindEnabled("LowCodePage")) throw new StudioException("STUDIO_NOT_FOUND", "页面不存在。", 404);
        await authorization.RequireAsync(principal, page, publish ? "studio.lowcode.publish" : "studio.lowcode.design", publish ? "Publish" : "Design", ct);
        return page;
    }
}
