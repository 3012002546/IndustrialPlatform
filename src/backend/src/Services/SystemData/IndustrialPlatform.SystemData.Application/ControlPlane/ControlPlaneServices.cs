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
public sealed record PublishedNavigationNode(string NodeNId, string? ParentNodeNId, NavigationNodeKind Kind, string Label, string? IconKey, string? ResourceNId, string? RouteName, string? RequiredPermissionNId, string? FeatureNId, int DisplayOrder, IReadOnlyCollection<UiTerminal> VisibleTerminals, IReadOnlyCollection<string>? ActionResourceNIds = null);

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
    Task<NavigationDefaultImportPreviewResponse> PreviewDefaultImportAsync(string tenantNId, CancellationToken cancellationToken);
    Task<NavigationDefaultImportPreviewResponse> ImportDefaultsAsync(string tenantNId, ImportNavigationDefaultsRequest request, CancellationToken cancellationToken);
    Task<UiResourceResponse> RegisterResourceAsync(string tenantNId, RegisterUiResourceRequest request, CancellationToken cancellationToken);
    Task<NavigationNodeResponse> AddNodeAsync(string tenantNId, CreateNavigationNodeRequest request, CancellationToken cancellationToken);
    Task<NavigationNodeResponse> UpdateNodeAsync(string tenantNId, string nodeNId, UpdateNavigationNodeRequest request, CancellationToken cancellationToken);
    Task DeleteNodeAsync(string tenantNId, string nodeNId, long? expectedDraftRevision, CancellationToken cancellationToken);
    Task<NavigationNodeResponse> RestoreNodeAsync(string tenantNId, string nodeNId, long? expectedDraftRevision, CancellationToken cancellationToken);
    Task<NavigationValidationResponse> ValidateAsync(string tenantNId, CancellationToken cancellationToken);
    Task<long> PublishAsync(string tenantNId, string actorUserNId, long? expectedDraftRevision, CancellationToken cancellationToken);
    Task<long> RollbackAsync(string tenantNId, string actorUserNId, long? expectedDraftRevision, CancellationToken cancellationToken);
    Task<NavigationRuntimeResponse> RuntimeAsync(string tenantNId, UiTerminal terminal, CancellationToken cancellationToken);
}

