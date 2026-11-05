using IndustrialPlatform.SharedKernel.Exceptions;
using IndustrialPlatform.SystemData.Contracts.ControlPlane;
using IndustrialPlatform.SystemData.Domain.ControlPlane;

namespace IndustrialPlatform.SystemData.Application.ControlPlane;

/// <summary>Module-owned links use one declared UI resource and stable business targets.</summary>
public sealed record OwnedNavigationTarget(string ModuleNId, string ResourceNId, bool Detached = false);
public sealed record OwnedNavigationLink(string NodeNId, string ParentNodeNId, string Label, string? IconKey, int DisplayOrder, string Path, bool Detached);
public sealed record OwnedNavigationOptions(long Revision, IReadOnlyList<OwnedNavigationLink> Links, IReadOnlyDictionary<string, string> Groups);
public sealed record OwnedNavigationCommand(string? NodeNId, string ParentNodeNId, string Label, string? IconKey, int DisplayOrder, long ExpectedRevision);

public interface IOwnedNavigationService
{
    Task<OwnedNavigationOptions> ReadAsync(string tenant, string ownerModule, string target, CancellationToken ct);
    Task<OwnedNavigationOptions> MountAsync(string tenant, string actor, string ownerModule, string target, string uiResource, OwnedNavigationCommand command, CancellationToken ct);
    Task<OwnedNavigationOptions> DetachAsync(string tenant, string actor, string ownerModule, string target, string? nodeNId, long? expectedRevision, CancellationToken ct);
}

public interface INavigationRuntimePolicy
{
    Task<NavigationRuntimeResponse> ApplyAsync(string tenant, NavigationRuntimeResponse runtime, CancellationToken ct);
}

public sealed partial class ResourceNavigationService : IOwnedNavigationService
{
    public async Task<OwnedNavigationOptions> ReadAsync(string tenant, string ownerModule, string target, CancellationToken ct)
        => OwnedOptions(await _store.LoadAsync(tenant, ct), ownerModule, target);

    public async Task<OwnedNavigationOptions> MountAsync(string tenant, string actor, string ownerModule, string target, string uiResource, OwnedNavigationCommand command, CancellationToken ct)
    {
        var state = await _store.LoadAsync(tenant, ct);
        EnsureExpectedRevision(state, command.ExpectedRevision);
        var nodes = ActiveNodes(state).ToList();
        var resource = state.Resources.SingleOrDefault(r => r.NId == uiResource && r.OwnerModuleNId == ownerModule && r.Status == UiResourceStatus.Active)
            ?? throw new ValidationException("The ownerModule runtime resource is unavailable.");
        if (resource.RequiredPermissionNId is null || !state.PermissionReceipts.Any(r => r.ModuleNId == ownerModule && r.Verified))
            throw new ValidationException("The ownerModule permission manifest is not verified.");
        if (!nodes.Any(n => n.NodeNId == command.ParentNodeNId && n.Kind == NavigationNodeKind.Group && n.OwnedTarget is null && n.VisibleTerminals.Contains(UiTerminal.Pc)))
            throw new ValidationException("Choose a published PC navigation group.");
        if (string.IsNullOrWhiteSpace(command.Label) || command.Label.Trim().Length > 128 || command.DisplayOrder is < 0 or > 100000)
            throw new ValidationException("Invalid menu label or display order.");
        var current = command.NodeNId is null ? null : nodes.SingleOrDefault(n => n.NodeNId == command.NodeNId);
        if (command.NodeNId is not null && current?.OwnedTarget is null || current is not null && (current.OwnedTarget!.ModuleNId != ownerModule || current.OwnedTarget.ResourceNId != target))
            throw new ValidationException("The navigation link does not belong to this resource.");
        if (current is null && nodes.Count(n => n.OwnedTarget is { Detached: false } o && o.ModuleNId == ownerModule && o.ResourceNId == target) >= 16)
            throw new ValidationException("A resource supports at most 16 menu entries.");
        var nodeId = current?.NodeNId ?? $"owned.{Guid.NewGuid():N}";
        var draft = state.DraftNodes.SingleOrDefault(n => n.NId == nodeId);
        if (draft is null)
        {
            draft = NavigationNode.CreateLink(tenant, nodeId, command.Label.Trim(), command.ParentNodeNId, "PLATFORM_NAVIGATION", uiResource, null, [UiTerminal.Pc], command.IconKey);
            state = state with { DraftNodes = state.DraftNodes.Append(draft).ToArray() };
        }
        else
        {
            draft.Restore(); draft.UpdateLabel(command.Label.Trim()); draft.UpdateIconKey(command.IconKey);
            draft.UpdateConfiguration(command.ParentNodeNId, uiResource, null, [UiTerminal.Pc]);
        }
        draft.SetDisplayOrder(command.DisplayOrder);
        var published = new PublishedNavigationNode(nodeId, command.ParentNodeNId, NavigationNodeKind.Link, command.Label.Trim(), draft.IconKey, uiResource, resource.RouteName, resource.RequiredPermissionNId, null, command.DisplayOrder, [UiTerminal.Pc])
            { OwnedTarget = new(ownerModule, target) };
        nodes.RemoveAll(n => n.NodeNId == nodeId); nodes.Add(published);
        var next = await CommitOwnedAsync(state, nodes, actor, nodeId, "navigation.owned.mount", ct);
        return OwnedOptions(next, ownerModule, target);
    }

