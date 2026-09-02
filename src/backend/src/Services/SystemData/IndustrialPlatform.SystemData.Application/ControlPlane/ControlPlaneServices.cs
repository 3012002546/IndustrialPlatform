using System.Globalization;
using System.Net;
using IndustrialPlatform.SharedKernel.Exceptions;
using IndustrialPlatform.SystemData.Application.Administration;
using IndustrialPlatform.SystemData.Application.Auditing;
using IndustrialPlatform.SystemData.Application.IdentityDirectory;
using IndustrialPlatform.SystemData.Application.Organizations;
using IndustrialPlatform.SystemData.Application.Reliability;
using IndustrialPlatform.SystemData.Contracts.ControlPlane;
using IndustrialPlatform.SystemData.Domain.ControlPlane;
using IndustrialPlatform.SystemData.Domain.Organizations;
using Microsoft.Extensions.Options;

namespace IndustrialPlatform.SystemData.Application.ControlPlane;

public sealed record PublishedNavigationSnapshot(long Revision, DateTimeOffset PublishedOn, IReadOnlyCollection<PublishedNavigationNode> Nodes, string? Checksum = null);
public sealed record PublishedNavigationNode(string NodeNId, string? ParentNodeNId, NavigationNodeKind Kind, string Label, string? IconKey, string? ResourceNId, string? RouteName, string? RequiredPermissionNId, string? FeatureNId, int DisplayOrder, IReadOnlyCollection<UiTerminal> VisibleTerminals);

public sealed class ControlPlaneOptions
{
    public const string SectionName = "SystemData:ControlPlane";
    public IReadOnlyCollection<string> EmergencyDisabledFeatureNIds { get; init; } = [];
    public bool EnableRemoteManifestRegistration { get; init; }
}

public interface IResourceNavigationService
{
    Task<IReadOnlyCollection<ModuleManifestResponse>> RegisterManifestAsync(string tenantNId, RegisterModuleManifestRequest request, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<UiResourceResponse>> ListResourcesAsync(string tenantNId, CancellationToken cancellationToken);
    Task<NavigationDraftResponse> GetDraftAsync(string tenantNId, CancellationToken cancellationToken);
    Task<UiResourceResponse> RegisterResourceAsync(string tenantNId, RegisterUiResourceRequest request, CancellationToken cancellationToken);
    Task<NavigationNodeResponse> AddNodeAsync(string tenantNId, CreateNavigationNodeRequest request, CancellationToken cancellationToken);
    Task<NavigationNodeResponse> UpdateNodeAsync(string tenantNId, string nodeNId, UpdateNavigationNodeRequest request, CancellationToken cancellationToken);
    Task DeleteNodeAsync(string tenantNId, string nodeNId, CancellationToken cancellationToken);
    Task<NavigationValidationResponse> ValidateAsync(string tenantNId, CancellationToken cancellationToken);
    Task<long> PublishAsync(string tenantNId, string actorUserNId, CancellationToken cancellationToken);
    Task<long> RollbackAsync(string tenantNId, string actorUserNId, CancellationToken cancellationToken);
    Task<NavigationRuntimeResponse> RuntimeAsync(string tenantNId, UiTerminal terminal, CancellationToken cancellationToken);
}

public sealed class ResourceNavigationService : IResourceNavigationService
{
    private readonly IControlPlaneStore _store;
    private readonly IIdentityPermissionRegistry _permissionRegistry;
    private readonly RuntimeSnapshotLoader _runtime;

    public ResourceNavigationService(IControlPlaneStore store, IIdentityPermissionRegistry permissionRegistry, RuntimeSnapshotLoader? runtime = null)
    {
        _store = store;
        _permissionRegistry = permissionRegistry;
        _runtime = runtime ?? new RuntimeSnapshotLoader(store);
    }

