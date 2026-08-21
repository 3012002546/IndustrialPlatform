using System.Net;

namespace IndustrialPlatform.SystemData.Domain.ControlPlane;

public enum UiResourceType { Page, Action }
public enum UiResourceStatus { Active, Retired }
public enum UiTerminal { Pc, Pda, Mobile }
public enum NavigationNodeKind { Group, Link }
public enum NavigationNodeStatus { Active, Inactive }
public enum FeatureStatus { Active, Retired }
public enum FeatureOverrideMode { Inherit, Enabled, Disabled }
public enum ServiceCatalogKind { Platform, External }
public enum ServiceCatalogStatus { Active, Inactive, Retired }
public enum ServiceCatalogSource { Manifest, Manual }
public enum ThemePalette { IndustrialCyan, TechnologyBlue, NeutralGray }
public enum ThemeMode { Light, Dark, System }
public enum PcDensity { Comfortable, Compact }

public sealed class UiResource
{
    private UiResource(string tenantNId, string nId, string ownerModuleNId, string manifestVersion,
        UiResourceType type, string name, string? routeName, string? requiredPermissionNId,
        IReadOnlyCollection<UiTerminal> supportedTerminals)
    {
        TenantNId = Require(tenantNId, nameof(tenantNId));
        NId = Require(nId, nameof(nId));
        OwnerModuleNId = Require(ownerModuleNId, nameof(ownerModuleNId));
        ManifestVersion = Require(manifestVersion, nameof(manifestVersion));
        Type = type;
        Name = Require(name, nameof(name));
        RouteName = routeName?.Trim();
        RequiredPermissionNId = requiredPermissionNId?.Trim();
        SupportedTerminals = ValidateTerminals(supportedTerminals);
        if (type == UiResourceType.Page && string.IsNullOrWhiteSpace(RouteName))
            throw new ArgumentException("Page resource must declare a route.", nameof(routeName));
        if (string.IsNullOrWhiteSpace(RequiredPermissionNId))
            throw new ArgumentException("Every actionable resource must declare a permission.", nameof(requiredPermissionNId));
        Status = UiResourceStatus.Active;
    }

    public string TenantNId { get; }
    public string NId { get; }
    public string OwnerModuleNId { get; }
    public string ManifestVersion { get; }
    public UiResourceType Type { get; }
    public string Name { get; }
    public string? RouteName { get; }
    public string? RequiredPermissionNId { get; }
    public IReadOnlyCollection<UiTerminal> SupportedTerminals { get; }
    public UiResourceStatus Status { get; private set; }

    public static UiResource Create(string tenantNId, string nId, string ownerModuleNId, string manifestVersion,
        UiResourceType type, string name, string? routeName, string? requiredPermissionNId,
        IReadOnlyCollection<UiTerminal> supportedTerminals) =>
        new(tenantNId, nId, ownerModuleNId, manifestVersion, type, name, routeName, requiredPermissionNId, supportedTerminals);

    public void Retire() => Status = UiResourceStatus.Retired;

    private static string Require(string? value, string parameterName) =>
        string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("Value is required.", parameterName) : value.Trim();

    internal static IReadOnlyCollection<UiTerminal> ValidateTerminals(IReadOnlyCollection<UiTerminal>? terminals)
    {
        if (terminals is null || terminals.Count == 0) throw new ArgumentException("At least one terminal is required.", nameof(terminals));
        return terminals.Distinct().ToArray();
    }
}

public sealed class NavigationNode
{
    private NavigationNode(string tenantNId, string nId, NavigationNodeKind kind, string label,
        string? parentNodeNId, string navigationSetNId, string? resourceNId, string? featureNId,
        IReadOnlyCollection<UiTerminal> visibleTerminals, string? iconKey)
    {
        TenantNId = Require(tenantNId);
        NId = Require(nId);
        Kind = kind;
        Label = Require(label);
        ParentNodeNId = parentNodeNId?.Trim();
        NavigationSetNId = Require(navigationSetNId);
        ResourceNId = resourceNId?.Trim();
        FeatureNId = featureNId?.Trim();
        IconKey = ValidateIconKey(iconKey);
        VisibleTerminals = UiResource.ValidateTerminals(visibleTerminals);
        Status = NavigationNodeStatus.Active;
        if (kind == NavigationNodeKind.Group && ResourceNId is not null)
            throw new ArgumentException("A navigation group cannot reference a resource.", nameof(resourceNId));
        if (kind == NavigationNodeKind.Link && string.IsNullOrWhiteSpace(ResourceNId))
            throw new ArgumentException("A navigation link must reference a resource.", nameof(resourceNId));
    }