    public async Task<OwnedNavigationOptions> DetachAsync(string tenant, string actor, string ownerModule, string target, string? nodeNId, long? expectedRevision, CancellationToken ct)
    {
        var state = await _store.LoadAsync(tenant, ct);
        if (expectedRevision.HasValue) EnsureExpectedRevision(state, expectedRevision);
        var nodes = ActiveNodes(state).ToArray(); var changed = false;
        for (var i = 0; i < nodes.Length; i++)
        {
            var node = nodes[i];
            if (node.OwnedTarget is not { } owner || owner.ModuleNId != ownerModule || owner.ResourceNId != target || nodeNId is not null && node.NodeNId != nodeNId) continue;
            if (!owner.Detached) { nodes[i] = node with { OwnedTarget = owner with { Detached = true } }; changed = true; }
            state.DraftNodes.SingleOrDefault(n => n.NId == node.NodeNId)?.Retire();
        }
        if (changed) state = await CommitOwnedAsync(state, nodes, actor, nodeNId ?? target, "navigation.owned.detach", ct);
        return OwnedOptions(state, ownerModule, target);
    }

    private static IReadOnlyCollection<PublishedNavigationNode> ActiveNodes(ControlPlaneSnapshot state)
        => state.Snapshots.FirstOrDefault(s => s.Revision == state.ActiveSnapshotRevision)?.Nodes ?? [];

    private static OwnedNavigationOptions OwnedOptions(ControlPlaneSnapshot state, string ownerModule, string target)
    {
        var nodes = ActiveNodes(state);
        string Path(PublishedNavigationNode node, HashSet<string>? seen = null)
        {
            seen ??= new(StringComparer.Ordinal); if (!seen.Add(node.NodeNId)) return node.Label;
            var parent = nodes.FirstOrDefault(n => n.NodeNId == node.ParentNodeNId);
            return parent is null ? node.Label : Path(parent, seen) + " / " + node.Label;
        }
        return new(state.Revision,
            nodes.Where(n => n.OwnedTarget is { } o && o.ModuleNId == ownerModule && o.ResourceNId == target).Select(n => new OwnedNavigationLink(n.NodeNId, n.ParentNodeNId ?? "", n.Label, n.IconKey, n.DisplayOrder, Path(n), n.OwnedTarget!.Detached)).ToArray(),
            nodes.Where(n => n.Kind == NavigationNodeKind.Group && n.VisibleTerminals.Contains(UiTerminal.Pc)).ToDictionary(n => n.NodeNId, n => Path(n)));
    }

    private async Task<ControlPlaneSnapshot> CommitOwnedAsync(ControlPlaneSnapshot state, IReadOnlyCollection<PublishedNavigationNode> nodes, string actor, string target, string action, CancellationToken ct)
    {
        var revision = state.Revision + 1;
        var snapshot = new PublishedNavigationSnapshot(revision, DateTimeOffset.UtcNow, nodes, NavigationSnapshotChecksum.Compute(nodes));
        var next = state with { Snapshots = state.Snapshots.Append(snapshot).ToArray(), ActiveSnapshotRevision = revision, PreviousSnapshotRevision = state.ActiveSnapshotRevision };
        await _store.CommitAsync(next, state.Revision, Commit("SystemData.NavigationPublished.v1", state.TenantNId, $"{{\"revision\":{revision}}}", action, "NavigationNode", target, actor), ct);
        await InvalidateNavigationAsync(state.TenantNId, ct);
        return next with { Revision = revision };
    }

    // Normal navigation publication may edit labels/parents, but cannot revive detached owned links.
    private static PublishedNavigationNode[] PreserveOwnedTargets(ControlPlaneSnapshot state, IEnumerable<PublishedNavigationNode> proposed, bool rollback = false)
    {
        var current = ActiveNodes(state).Where(n => n.OwnedTarget is not null).ToDictionary(n => n.NodeNId);
        var result = proposed.Where(n => n.OwnedTarget is null || current.ContainsKey(n.NodeNId)).Select(n => current.TryGetValue(n.NodeNId, out var own)
            ? rollback ? own : n with { OwnedTarget = own.OwnedTarget } : n).ToList();
        foreach (var own in current.Values.Where(n => result.All(r => r.NodeNId != n.NodeNId)))
            result.Add(rollback ? own : own with { OwnedTarget = own.OwnedTarget! with { Detached = true } });
        return result.ToArray();
    }
}