    public async Task<IReadOnlyCollection<ModuleManifestResponse>> RegisterManifestAsync(string tenantNId, RegisterModuleManifestRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var module = Required(request.ModuleNId);
        var version = Required(request.ManifestVersion);
        var checksum = Required(request.Checksum);
        var declarations = (request.Permissions ?? []).Select(x => new PermissionManifestEntry(
            Required(x.PermissionNId), Required(x.Name), Required(x.ResourceType), string.IsNullOrWhiteSpace(x.ParentPermissionNId) ? null : x.ParentPermissionNId.Trim())).ToArray();
        if (declarations.Length == 0)
            declarations = (request.PermissionNIds ?? []).Select(x => { var id = Required(x); return new PermissionManifestEntry(id, id, "permission", null); }).ToArray();
        var permissions = declarations.Select(x => x.PermissionNId).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (request.PermissionNIds is { Count: > 0 } ids && !ids.Select(Required).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).SequenceEqual(permissions.OrderBy(x => x, StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase))
            throw new ValidationException("PermissionNIds and PermissionManifest declarations must match.");
        var permissionManifest = new PermissionManifestV1(module, version, checksum, declarations);
        var resourceDeclarations = (request.Resources ?? []).Select(x => new ModuleResourceDeclaration(Required(x.ResourceNId), version, Required(x.Type), Required(x.Name), x.RouteName?.Trim(), x.RequiredPermissionNId?.Trim(), ParseTerminals(x.SupportedTerminals))).ToArray();
        var featureDeclarations = (request.Features ?? []).Select(x => new ModuleFeatureDeclaration(Required(x.FeatureNId), Required(x.Name), x.Description?.Trim(), x.DefaultEnabled)).ToArray();
        var state = await _store.LoadAsync(tenantNId, cancellationToken);
        var existing = state.Manifests.FirstOrDefault(x => x.ModuleNId.Equals(module, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            var resourcesAlreadyApplied = resourceDeclarations.All(declaration => state.Resources.Any(resource => resource.NId.Equals(declaration.ResourceNId, StringComparison.OrdinalIgnoreCase) && resource.ManifestVersion == declaration.ManifestVersion && resource.RouteName == declaration.RouteName && resource.Type.ToString().Equals(declaration.Type, StringComparison.OrdinalIgnoreCase) && resource.RequiredPermissionNId == declaration.RequiredPermissionNId && resource.SupportedTerminals.SequenceEqual(declaration.SupportedTerminals)));
            var featuresAlreadyApplied = featureDeclarations.All(declaration => state.Features.Any(feature => feature.NId.Equals(declaration.FeatureNId, StringComparison.OrdinalIgnoreCase) && feature.Name == declaration.Name && feature.Description == declaration.Description && feature.DefaultEnabled == declaration.DefaultEnabled));
            if (existing.ManifestVersion == version && existing.Checksum == checksum && existing.PermissionNIds.SequenceEqual(permissions, StringComparer.OrdinalIgnoreCase) && existing.PermissionDeclarations().SequenceEqual(declarations) && resourcesAlreadyApplied && featuresAlreadyApplied)
                return state.Manifests.Select(ToManifest).ToArray();
            if (existing.ManifestVersion == version && !existing.Checksum.Equals(checksum, StringComparison.OrdinalIgnoreCase))
                throw new ValidationException("Module manifest checksum conflict.");
        }

        var receipt = await _permissionRegistry.VerifyAsync(permissionManifest, cancellationToken)
            ?? throw new ValidationException("Identity permission registry is unavailable; manifest registration is denied.");
        if (!receipt.Verified || !receipt.Checksum.Equals(checksum, StringComparison.OrdinalIgnoreCase))
            throw new ValidationException("Identity did not verify the module permission manifest.");

        var manifest = new ModuleManifestState(tenantNId, module, version, checksum, permissions, receipt.ManifestVersion, receipt.Checksum, receipt.VerifiedOn) { PermissionDeclarationItems = declarations, ResourceDeclarations = resourceDeclarations, FeatureDeclarations = featureDeclarations };
        var manifests = state.Manifests.Where(x => !x.ModuleNId.Equals(module, StringComparison.OrdinalIgnoreCase)).Append(manifest).ToArray();
        var receipts = state.PermissionReceipts.Where(x => !x.ModuleNId.Equals(module, StringComparison.OrdinalIgnoreCase)).Append(new PermissionReceipt(module, receipt.ManifestVersion, receipt.Checksum, receipt.Verified)).ToArray();
        var resources = state.Resources.ToList();
        foreach (var declaration in request.Resources ?? [])
        {
            var resource = UiResource.Create(tenantNId, Required(declaration.ResourceNId), module, version, Parse<UiResourceType>(declaration.Type), Required(declaration.Name), declaration.RouteName, declaration.RequiredPermissionNId, ParseTerminals(declaration.SupportedTerminals));
            if (resource.RequiredPermissionNId is null || !permissions.Contains(resource.RequiredPermissionNId, StringComparer.OrdinalIgnoreCase)) throw new ValidationException("Manifest resource permission is not declared by the manifest.");
            if (resources.Any(x => x.NId.Equals(resource.NId, StringComparison.OrdinalIgnoreCase))) throw new ValidationException("ResourceNId cannot be reused.");
            resources.Add(resource);
        }
        var features = state.Features.ToList();
        foreach (var declaration in request.Features ?? [])
        {
            var feature = FeatureDefinition.Create(tenantNId, Required(declaration.FeatureNId), module, Required(declaration.Name), declaration.DefaultEnabled, declaration.Description);
            if (features.Any(x => x.NId.Equals(feature.NId, StringComparison.OrdinalIgnoreCase))) throw new ValidationException("FeatureNId cannot be reused.");
            features.Add(feature);
        }
        await _store.CommitAsync(state with { Manifests = manifests, PermissionReceipts = receipts, Resources = resources, Features = features }, state.Revision,
            Commit("SystemData.ModuleManifestRegistered.v1", tenantNId, $"{{\"moduleNId\":\"{Json(module)}\",\"manifestVersion\":\"{Json(version)}\",\"checksum\":\"{Json(checksum)}\"}}", "module-manifest.register", "ModuleManifest", module), cancellationToken);
        await _runtime.InvalidateAsync(tenantNId, "navigation:pc", cancellationToken);
        await _runtime.InvalidateAsync(tenantNId, "navigation:pda", cancellationToken);
        await _runtime.InvalidateAsync(tenantNId, "navigation:mobile", cancellationToken);
        await _runtime.InvalidateAsync(tenantNId, "features", cancellationToken);
        await _runtime.InvalidateAsync(tenantNId, "service-catalog", cancellationToken);
        return manifests.Select(ToManifest).ToArray();
    }

    public async Task<IReadOnlyCollection<UiResourceResponse>> ListResourcesAsync(string tenantNId, CancellationToken cancellationToken)
    {
        var state = await _store.LoadAsync(tenantNId, cancellationToken);
        return state.Resources.Select(ToResource).ToArray();
    }

    public async Task<UiResourceResponse> RegisterResourceAsync(string tenantNId, RegisterUiResourceRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var resource = UiResource.Create(tenantNId, Required(request.ResourceNId), Required(request.OwnerModuleNId), Required(request.ManifestVersion), Parse<UiResourceType>(request.Type), Required(request.Name), request.RouteName, request.RequiredPermissionNId, ParseTerminals(request.SupportedTerminals));
        var state = await _store.LoadAsync(tenantNId, cancellationToken);
        if (state.Resources.Any(x => x.NId.Equals(resource.NId, StringComparison.OrdinalIgnoreCase))) throw new ValidationException("ResourceNId cannot be reused.");
        var manifest = state.Manifests.FirstOrDefault(x => x.ModuleNId.Equals(resource.OwnerModuleNId, StringComparison.OrdinalIgnoreCase) && x.ManifestVersion == resource.ManifestVersion);
        if (manifest is null || manifest.PermissionReceiptVersion is null || manifest.PermissionReceiptChecksum is null || !manifest.PermissionReceiptChecksum.Equals(manifest.Checksum, StringComparison.OrdinalIgnoreCase))
            throw new ValidationException("Resource requires a verified module permission manifest.");
        if (resource.RequiredPermissionNId is null || !manifest.PermissionDeclarations().Any(x => x.PermissionNId.Equals(resource.RequiredPermissionNId, StringComparison.OrdinalIgnoreCase)))
            throw new ValidationException("Resource permission is not declared by its module manifest.");
        if (manifest.ResourceDeclarations.Count > 0)
        {
            var declaration = manifest.ResourceDeclarations.FirstOrDefault(x => x.ResourceNId.Equals(resource.NId, StringComparison.OrdinalIgnoreCase));
            if (declaration is null || declaration.ManifestVersion != resource.ManifestVersion || declaration.RouteName != resource.RouteName || !declaration.Type.Equals(resource.Type.ToString(), StringComparison.OrdinalIgnoreCase) || declaration.RequiredPermissionNId != resource.RequiredPermissionNId || !declaration.SupportedTerminals.SequenceEqual(resource.SupportedTerminals))
                throw new ValidationException("Resource is not declared by the trusted module manifest.");
        }
        var next = state with { Resources = state.Resources.Append(resource).ToArray() };
        await _store.CommitAsync(next, state.Revision, Commit("SystemData.ResourceChanged.v1", tenantNId, $"{{\"resourceNId\":\"{Json(resource.NId)}\"}}", "resource.register", "UiResource", resource.NId), cancellationToken);
        await _runtime.InvalidateAsync(tenantNId, "navigation:pc", cancellationToken);
        await _runtime.InvalidateAsync(tenantNId, "navigation:pda", cancellationToken);
        await _runtime.InvalidateAsync(tenantNId, "navigation:mobile", cancellationToken);
        return ToResource(resource);
    }

    public async Task<NavigationDraftResponse> GetDraftAsync(string tenantNId, CancellationToken cancellationToken)
    {
        var state = await _store.LoadAsync(tenantNId, cancellationToken);
        return new NavigationDraftResponse { DraftRevision = state.Revision, Nodes = BuildDraft(state.DraftNodes) };
    }

    public async Task<NavigationNodeResponse> AddNodeAsync(string tenantNId, CreateNavigationNodeRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var node = ParseNode(tenantNId, request);
        var state = await _store.LoadAsync(tenantNId, cancellationToken);
        if (state.DraftNodes.Any(x => x.NId.Equals(node.NId, StringComparison.OrdinalIgnoreCase))) throw new ValidationException("Navigation node id cannot be reused.");
        await _store.CommitAsync(state with { DraftNodes = state.DraftNodes.Append(node).ToArray() }, state.Revision,
            Commit("SystemData.NavigationDraftChanged.v1", tenantNId, $"{{\"nodeNId\":\"{Json(node.NId)}\"}}", "navigation.node.add", "NavigationNode", node.NId), cancellationToken);
        await InvalidateNavigationAsync(tenantNId, cancellationToken);
        return ToNode(node, null);
    }

    public async Task<NavigationNodeResponse> UpdateNodeAsync(string tenantNId, string nodeNId, UpdateNavigationNodeRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var state = await _store.LoadAsync(tenantNId, cancellationToken);
        var node = state.DraftNodes.FirstOrDefault(x => x.NId.Equals(nodeNId, StringComparison.OrdinalIgnoreCase)) ?? throw new ValidationException("Navigation node was not found.");
        if (request.Label is not null) node.UpdateLabel(request.Label);
        if (request.IconKey is not null) node.UpdateIconKey(request.IconKey);
        if (request.DisplayOrder is { } order) node.SetDisplayOrder(order);
        await _store.CommitAsync(state, state.Revision,
            Commit("SystemData.NavigationDraftChanged.v1", tenantNId, $"{{\"nodeNId\":\"{Json(node.NId)}\"}}", "navigation.node.update", "NavigationNode", node.NId), cancellationToken);
        await InvalidateNavigationAsync(tenantNId, cancellationToken);
        return ToNode(node, state.DraftNodes);
    }

    public async Task DeleteNodeAsync(string tenantNId, string nodeNId, CancellationToken cancellationToken)
    {
        var state = await _store.LoadAsync(tenantNId, cancellationToken);
        var node = state.DraftNodes.FirstOrDefault(x => x.NId.Equals(nodeNId, StringComparison.OrdinalIgnoreCase)) ?? throw new ValidationException("Navigation node was not found.");
        if (state.DraftNodes.Any(x => x.ParentNodeNId?.Equals(nodeNId, StringComparison.OrdinalIgnoreCase) == true && x.Status == NavigationNodeStatus.Active)) throw new ValidationException("Navigation node with active children cannot be deleted.");
        node.Retire();
        await _store.CommitAsync(state, state.Revision,
            Commit("SystemData.NavigationDraftChanged.v1", tenantNId, $"{{\"nodeNId\":\"{Json(node.NId)}\"}}", "navigation.node.delete", "NavigationNode", node.NId), cancellationToken);
        await InvalidateNavigationAsync(tenantNId, cancellationToken);
    }

    public async Task<NavigationValidationResponse> ValidateAsync(string tenantNId, CancellationToken cancellationToken)
    {
        var state = await _store.LoadAsync(tenantNId, cancellationToken);
        return ToValidation(ValidateState(state));
    }

    public async Task<long> PublishAsync(string tenantNId, string actorUserNId, CancellationToken cancellationToken)
    {
        var state = await _store.LoadAsync(tenantNId, cancellationToken);
        var validation = ValidateState(state);
        if (!validation.IsValid) throw new ValidationException(string.Join("; ", validation.Errors.Select(x => x.Code)));
        var revision = state.Revision + 1;
        var publishedNodes = BuildPublishedNodes(state);
        var snapshot = new PublishedNavigationSnapshot(revision, DateTimeOffset.UtcNow, publishedNodes, NavigationSnapshotChecksum.Compute(publishedNodes));
        var snapshots = state.Snapshots.Append(snapshot).ToArray();
        var next = state with { Revision = revision, Snapshots = snapshots, ActiveSnapshotRevision = revision, PreviousSnapshotRevision = state.ActiveSnapshotRevision };
        await _store.CommitAsync(next, state.Revision, Commit("SystemData.NavigationPublished.v1", tenantNId, $"{{\"revision\":{revision}}}", "navigation.publish", "NavigationSnapshot", revision.ToString(CultureInfo.InvariantCulture), actorUserNId), cancellationToken);
        await InvalidateNavigationAsync(tenantNId, cancellationToken);
        return revision;
    }

    public async Task<long> RollbackAsync(string tenantNId, string actorUserNId, CancellationToken cancellationToken)
    {
        var state = await _store.LoadAsync(tenantNId, cancellationToken);
        var previousRevision = state.PreviousSnapshotRevision ?? throw new ValidationException("No previous navigation snapshot is available.");
        var previous = state.Snapshots.FirstOrDefault(x => x.Revision == previousRevision) ?? throw new ValidationException("Previous navigation snapshot is missing.");
        var revision = state.Revision + 1;
        var rollbackNodes = previous.Nodes.ToArray();
        var rollback = previous with { Revision = revision, PublishedOn = DateTimeOffset.UtcNow, Nodes = rollbackNodes, Checksum = NavigationSnapshotChecksum.Compute(rollbackNodes) };
        var next = state with { Revision = revision, Snapshots = state.Snapshots.Append(rollback).ToArray(), ActiveSnapshotRevision = revision, PreviousSnapshotRevision = state.ActiveSnapshotRevision };
        await _store.CommitAsync(next, state.Revision, Commit("SystemData.NavigationRolledBack.v1", tenantNId, $"{{\"revision\":{revision},\"fromRevision\":{previousRevision}}}", "navigation.rollback", "NavigationSnapshot", revision.ToString(CultureInfo.InvariantCulture), actorUserNId), cancellationToken);
        await InvalidateNavigationAsync(tenantNId, cancellationToken);
        return revision;
    }

    public async Task<NavigationRuntimeResponse> RuntimeAsync(string tenantNId, UiTerminal terminal, CancellationToken cancellationToken)
    {
        var result = await _runtime.LoadAsync(tenantNId, $"navigation:{terminal.ToString().ToLowerInvariant()}", state =>
        {
            if (state.ActiveSnapshotRevision is null)
                return new NavigationRuntimeResponse { Revision = state.Revision, Configured = false };

            var snapshot = state.Snapshots.FirstOrDefault(x => x.Revision == state.ActiveSnapshotRevision.Value);
            if (snapshot is not null && snapshot.Checksum is not null && !snapshot.Checksum.Equals(NavigationSnapshotChecksum.Compute(snapshot.Nodes), StringComparison.OrdinalIgnoreCase))
            {
                var fallbackRevision = state.PreviousSnapshotRevision;
                snapshot = fallbackRevision is { } previousRevision ? state.Snapshots.FirstOrDefault(x => x.Revision == previousRevision) : null;
                if (snapshot is not null && snapshot.Checksum is not null && !snapshot.Checksum.Equals(NavigationSnapshotChecksum.Compute(snapshot.Nodes), StringComparison.OrdinalIgnoreCase)) snapshot = null;
            }
            if (snapshot is null) throw new ValidationException("Published navigation snapshot is missing.");
            var resources = state.Resources.ToDictionary(x => x.NId, StringComparer.OrdinalIgnoreCase);
            var nodes = snapshot.Nodes.Where(x => x.VisibleTerminals.Contains(terminal)).Where(x => x.ResourceNId is null || (resources.TryGetValue(x.ResourceNId, out var r) && r.Status == UiResourceStatus.Active)).ToArray();
            return new NavigationRuntimeResponse { Revision = snapshot.Revision, Configured = true, Nodes = BuildRuntime(nodes, null), Degraded = false };
        }, cancellationToken);
        return result.Value with { Degraded = result.Degraded };
    }

    private static NavigationValidationResult ValidateState(ControlPlaneSnapshot state)
    {
        var receipts = state.PermissionReceipts.ToArray();
        var errors = new List<NavigationValidationError>();
        var baseResult = NavigationValidator.Validate(state.DraftNodes, state.Resources, state.Features, receipts);
        errors.AddRange(baseResult.Errors);
        foreach (var node in state.DraftNodes.Where(x => x.Status == NavigationNodeStatus.Active && x.ResourceNId is not null))
        {
            var resource = state.Resources.FirstOrDefault(x => x.NId.Equals(node.ResourceNId, StringComparison.OrdinalIgnoreCase));
            var manifest = state.Manifests.FirstOrDefault(x => x.ModuleNId.Equals(resource?.OwnerModuleNId, StringComparison.OrdinalIgnoreCase) && x.ManifestVersion == resource.ManifestVersion);
            if (resource is not null && (manifest is null || !receipts.Any(x => x.ModuleNId.Equals(resource.OwnerModuleNId, StringComparison.OrdinalIgnoreCase) && x.ManifestVersion == manifest.ManifestVersion && x.Checksum.Equals(manifest.Checksum, StringComparison.OrdinalIgnoreCase) && x.Verified) || !manifest.PermissionDeclarations().Any(x => x.PermissionNId.Equals(resource.RequiredPermissionNId, StringComparison.OrdinalIgnoreCase))))
                errors.Add(new("PERMISSION_UNVERIFIED", "Resource permission registration is not verified.", node.NId));
        }
        return new NavigationValidationResult(errors);
    }

    private async Task InvalidateNavigationAsync(string tenantNId, CancellationToken cancellationToken)
    {
        await _runtime.InvalidateAsync(tenantNId, "navigation:pc", cancellationToken);
        await _runtime.InvalidateAsync(tenantNId, "navigation:pda", cancellationToken);
        await _runtime.InvalidateAsync(tenantNId, "navigation:mobile", cancellationToken);
    }

    private static PublishedNavigationNode[] BuildPublishedNodes(ControlPlaneSnapshot state)
    {
        var resources = state.Resources.ToDictionary(x => x.NId, StringComparer.OrdinalIgnoreCase);
        return state.DraftNodes.Where(x => x.Status == NavigationNodeStatus.Active).Select(x => new PublishedNavigationNode(x.NId, x.ParentNodeNId, x.Kind, x.Label, x.IconKey, x.ResourceNId, x.ResourceNId is not null && resources.TryGetValue(x.ResourceNId, out var r) ? r.RouteName : null, x.ResourceNId is not null && resources.TryGetValue(x.ResourceNId, out r) ? r.RequiredPermissionNId : null, x.FeatureNId, x.DisplayOrder, x.VisibleTerminals)).ToArray();
    }

    private static NavigationNodeResponse[] BuildDraft(IReadOnlyCollection<NavigationNode> nodes) => nodes.Where(x => x.Status == NavigationNodeStatus.Active && x.ParentNodeNId is null).OrderBy(x => x.DisplayOrder).ThenBy(x => x.NId).Select(x => ToNode(x, nodes)).ToArray();
    private static NavigationRuntimeNodeResponse[] BuildRuntime(IReadOnlyCollection<PublishedNavigationNode> nodes, string? parent) => nodes.Where(x => string.Equals(x.ParentNodeNId, parent, StringComparison.OrdinalIgnoreCase)).OrderBy(x => x.DisplayOrder).ThenBy(x => x.NodeNId).Select(x => new NavigationRuntimeNodeResponse { NodeNId = x.NodeNId, Kind = x.Kind.ToString(), Label = x.Label, IconKey = x.IconKey, ResourceNId = x.ResourceNId, RouteName = x.RouteName, RequiredPermissionNId = x.RequiredPermissionNId, FeatureNId = x.FeatureNId, DisplayOrder = x.DisplayOrder, Children = BuildRuntime(nodes, x.NodeNId) }).ToArray();
    private static NavigationNodeResponse ToNode(NavigationNode node, IReadOnlyCollection<NavigationNode>? all) => new() { NodeNId = node.NId, Kind = node.Kind.ToString(), Label = node.Label, IconKey = node.IconKey, ParentNodeNId = node.ParentNodeNId, ResourceNId = node.ResourceNId, FeatureNId = node.FeatureNId, DisplayOrder = node.DisplayOrder, VisibleTerminals = node.VisibleTerminals.Select(x => x.ToString()).ToArray(), Status = node.Status.ToString(), Children = all?.Where(x => x.ParentNodeNId?.Equals(node.NId, StringComparison.OrdinalIgnoreCase) == true).OrderBy(x => x.DisplayOrder).ThenBy(x => x.NId).Select(x => ToNode(x, all)).ToArray() ?? [] };
    private static UiResourceResponse ToResource(UiResource resource) => new() { ResourceNId = resource.NId, OwnerModuleNId = resource.OwnerModuleNId, ManifestVersion = resource.ManifestVersion, Type = resource.Type.ToString(), Name = resource.Name, RouteName = resource.RouteName, RequiredPermissionNId = resource.RequiredPermissionNId, SupportedTerminals = resource.SupportedTerminals.Select(x => x.ToString()).ToArray(), Status = resource.Status.ToString() };
    private static ModuleManifestResponse ToManifest(ModuleManifestState manifest) => new() { ModuleNId = manifest.ModuleNId, ManifestVersion = manifest.ManifestVersion, Checksum = manifest.Checksum, PermissionNIds = manifest.PermissionNIds, Permissions = manifest.PermissionDeclarations().Select(x => new PermissionDeclarationRequest { PermissionNId = x.PermissionNId, Name = x.Name, ResourceType = x.ResourceType, ParentPermissionNId = x.ParentPermissionNId }).ToArray(), PermissionVerified = manifest.PermissionReceiptVersion is not null, PermissionVerifiedOn = manifest.PermissionVerifiedOn };
    private static NavigationValidationResponse ToValidation(NavigationValidationResult result) => new() { IsValid = result.IsValid, Errors = result.Errors.Select(x => new NavigationValidationErrorResponse { Code = x.Code, Message = x.Message, NodeNId = x.NodeNId }).ToArray() };
    private static ControlPlaneCommit Commit(string eventType, string tenant, string payload, string action, string objectType, string objectNId, string actor = "system") => new([new ControlPlaneEvent(Guid.NewGuid(), eventType, "v1", tenant, payload, DateTimeOffset.UtcNow)], [new LocalAuditEntry(tenant, actor, action, objectType, objectNId, null, null, payload, Guid.NewGuid().ToString("N"))]);
    private static NavigationNode ParseNode(string tenant, CreateNavigationNodeRequest request) { var kind = Parse<NavigationNodeKind>(request.Kind); var terminals = ParseTerminals(request.VisibleTerminals); var node = kind == NavigationNodeKind.Group ? NavigationNode.CreateGroup(tenant, Required(request.NodeNId), Required(request.Label), request.ParentNodeNId, request.NavigationSetNId ?? "PLATFORM_NAVIGATION", request.IconKey) : NavigationNode.CreateLink(tenant, Required(request.NodeNId), Required(request.Label), request.ParentNodeNId, request.NavigationSetNId ?? "PLATFORM_NAVIGATION", Required(request.ResourceNId), request.FeatureNId, terminals, request.IconKey); if (request.DisplayOrder is { } order) node.SetDisplayOrder(order); return node; }
    private static string Required(string? value) => string.IsNullOrWhiteSpace(value) ? throw new ValidationException("Required value is missing.") : value.Trim();
    private static string Json(string value) => value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
    private static T Parse<T>(string? value) where T : struct, Enum => Enum.TryParse<T>(value, true, out var parsed) ? parsed : throw new ValidationException($"Invalid {typeof(T).Name} value.");
    private static UiTerminal[] ParseTerminals(IReadOnlyCollection<string>? values) => values is null || values.Count == 0 ? throw new ValidationException("At least one terminal is required.") : values.Select(x => Parse<UiTerminal>(x)).Distinct().ToArray();
}

public interface IFeatureControlService
{
    Task<FeatureDefinitionResponse> RegisterAsync(string tenantNId, RegisterFeatureDefinitionRequest request, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<FeatureDefinitionResponse>> ListAsync(string tenantNId, CancellationToken cancellationToken);
    Task<FeatureDefinitionResponse> SetOverrideAsync(string tenantNId, string featureNId, SetFeatureOverrideRequest request, CancellationToken cancellationToken);
    Task<FeatureRuntimeResponse> RuntimeAsync(string tenantNId, CancellationToken cancellationToken);
}

public sealed class FeatureControlService : IFeatureControlService
{
    private readonly IControlPlaneStore _store;
    private readonly IReadOnlyCollection<string> _emergencyDisabled;
    private readonly RuntimeSnapshotLoader _runtime;
    public FeatureControlService(IControlPlaneStore store, IIdentityPermissionRegistry _, IOptions<ControlPlaneOptions>? options = null, RuntimeSnapshotLoader? runtime = null) { _store = store; _emergencyDisabled = options?.Value.EmergencyDisabledFeatureNIds ?? []; _runtime = runtime ?? new RuntimeSnapshotLoader(store); }
    public async Task<FeatureDefinitionResponse> RegisterAsync(string tenantNId, RegisterFeatureDefinitionRequest request, CancellationToken cancellationToken) { ArgumentNullException.ThrowIfNull(request); var featureNId = Required(request.FeatureNId); var ownerModuleNId = Required(request.OwnerModuleNId); var name = Required(request.Name); var state = await _store.LoadAsync(tenantNId, cancellationToken); var existing = state.Features.FirstOrDefault(x => x.NId.Equals(featureNId, StringComparison.OrdinalIgnoreCase)); if (existing is not null) { if (existing.OwnerModuleNId.Equals(ownerModuleNId, StringComparison.OrdinalIgnoreCase) && existing.Name == name && existing.Description == request.Description?.Trim() && existing.DefaultEnabled == request.DefaultEnabled) return ToResponse(existing, state); throw new ValidationException("Feature declaration conflicts with an existing feature."); } var feature = FeatureDefinition.Create(tenantNId, featureNId, ownerModuleNId, name, request.DefaultEnabled, request.Description); var next = state with { Features = state.Features.Append(feature).ToArray() }; await _store.CommitAsync(next, state.Revision, new ControlPlaneCommit([new ControlPlaneEvent(Guid.NewGuid(), "SystemData.FeatureDeclared.v1", "v1", tenantNId, $"{{\"featureNId\":\"{featureNId}\",\"revision\":{feature.FeatureRevision}}}", DateTimeOffset.UtcNow)], [new LocalAuditEntry(tenantNId, "system", "feature.register", "Feature", featureNId, null, null, feature.Description, Guid.NewGuid().ToString("N"))]), cancellationToken); await _runtime.InvalidateAsync(tenantNId, "features", cancellationToken); return ToResponse(feature, next); }
    public async Task<IReadOnlyCollection<FeatureDefinitionResponse>> ListAsync(string tenantNId, CancellationToken cancellationToken) { var state = await _store.LoadAsync(tenantNId, cancellationToken); return state.Features.Select(x => ToResponse(x, state)).ToArray(); }
    public async Task<FeatureDefinitionResponse> SetOverrideAsync(string tenantNId, string featureNId, SetFeatureOverrideRequest request, CancellationToken cancellationToken) { var state = await _store.LoadAsync(tenantNId, cancellationToken); var feature = state.Features.FirstOrDefault(x => x.NId.Equals(featureNId, StringComparison.OrdinalIgnoreCase)) ?? throw new ValidationException("Feature was not found."); var mode = Parse<FeatureOverrideMode>(request.Mode); var revised = feature.WithRevision(feature.FeatureRevision + 1); var next = state with { Features = state.Features.Select(x => x.NId.Equals(featureNId, StringComparison.OrdinalIgnoreCase) ? revised : x).ToArray(), Overrides = state.Overrides.Where(x => !x.FeatureNId.Equals(featureNId, StringComparison.OrdinalIgnoreCase)).Append(FeatureOverride.Create(tenantNId, featureNId, mode, request.Reason)).ToArray() }; await _store.CommitAsync(next, state.Revision, new ControlPlaneCommit([new ControlPlaneEvent(Guid.NewGuid(), "SystemData.FeatureFlagsChanged.v1", "v1", tenantNId, $"{{\"featureNId\":\"{featureNId}\",\"revision\":{revised.FeatureRevision}}}", DateTimeOffset.UtcNow)], [new LocalAuditEntry(tenantNId, "system", "feature.override", "Feature", featureNId, request.Reason, null, mode.ToString(), Guid.NewGuid().ToString("N"))]), cancellationToken); await _runtime.InvalidateAsync(tenantNId, "features", cancellationToken); return ToResponse(revised, next); }
    public async Task<FeatureRuntimeResponse> RuntimeAsync(string tenantNId, CancellationToken cancellationToken) { var result = await _runtime.LoadAsync(tenantNId, "features", state => new FeatureRuntimeResponse { Revision = state.Revision, Configured = state.Features.Count > 0, Items = state.Features.Select(x => new FeatureRuntimeItem(x.NId, x.Resolve(state.Overrides.FirstOrDefault(o => o.FeatureNId.Equals(x.NId, StringComparison.OrdinalIgnoreCase)), _emergencyDisabled))).ToArray() }, cancellationToken); return result.Value with { Degraded = result.Degraded }; }
    private FeatureDefinitionResponse ToResponse(FeatureDefinition feature, ControlPlaneSnapshot state) => new() { FeatureNId = feature.NId, OwnerModuleNId = feature.OwnerModuleNId, Name = feature.Name, Description = feature.Description, DefaultEnabled = feature.DefaultEnabled, Status = feature.Status.ToString(), FeatureRevision = feature.FeatureRevision, EffectiveEnabled = feature.Resolve(state.Overrides.FirstOrDefault(x => x.FeatureNId.Equals(feature.NId, StringComparison.OrdinalIgnoreCase)), _emergencyDisabled) };
    private static T Parse<T>(string? value) where T : struct, Enum => Enum.TryParse<T>(value, true, out var parsed) ? parsed : throw new ValidationException("Invalid feature override mode.");
    private static string Required(string? value) => string.IsNullOrWhiteSpace(value) ? throw new ValidationException("Feature declaration value is required.") : value.Trim();
}

public interface IServiceCatalogControlService
{
    Task<IReadOnlyCollection<ServiceCatalogResponse>> ListAsync(string tenantNId, CancellationToken cancellationToken);
    Task<ServiceCatalogResponse> CreateExternalAsync(string tenantNId, CreateExternalServiceCatalogRequest request, CancellationToken cancellationToken);
    Task<ServiceCatalogResponse> UpdateAsync(string tenantNId, string serviceNId, UpdateServiceCatalogRequest request, CancellationToken cancellationToken);
    Task<ServiceCatalogResponse> SetStatusAsync(string tenantNId, string serviceNId, SetCatalogStatusRequest request, CancellationToken cancellationToken);
    Task<ServiceCatalogRuntimeResponse> RuntimeAsync(string tenantNId, CancellationToken cancellationToken);
}

public sealed class ServiceCatalogControlService : IServiceCatalogControlService
{
    private readonly IControlPlaneStore _store;
    private readonly IAdministrativeOrganizationStore _organizations;
    private readonly IIdentityUserDirectory _users;
    private readonly RuntimeSnapshotLoader _runtime;
    public ServiceCatalogControlService(
        IControlPlaneStore store,
        IIdentityPermissionRegistry _,
        IAdministrativeOrganizationStore organizations,
        IIdentityUserDirectory users,
        RuntimeSnapshotLoader? runtime = null)
    {
        _store = store;
        _organizations = organizations;
        _users = users;
        _runtime = runtime ?? new RuntimeSnapshotLoader(store);
    }