    public string TenantNId { get; }
    public string NId { get; }
    public NavigationNodeKind Kind { get; }
    public string Label { get; private set; }
    public string? ParentNodeNId { get; private set; }
    public string NavigationSetNId { get; }
    public string? ResourceNId { get; }
    public string? FeatureNId { get; }
    public string? IconKey { get; private set; }
    public IReadOnlyCollection<UiTerminal> VisibleTerminals { get; }
    public int DisplayOrder { get; private set; }
    public NavigationNodeStatus Status { get; private set; }

    public static NavigationNode CreateGroup(string tenantNId, string nId, string label, string? parentNodeNId, string navigationSetNId, string? iconKey = null) =>
        new(tenantNId, nId, NavigationNodeKind.Group, label, parentNodeNId, navigationSetNId, null, null, [UiTerminal.Pc, UiTerminal.Pda, UiTerminal.Mobile], iconKey);

    public static NavigationNode CreateLink(string tenantNId, string nId, string label, string? parentNodeNId,
        string navigationSetNId, string resourceNId, string? featureNId, IReadOnlyCollection<UiTerminal> visibleTerminals, string? iconKey = null) =>
        new(tenantNId, nId, NavigationNodeKind.Link, label, parentNodeNId, navigationSetNId, resourceNId, featureNId, visibleTerminals, iconKey);

    public void SetDisplayOrder(int displayOrder)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(displayOrder);
        DisplayOrder = displayOrder;
    }

    public void UpdateLabel(string label)
    {
        if (string.IsNullOrWhiteSpace(label)) throw new ArgumentException("Label is required.", nameof(label));
        Label = label.Trim();
    }

    public void UpdateIconKey(string? iconKey) => IconKey = ValidateIconKey(iconKey);

    public void Retire() => Status = NavigationNodeStatus.Inactive;

    private static string Require(string? value) => string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("Value is required.") : value.Trim();

    private static string? ValidateIconKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var candidate = value.Trim();
        if (candidate.Length > 64 || !candidate.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_'))
            throw new ArgumentException("IconKey must contain only ASCII letters, digits, '-' or '_'.", nameof(value));
        return candidate;
    }
}

public sealed record PermissionReceipt(string ModuleNId, string ManifestVersion, string Checksum, bool Verified);
public sealed record NavigationValidationError(string Code, string Message, string? NodeNId = null);
public sealed record NavigationValidationResult(IReadOnlyList<NavigationValidationError> Errors)
{
    public bool IsValid => Errors.Count == 0;
}

public static class NavigationValidator
{
    public static NavigationValidationResult Validate(
        IReadOnlyCollection<NavigationNode> nodes,
        IReadOnlyCollection<UiResource> resources,
        IReadOnlyCollection<FeatureDefinition> features,
        PermissionReceipt? permissionReceipt) =>
        Validate(nodes, resources, features, permissionReceipt is null ? [] : [permissionReceipt]);

