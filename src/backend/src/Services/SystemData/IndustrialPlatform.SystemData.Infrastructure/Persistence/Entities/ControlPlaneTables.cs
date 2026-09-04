using SqlSugar;

namespace IndustrialPlatform.SystemData.Infrastructure.Persistence.Entities;

public abstract class ControlPlaneEntityTable
{
    [SugarColumn(ColumnName = "id", IsPrimaryKey = true)] public Guid Id { get; set; }
    [SugarColumn(ColumnName = "is_frozen")] public bool IsFrozen { get; set; }
    [SugarColumn(ColumnName = "is_locked")] public bool IsLocked { get; set; }
    [SugarColumn(ColumnName = "is_deleted")] public bool IsDeleted { get; set; }
    [SugarColumn(ColumnName = "entity_type")] public string EntityType { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "created_on")] public DateTimeOffset CreatedOn { get; set; }
    [SugarColumn(ColumnName = "last_updated_on")] public DateTimeOffset LastUpdatedOn { get; set; }
    [SugarColumn(ColumnName = "optimistic_version")] public long OptimisticVersion { get; set; }
    [SugarColumn(ColumnName = "concurrency_version")] public Guid ConcurrencyVersion { get; set; }
    [SugarColumn(ColumnName = "tenant_n_id")] public string TenantNId { get; set; } = string.Empty;
}

[SugarTable("system_data_module_manifest")]
public sealed class SystemDataModuleManifestTable : ControlPlaneEntityTable
{
    [SugarColumn(ColumnName = "module_n_id")] public string ModuleNId { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "manifest_version")] public string ManifestVersion { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "checksum")] public string Checksum { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "permission_n_ids_json")] public string PermissionNIdsJson { get; set; } = "[]";
    [SugarColumn(ColumnName = "permission_manifest_json")] public string PermissionManifestJson { get; set; } = "[]";
    [SugarColumn(ColumnName = "resource_manifest_json")] public string ResourceManifestJson { get; set; } = "[]";
    [SugarColumn(ColumnName = "feature_manifest_json")] public string FeatureManifestJson { get; set; } = "[]";
    [SugarColumn(ColumnName = "permission_receipt_version")] public string? PermissionReceiptVersion { get; set; }
    [SugarColumn(ColumnName = "permission_receipt_checksum")] public string? PermissionReceiptChecksum { get; set; }
    [SugarColumn(ColumnName = "permission_verified_on")] public DateTimeOffset? PermissionVerifiedOn { get; set; }
}

[SugarTable("system_data_ui_resource")]
public sealed class SystemDataUiResourceTable : ControlPlaneEntityTable
{
    [SugarColumn(ColumnName = "resource_n_id")] public string ResourceNId { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "owner_module_n_id")] public string OwnerModuleNId { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "manifest_version")] public string ManifestVersion { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "resource_type")] public string ResourceType { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "name")] public string Name { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "route_name")] public string? RouteName { get; set; }
    [SugarColumn(ColumnName = "required_permission_n_id")] public string? RequiredPermissionNId { get; set; }
    [SugarColumn(ColumnName = "supported_terminals_json")] public string SupportedTerminalsJson { get; set; } = "[]";
    [SugarColumn(ColumnName = "status")] public string Status { get; set; } = string.Empty;
}

[SugarTable("system_data_navigation_set")]
public sealed class SystemDataNavigationSetTable : ControlPlaneEntityTable
{
    [SugarColumn(ColumnName = "navigation_set_n_id")] public string NavigationSetNId { get; set; } = "PLATFORM_NAVIGATION";
    [SugarColumn(ColumnName = "draft_revision")] public long DraftRevision { get; set; }
    [SugarColumn(ColumnName = "active_snapshot_revision")] public long? ActiveSnapshotRevision { get; set; }
    [SugarColumn(ColumnName = "previous_snapshot_revision")] public long? PreviousSnapshotRevision { get; set; }
}

[SugarTable("system_data_navigation")]
public sealed class SystemDataNavigationTable : ControlPlaneEntityTable
{
    [SugarColumn(ColumnName = "node_n_id")] public string NodeNId { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "navigation_set_n_id")] public string NavigationSetNId { get; set; } = "PLATFORM_NAVIGATION";
    [SugarColumn(ColumnName = "parent_node_n_id")] public string? ParentNodeNId { get; set; }
    [SugarColumn(ColumnName = "kind")] public string Kind { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "label")] public string Label { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "icon_key")] public string? IconKey { get; set; }
    [SugarColumn(ColumnName = "resource_n_id")] public string? ResourceNId { get; set; }
    [SugarColumn(ColumnName = "feature_n_id")] public string? FeatureNId { get; set; }
    [SugarColumn(ColumnName = "display_order")] public int DisplayOrder { get; set; }
    [SugarColumn(ColumnName = "visible_terminals_json")] public string VisibleTerminalsJson { get; set; } = "[]";
    [SugarColumn(ColumnName = "action_resource_n_ids_json")] public string ActionResourceNIdsJson { get; set; } = "[]";
    [SugarColumn(ColumnName = "status")] public string Status { get; set; } = string.Empty;
}

