using IndustrialPlatform.SystemData.Application.Auditing;
using IndustrialPlatform.SystemData.Application.Reliability;
using IndustrialPlatform.SystemData.Domain.ControlPlane;

namespace IndustrialPlatform.SystemData.Application.ControlPlane;

public sealed record PermissionManifestEntry(string PermissionNId, string Name, string ResourceType, string? ParentPermissionNId);
public sealed record PermissionManifestV1(string ModuleNId, string ManifestVersion, string Checksum, IReadOnlyCollection<PermissionManifestEntry> Permissions);
public sealed record ModuleResourceDeclaration(string ResourceNId, string ManifestVersion, string Type, string Name, string? RouteName, string? RequiredPermissionNId, IReadOnlyCollection<UiTerminal> SupportedTerminals);
public sealed record ModuleFeatureDeclaration(string FeatureNId, string Name, string? Description, bool DefaultEnabled);

/// <summary>
/// 受信任模块清单的持久化投影。资源表保存逐资源记录，清单表保存版本、checksum 和权限声明，
/// 两者必须在同一个控制面提交中更新。
/// </summary>
public sealed record ModuleManifestState(
    string TenantNId,
    string ModuleNId,
    string ManifestVersion,
    string Checksum,
    IReadOnlyCollection<string> PermissionNIds,
    string? PermissionReceiptVersion,
    string? PermissionReceiptChecksum,
    DateTimeOffset? PermissionVerifiedOn)
{
    public IReadOnlyCollection<PermissionManifestEntry> PermissionDeclarationItems { get; init; } = [];
    public IReadOnlyCollection<ModuleResourceDeclaration> ResourceDeclarations { get; init; } = [];
    public IReadOnlyCollection<ModuleFeatureDeclaration> FeatureDeclarations { get; init; } = [];
}

public static class ModuleManifestStateExtensions
{
    public static IReadOnlyCollection<PermissionManifestEntry> PermissionDeclarations(this ModuleManifestState state) =>
        state.PermissionDeclarationItems.Count > 0
            ? state.PermissionDeclarationItems
            : state.PermissionNIds.Select(x => new PermissionManifestEntry(x, x, "permission", null)).ToArray();
}

/// <summary>数据库中一次租户控制面读取的完整一致性快照。</summary>
public sealed record ControlPlaneSnapshot(
    string TenantNId,
    long Revision,
    IReadOnlyCollection<ModuleManifestState> Manifests,
    IReadOnlyCollection<UiResource> Resources,
    IReadOnlyCollection<NavigationNode> DraftNodes,
    IReadOnlyCollection<PublishedNavigationSnapshot> Snapshots,
    long? ActiveSnapshotRevision,
    long? PreviousSnapshotRevision,
    IReadOnlyCollection<FeatureDefinition> Features,
    IReadOnlyCollection<FeatureOverride> Overrides,
    IReadOnlyCollection<ServiceCatalogEntry> Catalog,
    ThemePolicy? Theme,
    IReadOnlyCollection<PermissionReceipt> PermissionReceipts)
{
    public static ControlPlaneSnapshot Empty(string tenantNId) => new(
        tenantNId,
        0,
        [],
        [],
        [],
        [],
        null,
        null,
        [],
        [],
        [],
        null,
        []);
}

public sealed record SeedLedgerEntry(string TenantNId, string SeedKey, string SeedVersion, string Checksum);

public sealed record ControlPlaneCommit(
    IReadOnlyCollection<ControlPlaneEvent> Events,
    IReadOnlyCollection<LocalAuditEntry> Audits,
    IReadOnlyCollection<SeedLedgerEntry>? Seeds = null);

/// <summary>
/// SystemData 控制面持久化端口。实现必须以 expectedRevision 做乐观并发校验，
/// 并在同一个数据库事务中写入业务行、审计和 Outbox。
/// </summary>
public interface IControlPlaneStore
{
    Task<ControlPlaneSnapshot> LoadAsync(string tenantNId, CancellationToken cancellationToken);

    Task<long> CommitAsync(
        ControlPlaneSnapshot snapshot,
        long expectedRevision,
        ControlPlaneCommit commit,
        CancellationToken cancellationToken);

    Task<bool> SeedAppliedAsync(string tenantNId, string seedKey, string seedVersion, string checksum, CancellationToken cancellationToken);
}

public interface IControlPlaneTenantSource
{
    Task<IReadOnlyCollection<string>> GetTenantNIdsAsync(CancellationToken cancellationToken);
}