    public static NavigationValidationResult Validate(
        IReadOnlyCollection<NavigationNode> nodes,
        IReadOnlyCollection<UiResource> resources,
        IReadOnlyCollection<FeatureDefinition> features,
        IReadOnlyCollection<PermissionReceipt> permissionReceipts)
    {
        var errors = new List<NavigationValidationError>();
        var activeNodes = nodes.Where(n => n.Status == NavigationNodeStatus.Active).ToArray();
        var resourceMap = resources.ToDictionary(r => r.NId, StringComparer.OrdinalIgnoreCase);
        var featureMap = features.ToDictionary(f => f.NId, StringComparer.OrdinalIgnoreCase);
        var nodeIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var node in activeNodes)
        {
            if (!nodeIds.Add(node.NId)) errors.Add(new("DUPLICATE_NODE", "Navigation node id is duplicated.", node.NId));
            if (node.ParentNodeNId is not null && !activeNodes.Any(p => p.NId.Equals(node.ParentNodeNId, StringComparison.OrdinalIgnoreCase)))
                errors.Add(new("ORPHAN_NODE", "Navigation node parent does not exist.", node.NId));
            if (node.Kind == NavigationNodeKind.Group && !activeNodes.Any(child => child.ParentNodeNId?.Equals(node.NId, StringComparison.OrdinalIgnoreCase) == true))
                errors.Add(new("EMPTY_GROUP", "Navigation group has no active children.", node.NId));
            if (node.Kind != NavigationNodeKind.Link) continue;
            if (!resourceMap.TryGetValue(node.ResourceNId!, out var resource))
            {
                errors.Add(new("RESOURCE_NOT_FOUND", "Navigation resource does not exist.", node.NId));
                continue;
            }
            if (resource.Status == UiResourceStatus.Retired) errors.Add(new("RESOURCE_RETIRED", "Retired resource cannot be published.", node.NId));
            if (!node.VisibleTerminals.All(resource.SupportedTerminals.Contains)) errors.Add(new("TERMINAL_NOT_SUPPORTED", "Visible terminals exceed resource support.", node.NId));
            if (node.FeatureNId is not null && (!featureMap.TryGetValue(node.FeatureNId, out var feature) || feature.Status == FeatureStatus.Retired))
                errors.Add(new("FEATURE_INVALID", "Navigation feature is missing or retired.", node.NId));
            if (!permissionReceipts.Any(receipt => receipt.Verified && receipt.ModuleNId.Equals(resource.OwnerModuleNId, StringComparison.OrdinalIgnoreCase)))
                errors.Add(new("PERMISSION_UNVERIFIED", "Resource permission registration is not verified.", node.NId));
        }

        foreach (var node in activeNodes)
        {
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var parent = node.ParentNodeNId; parent is not null; parent = activeNodes.FirstOrDefault(n => n.NId.Equals(parent, StringComparison.OrdinalIgnoreCase))?.ParentNodeNId)
                if (!visited.Add(parent)) { errors.Add(new("NODE_CYCLE", "Navigation nodes cannot form a cycle.", node.NId)); break; }
        }
        return new NavigationValidationResult(errors);
    }
}

public sealed class FeatureDefinition
{
    private FeatureDefinition(string tenantNId, string nId, string ownerModuleNId, string name, bool defaultEnabled, string? description)
    {
        TenantNId = Require(tenantNId); NId = Require(nId); OwnerModuleNId = Require(ownerModuleNId); Name = Require(name); Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(); DefaultEnabled = defaultEnabled; Status = FeatureStatus.Active;
    }
    public string TenantNId { get; }
    public string NId { get; }
    public string OwnerModuleNId { get; }
    public string Name { get; }
    public string? Description { get; private set; }
    public bool DefaultEnabled { get; private set; }
    public FeatureStatus Status { get; private set; }
    public long FeatureRevision { get; private set; } = 1;
    public static FeatureDefinition Create(string tenantNId, string nId, string ownerModuleNId, string name, bool defaultEnabled, string? description = null) => new(tenantNId, nId, ownerModuleNId, name, defaultEnabled, description);
    public FeatureDefinition WithRevision(long revision)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(revision, 1);
        return new FeatureDefinition(TenantNId, NId, OwnerModuleNId, Name, DefaultEnabled, Description) { FeatureRevision = revision, Status = Status };
    }
    public void Retire() => Status = FeatureStatus.Retired;
    public bool Resolve(FeatureOverride? tenantOverride, IReadOnlyCollection<string> emergencyDisabled)
    {
        if (Status == FeatureStatus.Retired || emergencyDisabled.Contains(NId, StringComparer.OrdinalIgnoreCase)) return false;
        return tenantOverride?.Mode switch { FeatureOverrideMode.Enabled => true, FeatureOverrideMode.Disabled => false, _ => DefaultEnabled };
    }
    private static string Require(string? value) => string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("Value is required.") : value.Trim();
}

