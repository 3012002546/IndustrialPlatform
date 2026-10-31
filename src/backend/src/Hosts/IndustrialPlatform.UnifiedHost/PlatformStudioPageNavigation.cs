using System.Globalization;
using IndustrialPlatform.PlatformStudio.Application;
using IndustrialPlatform.PlatformStudio.Application.Publishing;
using IndustrialPlatform.PlatformStudio.Domain;
using IndustrialPlatform.SystemData.Application.ControlPlane;
using IndustrialPlatform.SystemData.Contracts.ControlPlane;

namespace IndustrialPlatform.UnifiedHost;

/// <summary>Native composition only; neither product accesses the other's repository.</summary>
public sealed class PlatformStudioPageNavigation(IOwnedNavigationService navigation) : IStudioPageNavigationHost, IStudioPageLifecycle
{
    public const string Module = "platform-studio";
    public const string RuntimeResource = "studio.page.lowcode-runtime";
    public async Task<StudioMenuOptions> ReadAsync(StudioPrincipal principal, string resourceNId, CancellationToken ct)
        => Map(await navigation.ReadAsync(principal.TenantNId, Module, resourceNId, ct));
    public async Task<StudioMenuOptions> MountAsync(StudioPrincipal principal, string resourceNId, StudioMenuCommand command, CancellationToken ct)
        => Map(await navigation.MountAsync(principal.TenantNId, principal.SubjectNId, Module, resourceNId, RuntimeResource,
            new(command.NodeNId, command.ParentNodeNId, command.Label, command.IconKey, command.DisplayOrder, StudioJson.Version(command.ExpectedRevision, "expectedRevision")), ct));
    public async Task<StudioMenuOptions> DetachAsync(StudioPrincipal principal, string resourceNId, string? nodeNId, string? expectedRevision, CancellationToken ct)
        => Map(await navigation.DetachAsync(principal.TenantNId, principal.SubjectNId, Module, resourceNId, nodeNId, expectedRevision is null ? null : StudioJson.Version(expectedRevision, "expectedRevision"), ct));
    private static StudioMenuOptions Map(OwnedNavigationOptions options) => new(options.Revision.ToString(CultureInfo.InvariantCulture),
        options.Links.Select(l => new StudioMenuLink(l.NodeNId, l.ParentNodeNId, l.Label, l.IconKey, l.DisplayOrder, l.Path, l.Detached)).ToArray(), options.Groups);

    public async Task BeforeDeleteAsync(StudioPrincipal principal, string resourceNId, CancellationToken ct)
    {
        try { await navigation.DetachAsync(principal.TenantNId, principal.SubjectNId, Module, resourceNId, null, null, ct); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        { throw new StudioException("STUDIO_MENU_SYNC_FAILED", "菜单解除失败，页面尚未删除；请刷新关联后重试。", 409); }
    }
}

public sealed class PlatformStudioNavigationRuntimePolicy(StudioPublishing publishing, IStudioIdentity identity) : INavigationRuntimePolicy
{
    public async Task<NavigationRuntimeResponse> ApplyAsync(string tenant, NavigationRuntimeResponse runtime, CancellationToken ct)
    {
        var principal = await identity.GetCurrentAsync(ct);
        if (principal.TenantNId != tenant) throw new StudioException("STUDIO_FORBIDDEN", "导航租户不匹配。", 403);
        var allowed = new Dictionary<string, bool>(StringComparer.Ordinal);
        async Task<IReadOnlyCollection<NavigationRuntimeNodeResponse>> Filter(IReadOnlyCollection<NavigationRuntimeNodeResponse> nodes)
        {
            var result = new List<NavigationRuntimeNodeResponse>();
            foreach (var node in nodes)
            {
                if (node.OwnerModuleNId == PlatformStudioPageNavigation.Module && node.OwnerResourceNId is { } page)
                {
                    if (!allowed.TryGetValue(page, out var visible))
                    {
                        try { await publishing.RequireRuntimeAsync(principal, page, null, ct); visible = true; }
                        catch (StudioException) { visible = false; }
                        allowed.Add(page, visible);
                    }
                    if (!visible) continue;
                }
                result.Add(node with { Children = await Filter(node.Children) });
            }
            return result;
        }
        return runtime with { Nodes = await Filter(runtime.Nodes) };
    }
}

/// <summary>A host switched to an independent profile must not project old native Studio entries.</summary>
public sealed class PlatformStudioDeploymentNavigationPolicy(StudioDeploymentProfile profile) : INavigationRuntimePolicy
{
    public Task<NavigationRuntimeResponse> ApplyAsync(string tenant, NavigationRuntimeResponse runtime, CancellationToken ct)
    {
        if (profile.DeploymentMode == "PlatformNative") return Task.FromResult(runtime);
        static IReadOnlyCollection<NavigationRuntimeNodeResponse> Filter(IReadOnlyCollection<NavigationRuntimeNodeResponse> nodes)
            => nodes.Where(n => n.OwnerModuleNId != PlatformStudioPageNavigation.Module && n.ResourceNId?.StartsWith("studio.page.", StringComparison.Ordinal) != true)
                .Select(n => n with { Children = Filter(n.Children) }).ToArray();
        return Task.FromResult(runtime with { Nodes = Filter(runtime.Nodes) });
    }
}