    public async Task<IReadOnlyCollection<ServiceCatalogResponse>> ListAsync(string tenantNId, CancellationToken cancellationToken)
    {
        var state = await _store.LoadAsync(tenantNId, cancellationToken);
        return state.Catalog.Select(ToResponse).ToArray();
    }

    public async Task<ServiceCatalogResponse> CreateExternalAsync(string tenantNId, CreateExternalServiceCatalogRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var entry = ServiceCatalogEntry.CreateExternal(tenantNId, Required(request.ServiceNId), Required(request.Name), Required(request.EntryPoint), ParseTerminals(request.SupportedTerminals));
        entry.SetMetadata(request.Description, null);
        var owner = await ResolveOwnerAsync(tenantNId, request.OwnerOrganizationNId, request.TechnicalOwnerUserNId ?? request.TechnicalLeadUserNId, cancellationToken);
        if (owner is not null) entry.SetOwnerSnapshot(owner.Value.OrganizationNId, owner.Value.OrganizationName, owner.Value.UserNId, owner.Value.UserName, owner.Value.AuthVersion);
        var state = await _store.LoadAsync(tenantNId, cancellationToken);
        if (state.Catalog.Any(x => x.NId.Equals(entry.NId, StringComparison.OrdinalIgnoreCase))) throw new ValidationException("ServiceNId cannot be reused.");
        await _store.CommitAsync(state with { Catalog = state.Catalog.Append(entry).ToArray() }, state.Revision,
            new ControlPlaneCommit([new ControlPlaneEvent(Guid.NewGuid(), "SystemData.ServiceCatalogChanged.v1", "v1", tenantNId, $"{{\"serviceNId\":\"{entry.NId}\"}}", DateTimeOffset.UtcNow)], [new LocalAuditEntry(tenantNId, "system", "service-catalog.create", "ServiceCatalog", entry.NId, null, null, entry.EntryPoint, Guid.NewGuid().ToString("N"))]), cancellationToken);
        await _runtime.InvalidateAsync(tenantNId, "service-catalog", cancellationToken);
        return ToResponse(entry);
    }