public sealed record FeatureOverride(string TenantNId, string FeatureNId, FeatureOverrideMode Mode, string? Reason)
{
    public static FeatureOverride Create(string tenantNId, string featureNId, FeatureOverrideMode mode, string? reason) =>
        new(Require(tenantNId), Require(featureNId), mode, reason?.Trim());

    private static string Require(string? value) => string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("Value is required.") : value.Trim();
}

public sealed class ServiceCatalogEntry
{
    private ServiceCatalogEntry(string tenantNId, string nId, string name, ServiceCatalogKind kind, string endpoint, IReadOnlyCollection<UiTerminal> terminals)
    {
        TenantNId = Require(tenantNId); NId = Require(nId); Name = Require(name); Kind = kind; EntryPoint = endpoint; SupportedTerminals = UiResource.ValidateTerminals(terminals); Status = ServiceCatalogStatus.Active; Source = kind == ServiceCatalogKind.External ? ServiceCatalogSource.Manual : ServiceCatalogSource.Manifest;
    }
    public string TenantNId { get; }
    public string NId { get; }
    public string Name { get; private set; }
    public ServiceCatalogKind Kind { get; }
    public string EntryPoint { get; private set; }
    public string? Description { get; private set; }
    public string? GatewayPathPrefix { get; private set; }
    public string? HealthPath { get; private set; }
    public string? OwnerOrganizationNId { get; private set; }
    public string? OwnerOrganizationNameSnapshot { get; private set; }
    public string? TechnicalLeadUserNId { get; private set; }
    public string? TechnicalLeadDisplayNameSnapshot { get; private set; }
    public string? TechnicalOwnerAuthVersion { get; private set; }
    public string? TechnicalOwnerUserNId => TechnicalLeadUserNId;
    public string? OwnerDisplaySnapshot { get; private set; }
    public IReadOnlyCollection<UiTerminal> SupportedTerminals { get; }
    public ServiceCatalogStatus Status { get; private set; }
    public ServiceCatalogSource Source { get; }

    public static ServiceCatalogEntry CreateExternal(string tenantNId, string nId, string name, string endpoint, IReadOnlyCollection<UiTerminal> terminals)
    {
        return new(tenantNId, nId, name, ServiceCatalogKind.External, ValidateExternalEndpoint(endpoint), terminals);
    }

    public static ServiceCatalogEntry CreatePlatform(string tenantNId, string nId, string name, string relativeEntryPoint, string healthPath, IReadOnlyCollection<UiTerminal> terminals)
    {
        if (!relativeEntryPoint.StartsWith('/') || relativeEntryPoint.StartsWith("//", StringComparison.Ordinal)) throw new ArgumentException("Platform entry point must be relative.", nameof(relativeEntryPoint));
        if (!healthPath.StartsWith("/health", StringComparison.Ordinal)) throw new ArgumentException("Health path must be under /health.", nameof(healthPath));
        var entry = new ServiceCatalogEntry(tenantNId, nId, name, ServiceCatalogKind.Platform, relativeEntryPoint, terminals) { HealthPath = healthPath };
        return entry;
    }

    public void SetMetadata(string? description, string? gatewayPathPrefix)
    {
        Description = Limit(description);
        GatewayPathPrefix = Limit(gatewayPathPrefix);
    }

    public void UpdateName(string name) => Name = Require(name);

    public void UpdateExternalEntryPoint(string endpoint)
    {
        if (Kind != ServiceCatalogKind.External)
            throw new ArgumentException("Only external catalog entries can change EntryPoint.", nameof(endpoint));

        EntryPoint = ValidateExternalEndpoint(endpoint);
    }

    public void SetOwnerSnapshot(string? organizationNId, string? organizationName, string? technicalLeadUserNId, string? technicalLeadName, string? technicalOwnerAuthVersion = null)
    {
        var values = new[] { organizationNId, organizationName, technicalLeadUserNId, technicalLeadName };
        if (values.All(string.IsNullOrWhiteSpace))
        {
            OwnerOrganizationNId = OwnerOrganizationNameSnapshot = TechnicalLeadUserNId = TechnicalLeadDisplayNameSnapshot = null;
            TechnicalOwnerAuthVersion = OwnerDisplaySnapshot = null;
            return;
        }

        if (values.Any(string.IsNullOrWhiteSpace)) throw new ArgumentException("Owner organization and technical lead snapshots must be complete.");
        OwnerOrganizationNId = Require(organizationNId);
        OwnerOrganizationNameSnapshot = Limit(organizationName);
        TechnicalLeadUserNId = Require(technicalLeadUserNId);
        TechnicalLeadDisplayNameSnapshot = Limit(technicalLeadName);
        OwnerDisplaySnapshot = $"{OwnerOrganizationNameSnapshot} / {TechnicalLeadDisplayNameSnapshot}";
        TechnicalOwnerAuthVersion = Limit(technicalOwnerAuthVersion);
    }