public sealed class ResourceNavigationService : IResourceNavigationService
{
    private sealed record DefaultNavigationDeclaration(string NodeNId, NavigationNodeKind Kind, string Label, string? ParentNodeNId, string? RouteName, string? RequiredPermissionNId, int DisplayOrder);
    private static readonly DefaultNavigationDeclaration[] DefaultNavigation = [
        new("navigation.group.workspace", NavigationNodeKind.Group, "工作台", null, null, null, 0),
        new("navigation.link.pc-home", NavigationNodeKind.Link, "首页", "navigation.group.workspace", "pc-home", "platform.home.view", 0),
        new("navigation.link.terminal-preview", NavigationNodeKind.Link, "终端预览", "navigation.group.workspace", "terminal-preview", "platform.pda.view", 1),
        new("navigation.group.system", NavigationNodeKind.Group, "系统管理", null, null, null, 1),
        new("navigation.group.identity-access", NavigationNodeKind.Group, "身份与访问", "navigation.group.system", null, null, 0),
        new("navigation.group.organization-people", NavigationNodeKind.Group, "组织与人员", "navigation.group.system", null, null, 1),
        new("navigation.group.menu-platform", NavigationNodeKind.Group, "菜单与平台配置", "navigation.group.system", null, null, 2),
        new("navigation.group.service-operations", NavigationNodeKind.Group, "服务与运维", "navigation.group.system", null, null, 3),
        new("navigation.link.identity-users", NavigationNodeKind.Link, "用户管理", "navigation.group.identity-access", "identity-users", "identity.user.view", 0),
        new("navigation.link.identity-user-groups", NavigationNodeKind.Link, "用户组管理", "navigation.group.identity-access", "identity-user-groups", "identity.user-group.view", 1),
        new("navigation.link.identity-roles", NavigationNodeKind.Link, "角色权限", "navigation.group.identity-access", "identity-roles", "identity.role.view", 2),
        new("navigation.link.identity-permissions", NavigationNodeKind.Link, "权限目录", "navigation.group.identity-access", "identity-permissions", "identity.permission.view", 3),
        new("navigation.link.identity-audits", NavigationNodeKind.Link, "登录审计", "navigation.group.identity-access", "identity-audits", "identity.audit.login.view", 4),
        new("navigation.link.identity-sso-providers", NavigationNodeKind.Link, "企业登录源", "navigation.group.identity-access", "sso-providers", "identity.sso.view", 5),
        new("navigation.link.identity-sso-clients", NavigationNodeKind.Link, "SSO Client", "navigation.group.identity-access", "sso-clients", "identity.sso.view", 6),
        new("navigation.link.systemdata-organizations", NavigationNodeKind.Link, "行政组织与岗位", "navigation.group.organization-people", "systemdata-organizations", "systemdata.organization.view", 0),
        new("navigation.link.systemdata-assignments", NavigationNodeKind.Link, "用户任职", "navigation.group.organization-people", "systemdata-assignments", "systemdata.assignment.view", 1),
        new("navigation.link.systemdata-navigation", NavigationNodeKind.Link, "菜单管理", "navigation.group.menu-platform", "systemdata-navigation", "systemdata.navigation.view", 0),
        new("navigation.link.systemdata-features", NavigationNodeKind.Link, "功能开关", "navigation.group.menu-platform", "systemdata-features", "systemdata.feature.view", 1),
        new("navigation.link.systemdata-themes", NavigationNodeKind.Link, "租户主题策略", "navigation.group.menu-platform", "systemdata-themes", "systemdata.theme-policy.view", 2),
        new("navigation.link.systemdata-services", NavigationNodeKind.Link, "服务目录", "navigation.group.service-operations", "systemdata-services", "systemdata.service-catalog.view", 0),
        new("navigation.link.systemdata-service-initialization", NavigationNodeKind.Link, "服务初始化编排", "navigation.group.service-operations", "systemdata-service-initialization", "systemdata.service-initialization.view", 1),
    ];
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
        var resourceRequests = request.Resources?.ToArray();
        var featureRequests = request.Features?.ToArray();
        var resourceDeclarations = resourceRequests is null
            ? Array.Empty<ModuleResourceDeclaration>()
            : resourceRequests.Select(x => new ModuleResourceDeclaration(Required(x.ResourceNId), version, Required(x.Type), Required(x.Name), x.RouteName?.Trim(), x.RequiredPermissionNId?.Trim(), ParseTerminals(x.SupportedTerminals))).ToArray();
        var featureDeclarations = featureRequests is null
            ? Array.Empty<ModuleFeatureDeclaration>()
            : featureRequests.Select(x => new ModuleFeatureDeclaration(Required(x.FeatureNId), Required(x.Name), x.Description?.Trim(), x.DefaultEnabled)).ToArray();
        var state = await _store.LoadAsync(tenantNId, cancellationToken);
        var existing = state.Manifests.FirstOrDefault(x => x.ModuleNId.Equals(module, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            var resourcesAlreadyApplied = resourceRequests is null || resourceDeclarations.All(declaration => state.Resources.Any(resource => resource.NId.Equals(declaration.ResourceNId, StringComparison.OrdinalIgnoreCase) && ResourceDeclarationMatches(resource, module, declaration) && resource.ManifestVersion == declaration.ManifestVersion));
            var featuresAlreadyApplied = featureRequests is null || featureDeclarations.All(declaration => state.Features.Any(feature => feature.NId.Equals(declaration.FeatureNId, StringComparison.OrdinalIgnoreCase) && feature.OwnerModuleNId.Equals(module, StringComparison.OrdinalIgnoreCase) && feature.Name == declaration.Name && feature.Description == declaration.Description && feature.DefaultEnabled == declaration.DefaultEnabled));
            if (existing.ManifestVersion == version && existing.Checksum.Equals(checksum, StringComparison.OrdinalIgnoreCase) && PermissionDeclarationsMatch(existing.PermissionDeclarations(), declarations) && resourcesAlreadyApplied && featuresAlreadyApplied)
                return state.Manifests.Select(ToManifest).ToArray();
            if (existing.ManifestVersion == version && !existing.Checksum.Equals(checksum, StringComparison.OrdinalIgnoreCase))
                throw new ValidationException("Module manifest checksum conflict.");
            if (existing.ManifestVersion == version && !PermissionDeclarationsMatch(existing.PermissionDeclarations(), declarations))
                throw new ValidationException("Module manifest permission declarations conflict.");
            EnsurePermissionUpgradeCompatibility(state, existing, declarations, permissions);
        }

        var receipt = await _permissionRegistry.VerifyAsync(permissionManifest, cancellationToken)
            ?? throw new ValidationException("Identity permission registry is unavailable; manifest registration is denied.");
        if (!receipt.Verified
            || !receipt.ModuleNId.Equals(module, StringComparison.OrdinalIgnoreCase)
            || !receipt.ManifestVersion.Equals(version, StringComparison.Ordinal)
            || !receipt.Checksum.Equals(checksum, StringComparison.OrdinalIgnoreCase))
            throw new ValidationException("Identity did not verify the module permission manifest.");

        var manifest = new ModuleManifestState(tenantNId, module, version, checksum, permissions, receipt.ManifestVersion, receipt.Checksum, receipt.VerifiedOn) { PermissionDeclarationItems = declarations, ResourceDeclarations = resourceDeclarations, FeatureDeclarations = featureDeclarations };
        var manifests = state.Manifests.Where(x => !x.ModuleNId.Equals(module, StringComparison.OrdinalIgnoreCase)).Append(manifest).ToArray();
        var receipts = state.PermissionReceipts.Where(x => !x.ModuleNId.Equals(module, StringComparison.OrdinalIgnoreCase)).Append(new PermissionReceipt(module, receipt.ManifestVersion, receipt.Checksum, receipt.Verified)).ToArray();
        var resources = state.Resources.ToList();
        if (resourceRequests is not null && existing is not null)
        {
            var declaredResourceIds = resourceDeclarations.Select(x => x.ResourceNId).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var resource in state.Resources.Where(x => x.OwnerModuleNId.Equals(module, StringComparison.OrdinalIgnoreCase) && !declaredResourceIds.Contains(x.NId)))
            {
                if (IsResourceUsedByNavigation(state, resource.NId))
                    throw new ValidationException($"Resource {resource.NId} is used by navigation and cannot be omitted from a module manifest upgrade.");
            }
        }
        foreach (var declaration in resourceDeclarations)
        {
            var resource = UiResource.Create(tenantNId, declaration.ResourceNId, module, version, Parse<UiResourceType>(declaration.Type), declaration.Name, declaration.RouteName, declaration.RequiredPermissionNId, declaration.SupportedTerminals);
            if (resource.RequiredPermissionNId is null || !permissions.Contains(resource.RequiredPermissionNId, StringComparer.OrdinalIgnoreCase)) throw new ValidationException("Manifest resource permission is not declared by the manifest.");
            var current = resources.FirstOrDefault(x => x.NId.Equals(resource.NId, StringComparison.OrdinalIgnoreCase));
            if (current is null)
            {
                resources.Add(resource);
                continue;
            }
            if (!current.OwnerModuleNId.Equals(module, StringComparison.OrdinalIgnoreCase))
                throw new ValidationException($"ResourceNId collision across modules: {resource.NId}.");
            if (!ResourceDeclarationMatches(current, module, declaration))
                throw new ValidationException($"Resource declaration conflict for {resource.NId}.");
            if (current.ManifestVersion != version)
                resources[resources.IndexOf(current)] = current.RebindManifestVersion(version);
        }
        var features = state.Features.ToList();
        foreach (var declaration in featureDeclarations)
        {
            var feature = FeatureDefinition.Create(tenantNId, declaration.FeatureNId, module, declaration.Name, declaration.DefaultEnabled, declaration.Description);
            var current = features.FirstOrDefault(x => x.NId.Equals(feature.NId, StringComparison.OrdinalIgnoreCase));
            if (current is null)
            {
                features.Add(feature);
                continue;
            }
            if (!current.OwnerModuleNId.Equals(module, StringComparison.OrdinalIgnoreCase))
                throw new ValidationException($"FeatureNId collision across modules: {feature.NId}.");
            if (current.Name != feature.Name || current.Description != feature.Description || current.DefaultEnabled != feature.DefaultEnabled)
                throw new ValidationException($"Feature declaration conflict for {feature.NId}.");
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

    public async Task<NavigationDefaultImportPreviewResponse> PreviewDefaultImportAsync(string tenantNId, CancellationToken cancellationToken)
    {
        var state = await _store.LoadAsync(tenantNId, cancellationToken);
        return BuildDefaultImportPreview(state);
    }

    public async Task<NavigationDefaultImportPreviewResponse> ImportDefaultsAsync(
        string tenantNId,
        ImportNavigationDefaultsRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var state = await _store.LoadAsync(tenantNId, cancellationToken);
        EnsureExpectedRevision(state, request.ExpectedDraftRevision);
        var existing = state.DraftNodes.ToDictionary(node => node.NId, StringComparer.OrdinalIgnoreCase);
        var additions = new List<NavigationNode>();
        var previewItems = new List<NavigationDefaultImportItemResponse>();
        foreach (var declaration in DefaultNavigation)
        {
            if (existing.ContainsKey(declaration.NodeNId))
            {
                previewItems.Add(DefaultImportItem(declaration, "Skipped", "节点已存在,不会覆盖当前草稿。"));
                continue;
            }

            string? resourceNId = null;
            if (declaration.Kind == NavigationNodeKind.Link)
            {
                resourceNId = state.Resources.FirstOrDefault(resource =>
                    string.Equals(resource.RouteName, declaration.RouteName, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(resource.RequiredPermissionNId, declaration.RequiredPermissionNId, StringComparison.OrdinalIgnoreCase))?.NId;
                if (resourceNId is null)
                {
                    previewItems.Add(DefaultImportItem(declaration, "Blocked", $"缺少受信任资源:{declaration.RouteName}。"));
                    continue;
                }
            }

            var node = declaration.Kind == NavigationNodeKind.Group
                ? NavigationNode.CreateGroup(tenantNId, declaration.NodeNId, declaration.Label, declaration.ParentNodeNId, "PLATFORM_NAVIGATION")
                : NavigationNode.CreateLink(tenantNId, declaration.NodeNId, declaration.Label, declaration.ParentNodeNId, "PLATFORM_NAVIGATION", resourceNId!, null, [UiTerminal.Pc, UiTerminal.Pda, UiTerminal.Mobile]);
            node.SetDisplayOrder(declaration.DisplayOrder);
            additions.Add(node);
            existing[node.NId] = node;
            previewItems.Add(DefaultImportItem(declaration, "Added", "将追加到当前草稿,不覆盖已有节点。"));
        }

        if (previewItems.Any(item => item.Action == "Blocked"))
            throw new ValidationException(string.Join("; ", previewItems.Where(item => item.Action == "Blocked").Select(item => item.Reason)));

        if (additions.Count > 0)
        {
            var nextNodes = state.DraftNodes.Concat(additions).ToArray();
            foreach (var node in additions) EnsureHierarchy(nextNodes, node);
            await _store.CommitAsync(
                state with { DraftNodes = nextNodes },
                state.Revision,
                Commit("SystemData.NavigationDraftChanged.v1", tenantNId, "{\"source\":\"default-import\"}", "navigation.defaults.import", "NavigationDraft", tenantNId),
                cancellationToken);
            await InvalidateNavigationAsync(tenantNId, cancellationToken);
        }

        return new NavigationDefaultImportPreviewResponse
        {
            DraftRevision = state.Revision + (additions.Count > 0 ? 1 : 0),
            Items = previewItems,
        };
    }

    public async Task<NavigationNodeResponse> AddNodeAsync(string tenantNId, CreateNavigationNodeRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var state = await _store.LoadAsync(tenantNId, cancellationToken);
        EnsureExpectedRevision(state, request.ExpectedDraftRevision);
        var node = ParseNode(tenantNId, request);
        if (state.DraftNodes.Any(x => x.NId.Equals(node.NId, StringComparison.OrdinalIgnoreCase))) throw new ValidationException("Navigation node id cannot be reused.");
        EnsureActionResourceAssociations(state, node);
        var nextNodes = state.DraftNodes.Append(node).ToArray();
        // 校验包含候选节点本身,否则新增第四层时旧节点都合法而候选节点被漏检。
        EnsureHierarchy(nextNodes, node);
        await _store.CommitAsync(state with { DraftNodes = nextNodes }, state.Revision,
            Commit("SystemData.NavigationDraftChanged.v1", tenantNId, $"{{\"nodeNId\":\"{Json(node.NId)}\"}}", "navigation.node.add", "NavigationNode", node.NId), cancellationToken);
        await InvalidateNavigationAsync(tenantNId, cancellationToken);
        return ToNode(node, null);
    }

    public async Task<NavigationNodeResponse> UpdateNodeAsync(string tenantNId, string nodeNId, UpdateNavigationNodeRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var state = await _store.LoadAsync(tenantNId, cancellationToken);
        EnsureExpectedRevision(state, request.ExpectedDraftRevision);
        var node = state.DraftNodes.FirstOrDefault(x => x.NId.Equals(nodeNId, StringComparison.OrdinalIgnoreCase)) ?? throw new ValidationException("Navigation node was not found.");
        node.UpdateLabel(Required(request.Label));
        node.UpdateConfiguration(
            request.ParentNodeNId,
            request.ResourceNId,
            request.FeatureNId,
            request.VisibleTerminals?.Select(ParseTerminal).ToArray(),
            request.ActionResourceNIds);
        EnsureHierarchy(state.DraftNodes, node);
        EnsureActionResourceAssociations(state, node);
        node.UpdateIconKey(request.IconKey);
        if (request.DisplayOrder is { } order) node.SetDisplayOrder(order);
        await _store.CommitAsync(state, state.Revision,
            Commit("SystemData.NavigationDraftChanged.v1", tenantNId, $"{{\"nodeNId\":\"{Json(node.NId)}\"}}", "navigation.node.update", "NavigationNode", node.NId), cancellationToken);
        await InvalidateNavigationAsync(tenantNId, cancellationToken);
        return ToNode(node, state.DraftNodes);
    }

    public async Task<NavigationNodeResponse> RestoreNodeAsync(string tenantNId, string nodeNId, long? expectedDraftRevision, CancellationToken cancellationToken)
    {
        var state = await _store.LoadAsync(tenantNId, cancellationToken);
        EnsureExpectedRevision(state, expectedDraftRevision);
        var node = state.DraftNodes.FirstOrDefault(x => x.NId.Equals(nodeNId, StringComparison.OrdinalIgnoreCase)) ?? throw new ValidationException("Navigation node was not found.");
        node.Restore();
        EnsureHierarchy(state.DraftNodes, node);
        await _store.CommitAsync(state, state.Revision,
            Commit("SystemData.NavigationDraftChanged.v1", tenantNId, $"{{\"nodeNId\":\"{Json(node.NId)}\"}}", "navigation.node.restore", "NavigationNode", node.NId), cancellationToken);
        await InvalidateNavigationAsync(tenantNId, cancellationToken);
        return ToNode(node, state.DraftNodes);
    }

    public async Task DeleteNodeAsync(string tenantNId, string nodeNId, long? expectedDraftRevision, CancellationToken cancellationToken)
    {
        var state = await _store.LoadAsync(tenantNId, cancellationToken);
        EnsureExpectedRevision(state, expectedDraftRevision);
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
        return ToValidation(ValidateState(state), state);
    }

    public async Task<long> PublishAsync(string tenantNId, string actorUserNId, long? expectedDraftRevision, CancellationToken cancellationToken)
    {
        var state = await _store.LoadAsync(tenantNId, cancellationToken);
        EnsureExpectedRevision(state, expectedDraftRevision);
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

    public async Task<long> PublishAsync(string tenantNId, string actorUserNId, CancellationToken cancellationToken)
    {
        var state = await _store.LoadAsync(tenantNId, cancellationToken);
        return await PublishAsync(tenantNId, actorUserNId, state.Revision, cancellationToken);
    }

    public async Task<long> RollbackAsync(string tenantNId, string actorUserNId, long? expectedDraftRevision, CancellationToken cancellationToken)
    {
        var state = await _store.LoadAsync(tenantNId, cancellationToken);
        EnsureExpectedRevision(state, expectedDraftRevision);
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

    public async Task<long> RollbackAsync(string tenantNId, string actorUserNId, CancellationToken cancellationToken)
    {
        var state = await _store.LoadAsync(tenantNId, cancellationToken);
        return await RollbackAsync(tenantNId, actorUserNId, state.Revision, cancellationToken);
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
        foreach (var node in state.DraftNodes.Where(x => x.Status == NavigationNodeStatus.Active && x.Kind == NavigationNodeKind.Link))
        {
            var resourceIds = new[] { node.ResourceNId }
                .Concat(node.ActionResourceNIds)
                .Where(resourceNId => resourceNId is not null)
                .Select(resourceNId => resourceNId!);
            foreach (var resourceNId in resourceIds.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var resource = state.Resources.FirstOrDefault(x => x.NId.Equals(resourceNId, StringComparison.OrdinalIgnoreCase));
                if (resource is not null && !IsResourcePermissionVerified(state, resource))
                    errors.Add(new("PERMISSION_UNVERIFIED", "Resource permission registration is not verified.", node.NId));
            }
        }
        return new NavigationValidationResult(errors);
    }

    private static bool IsResourcePermissionVerified(ControlPlaneSnapshot state, UiResource resource)
    {
        if (string.IsNullOrWhiteSpace(resource.RequiredPermissionNId)) return false;
        var manifest = state.Manifests.FirstOrDefault(item =>
            item.ModuleNId.Equals(resource.OwnerModuleNId, StringComparison.OrdinalIgnoreCase)
            && item.ManifestVersion.Equals(resource.ManifestVersion, StringComparison.Ordinal));
        if (manifest is null || !manifest.PermissionDeclarations().Any(item => item.PermissionNId.Equals(resource.RequiredPermissionNId, StringComparison.OrdinalIgnoreCase)))
            return false;
        var receipt = state.PermissionReceipts.FirstOrDefault(item => item.ModuleNId.Equals(resource.OwnerModuleNId, StringComparison.OrdinalIgnoreCase));
        if (receipt is null
            || !receipt.Verified
            || !receipt.ManifestVersion.Equals(manifest.ManifestVersion, StringComparison.Ordinal)
            || !receipt.Checksum.Equals(manifest.Checksum, StringComparison.OrdinalIgnoreCase))
            return false;
        if (manifest.ResourceDeclarations.Count == 0) return true;
        var declaration = manifest.ResourceDeclarations.FirstOrDefault(item => item.ResourceNId.Equals(resource.NId, StringComparison.OrdinalIgnoreCase));
        return declaration is not null
            && declaration.ManifestVersion.Equals(resource.ManifestVersion, StringComparison.Ordinal)
            && ResourceDeclarationMatches(resource, resource.OwnerModuleNId, declaration);
    }

    private static NavigationDefaultImportPreviewResponse BuildDefaultImportPreview(ControlPlaneSnapshot state)
    {
        var existing = state.DraftNodes.ToDictionary(node => node.NId, StringComparer.OrdinalIgnoreCase);
        var items = DefaultNavigation.Select(declaration =>
        {
            if (existing.ContainsKey(declaration.NodeNId))
                return DefaultImportItem(declaration, "Skipped", "节点已存在,不会覆盖当前草稿。");
            if (declaration.Kind == NavigationNodeKind.Link && !state.Resources.Any(resource =>
                    string.Equals(resource.RouteName, declaration.RouteName, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(resource.RequiredPermissionNId, declaration.RequiredPermissionNId, StringComparison.OrdinalIgnoreCase)))
                return DefaultImportItem(declaration, "Blocked", $"缺少受信任资源:{declaration.RouteName}。");
            return DefaultImportItem(declaration, "Add", "确认后追加到当前草稿。");
        }).ToArray();
        return new NavigationDefaultImportPreviewResponse { DraftRevision = state.Revision, Items = items };
    }

    private static NavigationDefaultImportItemResponse DefaultImportItem(
        DefaultNavigationDeclaration declaration,
        string action,
        string reason) => new()
        {
            NodeNId = declaration.NodeNId,
            Label = declaration.Label,
            ParentNodeNId = declaration.ParentNodeNId,
            Kind = declaration.Kind.ToString(),
            Level = DefaultNavigationLevel(declaration),
            Action = action,
            Reason = reason,
        };

    private static int DefaultNavigationLevel(DefaultNavigationDeclaration declaration)
    {
        var byId = DefaultNavigation.ToDictionary(item => item.NodeNId, StringComparer.OrdinalIgnoreCase);
        var level = 1;
        var parent = declaration.ParentNodeNId;
        while (parent is not null && byId.TryGetValue(parent, out var parentDeclaration))
        {
            level++;
            parent = parentDeclaration.ParentNodeNId;
        }

        return level;
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
        return state.DraftNodes.Where(x => x.Status == NavigationNodeStatus.Active).Select(x => new PublishedNavigationNode(x.NId, x.ParentNodeNId, x.Kind, x.Label, x.IconKey, x.ResourceNId, x.ResourceNId is not null && resources.TryGetValue(x.ResourceNId, out var r) ? r.RouteName : null, x.ResourceNId is not null && resources.TryGetValue(x.ResourceNId, out r) ? r.RequiredPermissionNId : null, x.FeatureNId, x.DisplayOrder, x.VisibleTerminals, x.ActionResourceNIds)).ToArray();
    }

    private static NavigationNodeResponse[] BuildDraft(IReadOnlyCollection<NavigationNode> nodes) => nodes.Where(x => x.ParentNodeNId is null).OrderBy(x => x.DisplayOrder).ThenBy(x => x.NId).Select(x => ToNode(x, nodes)).ToArray();
    private static NavigationRuntimeNodeResponse[] BuildRuntime(IReadOnlyCollection<PublishedNavigationNode> nodes, string? parent) => nodes.Where(x => string.Equals(x.ParentNodeNId, parent, StringComparison.OrdinalIgnoreCase)).OrderBy(x => x.DisplayOrder).ThenBy(x => x.NodeNId).Select(x => new NavigationRuntimeNodeResponse { NodeNId = x.NodeNId, Kind = x.Kind.ToString(), Label = x.Label, IconKey = x.IconKey, ResourceNId = x.ResourceNId, RouteName = x.RouteName, RequiredPermissionNId = x.RequiredPermissionNId, FeatureNId = x.FeatureNId, DisplayOrder = x.DisplayOrder, Children = BuildRuntime(nodes, x.NodeNId) }).ToArray();
    private static NavigationNodeResponse ToNode(NavigationNode node, IReadOnlyCollection<NavigationNode>? all) => new() { NodeNId = node.NId, Kind = node.Kind.ToString(), Label = node.Label, IconKey = node.IconKey, ParentNodeNId = node.ParentNodeNId, ResourceNId = node.ResourceNId, FeatureNId = node.FeatureNId, DisplayOrder = node.DisplayOrder, VisibleTerminals = node.VisibleTerminals.Select(x => x.ToString()).ToArray(), Status = node.Status.ToString(), ActionResourceNIds = node.ActionResourceNIds, Children = all?.Where(x => x.ParentNodeNId?.Equals(node.NId, StringComparison.OrdinalIgnoreCase) == true).OrderBy(x => x.DisplayOrder).ThenBy(x => x.NId).Select(x => ToNode(x, all)).ToArray() ?? [] };
    private static UiResourceResponse ToResource(UiResource resource) => new() { ResourceNId = resource.NId, OwnerModuleNId = resource.OwnerModuleNId, ManifestVersion = resource.ManifestVersion, Type = resource.Type.ToString(), Name = resource.Name, RouteName = resource.RouteName, RequiredPermissionNId = resource.RequiredPermissionNId, SupportedTerminals = resource.SupportedTerminals.Select(x => x.ToString()).ToArray(), Status = resource.Status.ToString() };
    private static ModuleManifestResponse ToManifest(ModuleManifestState manifest) => new() { ModuleNId = manifest.ModuleNId, ManifestVersion = manifest.ManifestVersion, Checksum = manifest.Checksum, PermissionNIds = manifest.PermissionNIds, Permissions = manifest.PermissionDeclarations().Select(x => new PermissionDeclarationRequest { PermissionNId = x.PermissionNId, Name = x.Name, ResourceType = x.ResourceType, ParentPermissionNId = x.ParentPermissionNId }).ToArray(), PermissionVerified = manifest.PermissionReceiptVersion is not null, PermissionVerifiedOn = manifest.PermissionVerifiedOn };
    private static NavigationValidationResponse ToValidation(NavigationValidationResult result, ControlPlaneSnapshot? state = null) => new()
    {
        DraftRevision = state?.Revision ?? 0,
        IsValid = result.IsValid,
        Errors = result.Errors.Select(error =>
        {
            var node = state?.DraftNodes.FirstOrDefault(item => item.NId.Equals(error.NodeNId, StringComparison.OrdinalIgnoreCase));
            var resource = node?.ResourceNId is null
                ? null
                : state?.Resources.FirstOrDefault(item => item.NId.Equals(node.ResourceNId, StringComparison.OrdinalIgnoreCase));
            var manifest = resource is null
                ? null
                : state?.Manifests.FirstOrDefault(item => item.ModuleNId.Equals(resource.OwnerModuleNId, StringComparison.OrdinalIgnoreCase) && item.ManifestVersion == resource.ManifestVersion);
            var receipt = manifest is null
                ? null
                : state?.PermissionReceipts.FirstOrDefault(item => item.ModuleNId.Equals(manifest.ModuleNId, StringComparison.OrdinalIgnoreCase));
            var receiptDetails = error.Code == "PERMISSION_UNVERIFIED" && state is not null && node is not null
                ? BuildReceiptDetails(state, node)
                : [];
            var primaryReceiptDetail = receiptDetails.FirstOrDefault();
            return new NavigationValidationErrorResponse
            {
                Code = error.Code,
                Message = error.Message,
                NodeNId = error.NodeNId,
                ResourceNId = primaryReceiptDetail?.ResourceNId ?? resource?.NId,
                ModuleNId = primaryReceiptDetail?.ModuleNId ?? resource?.OwnerModuleNId,
                ManifestVersion = primaryReceiptDetail?.ManifestVersion ?? manifest?.ManifestVersion ?? resource?.ManifestVersion,
                ManifestChecksum = primaryReceiptDetail?.ManifestChecksum ?? manifest?.Checksum,
                TrustedReceiptVersion = primaryReceiptDetail?.TrustedReceiptVersion ?? receipt?.ManifestVersion,
                TrustedReceiptChecksum = primaryReceiptDetail?.TrustedReceiptChecksum ?? receipt?.Checksum,
                TrustedReceiptVerified = primaryReceiptDetail?.TrustedReceiptVerified ?? receipt?.Verified,
                ReceiptDetails = receiptDetails,
            };
        }).ToArray(),
    };

    private static NavigationValidationReceiptDetail[] BuildReceiptDetails(ControlPlaneSnapshot state, NavigationNode node)
    {
        var resourceIds = new[] { node.ResourceNId }
            .Concat(node.ActionResourceNIds)
            .Where(resourceNId => resourceNId is not null)
            .Select(resourceNId => resourceNId!)
            .Distinct(StringComparer.OrdinalIgnoreCase);
        var resources = resourceIds
            .Select(resourceNId => state.Resources.FirstOrDefault(resource => resource.NId.Equals(resourceNId, StringComparison.OrdinalIgnoreCase)))
            .Where(resource => resource is not null)
            .Select(resource => resource!);
        return resources.Select(resource =>
        {
            var manifest = state.Manifests.FirstOrDefault(item => item.ModuleNId.Equals(resource.OwnerModuleNId, StringComparison.OrdinalIgnoreCase) && item.ManifestVersion == resource.ManifestVersion);
            var receipt = state.PermissionReceipts.FirstOrDefault(item => item.ModuleNId.Equals(resource.OwnerModuleNId, StringComparison.OrdinalIgnoreCase));
            var verified = receipt?.Verified == true
                && receipt.ManifestVersion == manifest?.ManifestVersion
                && receipt.Checksum.Equals(manifest?.Checksum, StringComparison.OrdinalIgnoreCase);
            return new NavigationValidationReceiptDetail
            {
                ResourceNId = resource.NId,
                ModuleNId = resource.OwnerModuleNId,
                ManifestVersion = manifest?.ManifestVersion ?? resource.ManifestVersion,
                ManifestChecksum = manifest?.Checksum,
                TrustedReceiptVersion = receipt?.ManifestVersion,
                TrustedReceiptChecksum = receipt?.Checksum,
                TrustedReceiptVerified = verified,
            };
        }).Where(detail => !detail.TrustedReceiptVerified).ToArray();
    }
    private static ControlPlaneCommit Commit(string eventType, string tenant, string payload, string action, string objectType, string objectNId, string actor = "system") => new([new ControlPlaneEvent(Guid.NewGuid(), eventType, "v1", tenant, payload, DateTimeOffset.UtcNow)], [new LocalAuditEntry(tenant, actor, action, objectType, objectNId, null, null, payload, Guid.NewGuid().ToString("N"))]);
    private static NavigationNode ParseNode(string tenant, CreateNavigationNodeRequest request) { var kind = Parse<NavigationNodeKind>(request.Kind); var terminals = kind == NavigationNodeKind.Link ? ParseTerminals(request.VisibleTerminals) : [UiTerminal.Pc, UiTerminal.Pda, UiTerminal.Mobile]; var node = kind == NavigationNodeKind.Group ? NavigationNode.CreateGroup(tenant, string.IsNullOrWhiteSpace(request.NodeNId) ? $"navigation.group.{Guid.NewGuid():N}" : request.NodeNId, Required(request.Label), request.ParentNodeNId, request.NavigationSetNId ?? "PLATFORM_NAVIGATION", request.IconKey) : NavigationNode.CreateLink(tenant, string.IsNullOrWhiteSpace(request.NodeNId) ? $"navigation.link.{Guid.NewGuid():N}" : request.NodeNId, Required(request.Label), request.ParentNodeNId, request.NavigationSetNId ?? "PLATFORM_NAVIGATION", Required(request.ResourceNId), request.FeatureNId, terminals, request.IconKey, request.ActionResourceNIds); if (request.DisplayOrder is { } order) node.SetDisplayOrder(order); return node; }
    private static void EnsureActionResourceAssociations(ControlPlaneSnapshot state, NavigationNode node)
    {
        if (node.ActionResourceNIds.Count == 0) return;
        if (node.Kind != NavigationNodeKind.Link)
            throw new ValidationException("Only page menu nodes can associate action permissions.");
        foreach (var resourceNId in node.ActionResourceNIds)
        {
            var resource = state.Resources.FirstOrDefault(x => x.NId.Equals(resourceNId, StringComparison.OrdinalIgnoreCase));
            if (resource is null) throw new ValidationException($"Action resource was not found: {resourceNId}.");
            if (resource.Type != UiResourceType.Action) throw new ValidationException($"Resource is not an Action resource: {resourceNId}.");
            if (resource.Status == UiResourceStatus.Retired) throw new ValidationException($"Retired action resource cannot be associated: {resourceNId}.");
            if (string.IsNullOrWhiteSpace(resource.RequiredPermissionNId)) throw new ValidationException($"Action resource has no permission: {resourceNId}.");
        }
    }
    private static bool ResourceDeclarationMatches(UiResource resource, string module, ModuleResourceDeclaration declaration) =>
        resource.OwnerModuleNId.Equals(module, StringComparison.OrdinalIgnoreCase)
        && resource.Type.ToString().Equals(declaration.Type, StringComparison.OrdinalIgnoreCase)
        && string.Equals(resource.RouteName, declaration.RouteName, StringComparison.Ordinal)
        && string.Equals(resource.RequiredPermissionNId, declaration.RequiredPermissionNId, StringComparison.OrdinalIgnoreCase)
        && resource.SupportedTerminals.ToHashSet().SetEquals(declaration.SupportedTerminals);

    private static bool PermissionDeclarationsMatch(
        IReadOnlyCollection<PermissionManifestEntry> left,
        PermissionManifestEntry[] right) =>
        left.Count == right.Length
        && left.All(existing => right.Any(candidate =>
            existing.PermissionNId.Equals(candidate.PermissionNId, StringComparison.OrdinalIgnoreCase)
            && existing.Name == candidate.Name
            && existing.ResourceType == candidate.ResourceType
            && string.Equals(existing.ParentPermissionNId, candidate.ParentPermissionNId, StringComparison.OrdinalIgnoreCase)));

    private static void EnsurePermissionUpgradeCompatibility(
        ControlPlaneSnapshot state,
        ModuleManifestState existing,
        IReadOnlyCollection<PermissionManifestEntry> declarations,
        IReadOnlyCollection<string> permissions)
    {
        foreach (var previous in existing.PermissionDeclarations())
        {
            var current = declarations.FirstOrDefault(item => item.PermissionNId.Equals(previous.PermissionNId, StringComparison.OrdinalIgnoreCase));
            if (current is not null && !PermissionDeclarationMatches(previous, current))
                throw new ValidationException($"Permission declaration identity changed: {previous.PermissionNId}.");
        }

        foreach (var resource in state.Resources.Where(item => item.OwnerModuleNId.Equals(existing.ModuleNId, StringComparison.OrdinalIgnoreCase)))
        {
            if (resource.RequiredPermissionNId is not null && !permissions.Contains(resource.RequiredPermissionNId, StringComparer.OrdinalIgnoreCase))
                throw new ValidationException($"Permission {resource.RequiredPermissionNId} is still referenced by resource {resource.NId}.");
        }
    }

    private static bool PermissionDeclarationMatches(PermissionManifestEntry left, PermissionManifestEntry right) =>
        left.PermissionNId.Equals(right.PermissionNId, StringComparison.OrdinalIgnoreCase)
        && left.Name == right.Name
        && left.ResourceType == right.ResourceType
        && string.Equals(left.ParentPermissionNId, right.ParentPermissionNId, StringComparison.OrdinalIgnoreCase);

    private static bool IsResourceUsedByNavigation(ControlPlaneSnapshot state, string resourceNId) =>
        state.DraftNodes.Any(node =>
            string.Equals(node.ResourceNId, resourceNId, StringComparison.OrdinalIgnoreCase)
            || node.ActionResourceNIds.Any(item => item.Equals(resourceNId, StringComparison.OrdinalIgnoreCase)))
        || state.Snapshots.SelectMany(snapshot => snapshot.Nodes).Any(node =>
            string.Equals(node.ResourceNId, resourceNId, StringComparison.OrdinalIgnoreCase)
            || node.ActionResourceNIds?.Any(item => item.Equals(resourceNId, StringComparison.OrdinalIgnoreCase)) == true);

    private static string Required(string? value) => string.IsNullOrWhiteSpace(value) ? throw new ValidationException("Required value is missing.") : value.Trim();
    private static string Json(string value) => value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
    private static T Parse<T>(string? value) where T : struct, Enum => Enum.TryParse<T>(value, true, out var parsed) ? parsed : throw new ValidationException($"Invalid {typeof(T).Name} value.");
    private static UiTerminal[] ParseTerminals(IReadOnlyCollection<string>? values) => values is null || values.Count == 0 ? throw new ValidationException("At least one terminal is required.") : values.Select(x => Parse<UiTerminal>(x)).Distinct().ToArray();
    private static UiTerminal ParseTerminal(string value) => Parse<UiTerminal>(value);
    private static void EnsureExpectedRevision(ControlPlaneSnapshot state, long? expected)
    {
        if (expected is not { } value)
            throw new ValidationException("Navigation draft revision is required.");
        if (value != state.Revision)
            throw new ConcurrencyException("Navigation draft revision is stale.");
    }
    private static void EnsureHierarchy(IReadOnlyCollection<NavigationNode> nodes, NavigationNode candidate)
    {
        var parent = candidate.ParentNodeNId is null ? null : nodes.FirstOrDefault(x => x.NId.Equals(candidate.ParentNodeNId, StringComparison.OrdinalIgnoreCase));
        if (candidate.ParentNodeNId is not null && parent is null) throw new ValidationException("Navigation node parent was not found.");
        if (parent is not null && parent.Status != NavigationNodeStatus.Active) throw new ValidationException("Navigation node parent must be active.");
        if (parent?.Kind == NavigationNodeKind.Link) throw new ValidationException("Navigation nodes cannot be children of a link.");
        foreach (var node in nodes.Where(x => x.Status == NavigationNodeStatus.Active || x.NId.Equals(candidate.NId, StringComparison.OrdinalIgnoreCase)))
        {
            var depth = 1;
            var current = node;
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            while (current.ParentNodeNId is not null)
            {
                if (!visited.Add(current.NId)) throw new ValidationException("Navigation node cycle detected.");
                depth++;
                current = nodes.FirstOrDefault(x => x.NId.Equals(current.ParentNodeNId, StringComparison.OrdinalIgnoreCase))
                    ?? throw new ValidationException("Navigation node parent was not found.");
            }
            if (depth > 3) throw new ValidationException("Navigation hierarchy cannot exceed three levels.");
        }
    }
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
    public async Task<ThemePolicyResponse> GetAsync(string tenantNId, CancellationToken cancellationToken)
    {
        var state = await _store.LoadAsync(tenantNId, cancellationToken);
        return state.Theme is null
            ? new ThemePolicyResponse
            {
                PolicyRevision = 0,
                Configured = false,
                AllowedPalettes = ["industrial-cyan", "technology-blue", "neutral-gray"],
                AllowedModes = ["light", "dark", "system"],
                AllowedPcDensities = ["comfortable", "compact"],
                DefaultPalette = "industrial-cyan",
                DefaultMode = "system",
                DefaultPcDensity = "comfortable"
            }
            : ToResponse(state.Theme);
    }
    public async Task<ThemePolicyResponse> RuntimeAsync(string tenantNId, CancellationToken cancellationToken) { var result = await _runtime.LoadAsync(tenantNId, "theme-policy", state => state.Theme is null ? new ThemePolicyResponse { PolicyRevision = 0, Configured = false } : ToResponse(state.Theme, true), cancellationToken); return result.Value with { Degraded = result.Degraded }; }
    public async Task<ThemePolicyResponse> UpdateAsync(string tenantNId, ThemePolicyRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var state = await _store.LoadAsync(tenantNId, cancellationToken);
        var currentRevision = state.Theme?.PolicyRevision ?? 0;
        if (request.ExpectedPolicyRevision is not { } expected)
            throw new ValidationException("Theme policy revision is required.");
        if (expected != currentRevision)
            throw new ConcurrencyException("Theme policy revision is stale.");
        var policy = ThemePolicy.Create(
            tenantNId,
            ParseMany<ThemePalette>(request.AllowedPalettes),
            ParseMany<ThemeMode>(request.AllowedModes),
            ParseMany<PcDensity>(request.AllowedPcDensities),
            Parse<ThemePalette>(request.DefaultPalette),
            Parse<ThemeMode>(request.DefaultMode),
            Parse<PcDensity>(request.DefaultPcDensity)).WithRevision(currentRevision + 1);
        await _store.CommitAsync(state with { Theme = policy }, state.Revision,
            new ControlPlaneCommit(
                [new ControlPlaneEvent(Guid.NewGuid(), "SystemData.ThemePolicyChanged.v1", "v1", tenantNId, $"{{\"revision\":{policy.PolicyRevision}}}", DateTimeOffset.UtcNow)],
                [new LocalAuditEntry(tenantNId, "system", "theme-policy.update", "ThemePolicy", tenantNId, null, null, $"revision={policy.PolicyRevision}", Guid.NewGuid().ToString("N"))]),
            cancellationToken);
        await _runtime.InvalidateAsync(tenantNId, "theme-policy", cancellationToken);
        return ToResponse(policy);
    }
    private static ThemePolicyResponse ToResponse(ThemePolicy p, bool configured = true) => new() { PolicyRevision = p.PolicyRevision, Configured = configured, AllowedPalettes = p.AllowedPalettes.Select(ToWireValue).ToArray(), AllowedModes = p.AllowedModes.Select(ToWireValue).ToArray(), AllowedPcDensities = p.AllowedPcDensities.Select(ToWireValue).ToArray(), DefaultPalette = ToWireValue(p.DefaultPalette), DefaultMode = ToWireValue(p.DefaultMode), DefaultPcDensity = ToWireValue(p.DefaultPcDensity) };
    private static T[] ParseMany<T>(IReadOnlyCollection<string>? values) where T : struct, Enum => values is null ? throw new ValidationException("Theme values are required.") : values.Select(x => Parse<T>(x)).Distinct().ToArray();
    private static T Parse<T>(string? value) where T : struct, Enum
    {
        var normalized = string.Concat((value ?? string.Empty).Where(char.IsAsciiLetterOrDigit));
        var matches = Enum.GetValues<T>().Where(x => string.Equals(string.Concat(x.ToString().Where(char.IsAsciiLetterOrDigit)), normalized, StringComparison.OrdinalIgnoreCase)).ToArray();
        return string.IsNullOrEmpty(normalized) || matches.Length != 1 ? throw new ValidationException("Invalid theme value.") : matches[0];
    }
    private static string ToWireValue<T>(T value) where T : struct, Enum => value switch
    {
        ThemePalette.IndustrialCyan => "industrial-cyan",
        ThemePalette.TechnologyBlue => "technology-blue",
        ThemePalette.NeutralGray => "neutral-gray",
        ThemeMode.Light => "light",
        ThemeMode.Dark => "dark",
        ThemeMode.System => "system",
        PcDensity.Comfortable => "comfortable",
        PcDensity.Compact => "compact",
        _ => value.ToString()
    };
}