    public async Task<ServiceCatalogResponse> UpdateAsync(string tenantNId, string serviceNId, UpdateServiceCatalogRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var state = await _store.LoadAsync(tenantNId, cancellationToken);
        var entry = state.Catalog.FirstOrDefault(x => x.NId.Equals(serviceNId, StringComparison.OrdinalIgnoreCase)) ?? throw new ValidationException("Service catalog entry was not found.");
        if (request.Name is not null) entry.UpdateName(request.Name);
        if (request.Description is not null) entry.SetMetadata(request.Description, entry.GatewayPathPrefix);
        if (request.EntryPoint is not null)
        {
            if (entry.Kind == ServiceCatalogKind.Platform) throw new ValidationException("Platform EntryPoint is protected by its service manifest.");
            try { entry.UpdateExternalEntryPoint(request.EntryPoint); }
            catch (ArgumentException exception) { throw new ValidationException(exception.Message); }
        }

        if (request.OwnerOrganizationNId is not null || request.TechnicalOwnerUserNId is not null || request.TechnicalLeadUserNId is not null)
        {
            var owner = await ResolveOwnerAsync(tenantNId, request.OwnerOrganizationNId ?? entry.OwnerOrganizationNId, request.TechnicalOwnerUserNId ?? request.TechnicalLeadUserNId ?? entry.TechnicalOwnerUserNId, cancellationToken)
                ?? throw new ValidationException("Owner organization and technical lead are required together.");
            entry.SetOwnerSnapshot(owner.OrganizationNId, owner.OrganizationName, owner.UserNId, owner.UserName, owner.AuthVersion);
        }

        if (request.Status is not null) ApplyStatus(entry, Parse<ServiceCatalogStatus>(request.Status));
        await _store.CommitAsync(state, state.Revision,
            new ControlPlaneCommit([new ControlPlaneEvent(Guid.NewGuid(), "SystemData.ServiceCatalogChanged.v1", "v1", tenantNId, $"{{\"serviceNId\":\"{entry.NId}\"}}", DateTimeOffset.UtcNow)], [new LocalAuditEntry(tenantNId, "system", "service-catalog.update", "ServiceCatalog", entry.NId, null, null, entry.Description, Guid.NewGuid().ToString("N"))]), cancellationToken);
        await _runtime.InvalidateAsync(tenantNId, "service-catalog", cancellationToken);
        return ToResponse(entry);
    }
    public async Task<ServiceCatalogResponse> SetStatusAsync(string tenantNId, string serviceNId, SetCatalogStatusRequest request, CancellationToken cancellationToken) { var state = await _store.LoadAsync(tenantNId, cancellationToken); var entry = state.Catalog.FirstOrDefault(x => x.NId.Equals(serviceNId, StringComparison.OrdinalIgnoreCase)) ?? throw new ValidationException("Service catalog entry was not found."); var status = Parse<ServiceCatalogStatus>(request.Status); if (status == ServiceCatalogStatus.Retired) entry.Retire(); else if (status == ServiceCatalogStatus.Inactive) entry.SetInactive(); else entry.SetActive(); await _store.CommitAsync(state, state.Revision, new ControlPlaneCommit([new ControlPlaneEvent(Guid.NewGuid(), "SystemData.ServiceCatalogChanged.v1", "v1", tenantNId, $"{{\"serviceNId\":\"{entry.NId}\",\"status\":\"{status}\"}}", DateTimeOffset.UtcNow)], [new LocalAuditEntry(tenantNId, "system", "service-catalog.status", "ServiceCatalog", entry.NId, null, null, status.ToString(), Guid.NewGuid().ToString("N"))]), cancellationToken); await _runtime.InvalidateAsync(tenantNId, "service-catalog", cancellationToken); return ToResponse(entry); }
    public async Task<ServiceCatalogRuntimeResponse> RuntimeAsync(string tenantNId, CancellationToken cancellationToken) { var result = await _runtime.LoadAsync(tenantNId, "service-catalog", state => new ServiceCatalogRuntimeResponse { Revision = state.Revision, Configured = state.Catalog.Count > 0, Items = state.Catalog.Where(x => x.Status == ServiceCatalogStatus.Active).Select(ToResponse).ToArray() }, cancellationToken); return result.Value with { Degraded = result.Degraded, Items = result.Value.Items.Select(x => x with { Degraded = result.Degraded }).ToArray() }; }
    private async Task<(string OrganizationNId, string OrganizationName, string UserNId, string UserName, string? AuthVersion)?> ResolveOwnerAsync(string tenantNId, string? organizationNId, string? userNId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(organizationNId) && string.IsNullOrWhiteSpace(userNId)) return null;
        if (string.IsNullOrWhiteSpace(organizationNId) || string.IsNullOrWhiteSpace(userNId)) throw new ValidationException("Owner organization and technical lead are required together.");
        var organization = await _organizations.GetAsync(tenantNId, organizationNId.Trim(), cancellationToken);
        if (organization is null || organization.Status != OrganizationStatus.Active) throw new ValidationException("Owner organization must exist and be Active.");
        var user = await _users.GetAsync(tenantNId, userNId.Trim(), cancellationToken);
        if (user is null || !string.Equals(user.Status, "Active", StringComparison.OrdinalIgnoreCase)) throw new ValidationException("Technical lead user must exist and be Active in Identity.");
        return (organization.NId, PlainTextSnapshot(organization.Name), user.UserNId, PlainTextSnapshot(user.Name), user.AuthVersion);
    }