    public void Retire() => Status = ServiceCatalogStatus.Retired;
    public void SetInactive() { if (Status != ServiceCatalogStatus.Retired) Status = ServiceCatalogStatus.Inactive; }
    public void SetActive() { if (Status != ServiceCatalogStatus.Retired) Status = ServiceCatalogStatus.Active; }

    private static string Require(string? value) => string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("Value is required.") : value.Trim();
    private static string ValidateExternalEndpoint(string? endpoint)
    {
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Fragment))
            throw new ArgumentException("External catalog entry must be a safe HTTPS URL.", nameof(endpoint));
        if (uri.IsLoopback || string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase) || IsPrivate(uri.Host))
            throw new ArgumentException("External catalog entry cannot target a local or private host.", nameof(endpoint));
        return uri.AbsoluteUri;
    }
    private static bool IsPrivate(string host)
    {
        if (!IPAddress.TryParse(host, out var ip)) return false;
        var bytes = ip.GetAddressBytes();
        return ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork &&
               (bytes[0] == 10 || bytes[0] == 127 || (bytes[0] == 169 && bytes[1] == 254) || (bytes[0] == 192 && bytes[1] == 168) || (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31));
    }

    private static string? Limit(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim()[..Math.Min(value.Trim().Length, 256)];
}

public sealed class ThemePolicy
{
    private ThemePolicy(string tenantNId, IReadOnlyCollection<ThemePalette> palettes, IReadOnlyCollection<ThemeMode> modes, IReadOnlyCollection<PcDensity> densities, ThemePalette defaultPalette, ThemeMode defaultMode, PcDensity defaultDensity)
    {
        TenantNId = string.IsNullOrWhiteSpace(tenantNId) ? throw new ArgumentException("Tenant is required.") : tenantNId.Trim();
        AllowedPalettes = Validate(palettes, defaultPalette); AllowedModes = Validate(modes, defaultMode); AllowedPcDensities = Validate(densities, defaultDensity); DefaultPalette = defaultPalette; DefaultMode = defaultMode; DefaultPcDensity = defaultDensity;
    }
    public string TenantNId { get; }
    public IReadOnlyCollection<ThemePalette> AllowedPalettes { get; }
    public IReadOnlyCollection<ThemeMode> AllowedModes { get; }
    public IReadOnlyCollection<PcDensity> AllowedPcDensities { get; }
    public ThemePalette DefaultPalette { get; }
    public ThemeMode DefaultMode { get; }
    public PcDensity DefaultPcDensity { get; }
    public long PolicyRevision { get; private set; } = 1;
    public static ThemePolicy Create(string tenantNId, IReadOnlyCollection<ThemePalette> palettes, IReadOnlyCollection<ThemeMode> modes, IReadOnlyCollection<PcDensity> densities, ThemePalette defaultPalette, ThemeMode defaultMode, PcDensity defaultDensity) => new(tenantNId, palettes, modes, densities, defaultPalette, defaultMode, defaultDensity);
    public ThemePolicy WithRevision(long revision)
    {
        var copy = new ThemePolicy(TenantNId, AllowedPalettes, AllowedModes, AllowedPcDensities, DefaultPalette, DefaultMode, DefaultPcDensity) { PolicyRevision = revision };
        return copy;
    }
    private static T[] Validate<T>(IReadOnlyCollection<T>? values, T @default) where T : struct, Enum => values is null || values.Count == 0 || !values.Contains(@default) ? throw new ArgumentException("Theme policy allowed values cannot be empty and must contain the default.") : values.Distinct().ToArray();
}
