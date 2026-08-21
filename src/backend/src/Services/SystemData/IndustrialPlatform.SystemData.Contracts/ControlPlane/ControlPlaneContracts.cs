using System.Text.Json.Serialization;

namespace IndustrialPlatform.SystemData.Contracts.ControlPlane;

public sealed record RegisterModuleManifestRequest
{
    public string? ModuleNId { get; init; }
    public string? ManifestVersion { get; init; }
    public string? Checksum { get; init; }
    public IReadOnlyCollection<string>? PermissionNIds { get; init; }
    public IReadOnlyCollection<PermissionDeclarationRequest>? Permissions { get; init; }
    public IReadOnlyCollection<RegisterUiResourceRequest>? Resources { get; init; }
    public IReadOnlyCollection<RegisterFeatureDefinitionRequest>? Features { get; init; }
}

public sealed record PermissionDeclarationRequest
{
    public string? PermissionNId { get; init; }
    public string? Name { get; init; }
    public string? ResourceType { get; init; }
    public string? ParentPermissionNId { get; init; }
}

public sealed record ModuleManifestResponse
{
    public string ModuleNId { get; init; } = string.Empty;
    public string ManifestVersion { get; init; } = string.Empty;
    public string Checksum { get; init; } = string.Empty;
    public IReadOnlyCollection<string> PermissionNIds { get; init; } = [];
    public IReadOnlyCollection<PermissionDeclarationRequest> Permissions { get; init; } = [];
    public IReadOnlyCollection<string> ResourceNIds { get; init; } = [];
    public IReadOnlyCollection<string> FeatureNIds { get; init; } = [];
    public bool PermissionVerified { get; init; }
    public DateTimeOffset? PermissionVerifiedOn { get; init; }
}

public sealed record RegisterFeatureDefinitionRequest
{
    public string? FeatureNId { get; init; }
    public string? OwnerModuleNId { get; init; }
    public string? Name { get; init; }
    public string? Description { get; init; }
    public bool DefaultEnabled { get; init; }
}

public sealed record RegisterUiResourceRequest
{
    public string? ResourceNId { get; init; }
    public string? OwnerModuleNId { get; init; }
    public string? ManifestVersion { get; init; }
    public string? Type { get; init; }
    public string? Name { get; init; }
    public string? RouteName { get; init; }
    public string? RequiredPermissionNId { get; init; }
    public IReadOnlyCollection<string>? SupportedTerminals { get; init; }
}