    private static void ApplyStatus(ServiceCatalogEntry entry, ServiceCatalogStatus status)
    {
        if (status == ServiceCatalogStatus.Retired) entry.Retire();
        else if (status == ServiceCatalogStatus.Inactive) entry.SetInactive();
        else entry.SetActive();
    }

    private static string PlainTextSnapshot(string value)
    {
        var decoded = WebUtility.HtmlDecode(value).Trim();
        var chars = new List<char>(decoded.Length);
        var inTag = false;
        foreach (var character in decoded)
        {
            if (character == '<') { inTag = true; continue; }
            if (character == '>') { inTag = false; continue; }
            if (!inTag) chars.Add(character);
        }
        var plain = new string(chars.ToArray()).Trim();
        return plain[..Math.Min(plain.Length, 256)];
    }

    private static ServiceCatalogResponse ToResponse(ServiceCatalogEntry e) => new() { ServiceNId = e.NId, Kind = e.Kind.ToString(), Name = e.Name, Description = e.Description, EntryPoint = e.EntryPoint, GatewayPathPrefix = e.GatewayPathPrefix, HealthPath = e.HealthPath, OwnerOrganizationNId = e.OwnerOrganizationNId, OwnerOrganizationNameSnapshot = e.OwnerOrganizationNameSnapshot, OwnerDisplaySnapshot = e.OwnerDisplaySnapshot, TechnicalLeadUserNId = e.TechnicalLeadUserNId, TechnicalOwnerUserNId = e.TechnicalOwnerUserNId, TechnicalOwnerAuthVersion = e.TechnicalOwnerAuthVersion, TechnicalLeadDisplayNameSnapshot = e.TechnicalLeadDisplayNameSnapshot, SupportedTerminals = e.SupportedTerminals.Select(x => x.ToString()).ToArray(), Status = e.Status.ToString(), Source = e.Source.ToString() };
    private static string Required(string? value) => string.IsNullOrWhiteSpace(value) ? throw new ValidationException("Required value is missing.") : value.Trim();
    private static T Parse<T>(string? value) where T : struct, Enum => Enum.TryParse<T>(value, true, out var parsed) ? parsed : throw new ValidationException("Invalid status.");
    private static UiTerminal[] ParseTerminals(IReadOnlyCollection<string>? values) => values is null || values.Count == 0 ? throw new ValidationException("At least one terminal is required.") : values.Select(x => Enum.TryParse<UiTerminal>(x, true, out var parsed) ? parsed : throw new ValidationException("Invalid terminal.")).Distinct().ToArray();
}

public interface IThemePolicyControlService
{
    Task<ThemePolicyResponse> GetAsync(string tenantNId, CancellationToken cancellationToken);
    Task<ThemePolicyResponse> RuntimeAsync(string tenantNId, CancellationToken cancellationToken);
    Task<ThemePolicyResponse> UpdateAsync(string tenantNId, ThemePolicyRequest request, CancellationToken cancellationToken);
}

public sealed class ThemePolicyControlService : IThemePolicyControlService
{
    private readonly IControlPlaneStore _store;
    private readonly RuntimeSnapshotLoader _runtime;
    public ThemePolicyControlService(IControlPlaneStore store, IIdentityPermissionRegistry _, RuntimeSnapshotLoader? runtime = null) { _store = store; _runtime = runtime ?? new RuntimeSnapshotLoader(store); }
    public async Task<ThemePolicyResponse> GetAsync(string tenantNId, CancellationToken cancellationToken) { var state = await _store.LoadAsync(tenantNId, cancellationToken); return ToResponse(state.Theme ?? throw new ValidationException("Theme policy was not configured.")); }
    public async Task<ThemePolicyResponse> RuntimeAsync(string tenantNId, CancellationToken cancellationToken) { var result = await _runtime.LoadAsync(tenantNId, "theme-policy", state => state.Theme is null ? new ThemePolicyResponse { PolicyRevision = state.Revision, Configured = false } : ToResponse(state.Theme, true), cancellationToken); return result.Value with { Degraded = result.Degraded }; }
    public async Task<ThemePolicyResponse> UpdateAsync(string tenantNId, ThemePolicyRequest request, CancellationToken cancellationToken) { var state = await _store.LoadAsync(tenantNId, cancellationToken); var policy = ThemePolicy.Create(tenantNId, ParseMany<ThemePalette>(request.AllowedPalettes), ParseMany<ThemeMode>(request.AllowedModes), ParseMany<PcDensity>(request.AllowedPcDensities), Parse<ThemePalette>(request.DefaultPalette), Parse<ThemeMode>(request.DefaultMode), Parse<PcDensity>(request.DefaultPcDensity)).WithRevision((state.Theme?.PolicyRevision ?? 0) + 1); await _store.CommitAsync(state with { Theme = policy }, state.Revision, new ControlPlaneCommit([new ControlPlaneEvent(Guid.NewGuid(), "SystemData.ThemePolicyChanged.v1", "v1", tenantNId, $"{{\"revision\":{policy.PolicyRevision}}}", DateTimeOffset.UtcNow)], [new LocalAuditEntry(tenantNId, "system", "theme-policy.update", "ThemePolicy", tenantNId, null, null, $"revision={policy.PolicyRevision}", Guid.NewGuid().ToString("N"))]), cancellationToken); await _runtime.InvalidateAsync(tenantNId, "theme-policy", cancellationToken); return ToResponse(policy); }
    private static ThemePolicyResponse ToResponse(ThemePolicy p, bool configured = true) => new() { PolicyRevision = p.PolicyRevision, Configured = configured, AllowedPalettes = p.AllowedPalettes.Select(x => x.ToString()).ToArray(), AllowedModes = p.AllowedModes.Select(x => x.ToString()).ToArray(), AllowedPcDensities = p.AllowedPcDensities.Select(x => x.ToString()).ToArray(), DefaultPalette = p.DefaultPalette.ToString(), DefaultMode = p.DefaultMode.ToString(), DefaultPcDensity = p.DefaultPcDensity.ToString() };
    private static T[] ParseMany<T>(IReadOnlyCollection<string>? values) where T : struct, Enum => values is null ? throw new ValidationException("Theme values are required.") : values.Select(x => Parse<T>(x)).Distinct().ToArray();
    private static T Parse<T>(string? value) where T : struct, Enum => Enum.TryParse<T>(value, true, out var parsed) ? parsed : throw new ValidationException("Invalid theme value.");
}