[SugarTable("system_data_navigation_snapshot")]
public sealed class SystemDataNavigationSnapshotTable : ControlPlaneEntityTable
{
    [SugarColumn(ColumnName = "snapshot_revision")] public long SnapshotRevision { get; set; }
    [SugarColumn(ColumnName = "published_on")] public DateTimeOffset PublishedOn { get; set; }
    [SugarColumn(ColumnName = "checksum")] public string Checksum { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "nodes_json")] public string NodesJson { get; set; } = "[]";
}

[SugarTable("system_data_feature")]
public sealed class SystemDataFeatureTable : ControlPlaneEntityTable
{
    [SugarColumn(ColumnName = "feature_n_id")] public string FeatureNId { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "owner_module_n_id")] public string OwnerModuleNId { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "name")] public string Name { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "description")] public string? Description { get; set; }
    [SugarColumn(ColumnName = "default_enabled")] public bool DefaultEnabled { get; set; }
    [SugarColumn(ColumnName = "status")] public string Status { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "feature_revision")] public long FeatureRevision { get; set; }
}

[SugarTable("system_data_feature_override")]
public sealed class SystemDataFeatureOverrideTable : ControlPlaneEntityTable
{
    [SugarColumn(ColumnName = "feature_n_id")] public string FeatureNId { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "mode")] public string Mode { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "reason")] public string? Reason { get; set; }
}

[SugarTable("system_data_service_catalog")]
public sealed class SystemDataServiceCatalogTable : ControlPlaneEntityTable
{
    [SugarColumn(ColumnName = "service_n_id")] public string ServiceNId { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "kind")] public string Kind { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "name")] public string Name { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "description")] public string? Description { get; set; }
    [SugarColumn(ColumnName = "entry_point")] public string EntryPoint { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "gateway_path_prefix")] public string? GatewayPathPrefix { get; set; }
    [SugarColumn(ColumnName = "health_path")] public string? HealthPath { get; set; }
    [SugarColumn(ColumnName = "supported_terminals_json")] public string SupportedTerminalsJson { get; set; } = "[]";
    [SugarColumn(ColumnName = "status")] public string Status { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "source")] public string Source { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "owner_organization_n_id")] public string? OwnerOrganizationNId { get; set; }
    [SugarColumn(ColumnName = "owner_organization_name_snapshot")] public string? OwnerOrganizationNameSnapshot { get; set; }
    [SugarColumn(ColumnName = "owner_display_snapshot")] public string? OwnerDisplaySnapshot { get; set; }
    [SugarColumn(ColumnName = "technical_lead_user_n_id")] public string? TechnicalLeadUserNId { get; set; }
    [SugarColumn(ColumnName = "technical_owner_user_n_id")] public string? TechnicalOwnerUserNId { get; set; }
    [SugarColumn(ColumnName = "technical_owner_auth_version")] public string? TechnicalOwnerAuthVersion { get; set; }
    [SugarColumn(ColumnName = "technical_lead_display_name_snapshot")] public string? TechnicalLeadDisplayNameSnapshot { get; set; }
}

[SugarTable("system_data_theme_policy")]
public sealed class SystemDataThemePolicyTable : ControlPlaneEntityTable
{
    [SugarColumn(ColumnName = "policy_n_id")] public string PolicyNId { get; set; } = "TENANT_THEME_POLICY";
    [SugarColumn(ColumnName = "allowed_palettes_json")] public string AllowedPalettesJson { get; set; } = "[]";
    [SugarColumn(ColumnName = "allowed_modes_json")] public string AllowedModesJson { get; set; } = "[]";
    [SugarColumn(ColumnName = "allowed_pc_densities_json")] public string AllowedPcDensitiesJson { get; set; } = "[]";
    [SugarColumn(ColumnName = "default_palette")] public string DefaultPalette { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "default_mode")] public string DefaultMode { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "default_pc_density")] public string DefaultPcDensity { get; set; } = string.Empty;
    [SugarColumn(ColumnName = "policy_revision")] public long PolicyRevision { get; set; }
}

[SugarTable("system_data_projection_revision")]
public sealed class SystemDataProjectionRevisionTable : ControlPlaneEntityTable
{
    [SugarColumn(ColumnName = "area")] public string Area { get; set; } = "control-plane";
    [SugarColumn(ColumnName = "revision")] public long Revision { get; set; }
    [SugarColumn(ColumnName = "generated_on")] public DateTimeOffset GeneratedOn { get; set; }
}