public sealed record UiResourceResponse
{
    public string ResourceNId { get; init; } = string.Empty;
    public string OwnerModuleNId { get; init; } = string.Empty;
    public string ManifestVersion { get; init; } = string.Empty;
    public string Type { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? RouteName { get; init; }
    public string? RequiredPermissionNId { get; init; }
    public IReadOnlyCollection<string> SupportedTerminals { get; init; } = [];
    public string Status { get; init; } = string.Empty;
}

public sealed record CreateNavigationNodeRequest
{
    public string? NodeNId { get; init; }
    public string? Kind { get; init; }
    public string? Label { get; init; }
    public string? ParentNodeNId { get; init; }
    public string? NavigationSetNId { get; init; }
    public string? ResourceNId { get; init; }
    public string? FeatureNId { get; init; }
    public string? IconKey { get; init; }
    public IReadOnlyCollection<string>? VisibleTerminals { get; init; }
    public int? DisplayOrder { get; init; }
}

public sealed record UpdateNavigationNodeRequest
{
    public string? Label { get; init; }
    public string? IconKey { get; init; }
    public int? DisplayOrder { get; init; }
}

public sealed record NavigationNodeResponse
{
    public string NodeNId { get; init; } = string.Empty;
    public string Kind { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
    public string? ParentNodeNId { get; init; }
    public string? ResourceNId { get; init; }
    public string? FeatureNId { get; init; }
    public string? IconKey { get; init; }
    public int DisplayOrder { get; init; }
    public IReadOnlyCollection<string> VisibleTerminals { get; init; } = [];
    public string Status { get; init; } = string.Empty;
    public IReadOnlyCollection<NavigationNodeResponse> Children { get; init; } = [];
}

public sealed record NavigationDraftResponse
{
    public long DraftRevision { get; init; }
    public IReadOnlyCollection<NavigationNodeResponse> Nodes { get; init; } = [];
}

public sealed record NavigationValidationResponse
{
    public bool IsValid { get; init; }
    public IReadOnlyCollection<NavigationValidationErrorResponse> Errors { get; init; } = [];
}

public sealed record NavigationValidationErrorResponse
{
    public string Code { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public string? NodeNId { get; init; }
}

public sealed record NavigationRuntimeResponse
{
    public long Revision { get; init; }
    public bool Degraded { get; init; }
    public IReadOnlyCollection<NavigationRuntimeNodeResponse> Nodes { get; init; } = [];
}

public sealed record NavigationRuntimeNodeResponse
{
    public string NodeNId { get; init; } = string.Empty;
    public string Kind { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
    public string? ResourceNId { get; init; }
    public string? RouteName { get; init; }
    public string? RequiredPermissionNId { get; init; }
    public string? FeatureNId { get; init; }
    public string? IconKey { get; init; }
    public int DisplayOrder { get; init; }
    public IReadOnlyCollection<NavigationRuntimeNodeResponse> Children { get; init; } = [];
}

public sealed record FeatureDefinitionResponse
{
    public string FeatureNId { get; init; } = string.Empty;
    public string OwnerModuleNId { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public bool DefaultEnabled { get; init; }
    public string Status { get; init; } = string.Empty;
    public long FeatureRevision { get; init; }
    public bool EffectiveEnabled { get; init; }
}

public sealed record SetFeatureOverrideRequest
{
    public string? Mode { get; init; }
    public string? Reason { get; init; }
}

public sealed record FeatureRuntimeResponse
{
    public long Revision { get; init; }
    public bool Degraded { get; init; }
    public IReadOnlyCollection<FeatureRuntimeItem> Items { get; init; } = [];
}

public sealed record FeatureRuntimeItem(string FeatureNId, bool Enabled);

public sealed record CreateExternalServiceCatalogRequest
{
    public string? ServiceNId { get; init; }
    public string? Name { get; init; }
    public string? Description { get; init; }
    public string? EntryPoint { get; init; }
    public IReadOnlyCollection<string>? SupportedTerminals { get; init; }
    public string? OwnerOrganizationNId { get; init; }
    public string? TechnicalLeadUserNId { get; init; }
    public string? TechnicalOwnerUserNId { get; init; }
}

public sealed record UpdateServiceCatalogRequest
{
    public string? Name { get; init; }
    public string? Description { get; init; }
    public string? EntryPoint { get; init; }
    public string? OwnerOrganizationNId { get; init; }
    public string? TechnicalLeadUserNId { get; init; }
    public string? TechnicalOwnerUserNId { get; init; }
    public string? Status { get; init; }
}

public sealed record ServiceCatalogResponse
{
    public string ServiceNId { get; init; } = string.Empty;
    public string Kind { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string EntryPoint { get; init; } = string.Empty;
    public string? GatewayPathPrefix { get; init; }
    public string? HealthPath { get; init; }
    public string? OwnerOrganizationNId { get; init; }
    public string? OwnerOrganizationNameSnapshot { get; init; }
    public string? OwnerDisplaySnapshot { get; init; }
    public string? TechnicalLeadUserNId { get; init; }
    public string? TechnicalOwnerUserNId { get; init; }
    public string? TechnicalLeadDisplayNameSnapshot { get; init; }
    public string? TechnicalOwnerAuthVersion { get; init; }
    public IReadOnlyCollection<string> SupportedTerminals { get; init; } = [];
    public string Status { get; init; } = string.Empty;
    public string Source { get; init; } = string.Empty;
    public bool Degraded { get; init; }
}

public sealed record ServiceCatalogRuntimeResponse
{
    public long Revision { get; init; }
    public bool Degraded { get; init; }
    public IReadOnlyCollection<ServiceCatalogResponse> Items { get; init; } = [];
}

public sealed record SetCatalogStatusRequest { public string? Status { get; init; } }

public sealed record ThemePolicyRequest
{
    public IReadOnlyCollection<string>? AllowedPalettes { get; init; }
    public IReadOnlyCollection<string>? AllowedModes { get; init; }
    public IReadOnlyCollection<string>? AllowedPcDensities { get; init; }
    public string? DefaultPalette { get; init; }
    public string? DefaultMode { get; init; }
    public string? DefaultPcDensity { get; init; }
}

public sealed record ThemePolicyResponse
{
    public long PolicyRevision { get; init; }
    public bool Degraded { get; init; }
    public IReadOnlyCollection<string> AllowedPalettes { get; init; } = [];
    public IReadOnlyCollection<string> AllowedModes { get; init; } = [];
    public IReadOnlyCollection<string> AllowedPcDensities { get; init; } = [];
    public string DefaultPalette { get; init; } = string.Empty;
    public string DefaultMode { get; init; } = string.Empty;
    public string DefaultPcDensity { get; init; } = string.Empty;
}
