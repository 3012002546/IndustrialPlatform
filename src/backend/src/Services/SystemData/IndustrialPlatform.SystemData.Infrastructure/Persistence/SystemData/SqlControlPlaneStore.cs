using System.Text.Json;
using System.Text.Json.Serialization;
using IndustrialPlatform.Infrastructure.Database;
using IndustrialPlatform.SharedKernel.Exceptions;
using IndustrialPlatform.SystemData.Application.Auditing;
using IndustrialPlatform.SystemData.Application.ControlPlane;
using IndustrialPlatform.SystemData.Application.Reliability;
using IndustrialPlatform.SystemData.Domain.ControlPlane;
using IndustrialPlatform.SystemData.Infrastructure.Persistence.Entities;

namespace IndustrialPlatform.SystemData.Infrastructure.Persistence.SystemData;

/// <summary>
/// SystemData 控制面真实 SQL 存储。所有控制面写模型、审计和 Outbox 在一个 SqlSugar 事务中提交；
/// projection_revision 是租户级乐观并发闸门，缓存永远不是权威来源。
/// </summary>
public sealed class SqlControlPlaneStore : IControlPlaneStore, IControlPlaneTenantSource
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
    private readonly SqlSugarDbContext _dbContext;

    public SqlControlPlaneStore(SqlSugarDbContext dbContext) => _dbContext = dbContext;

    public async Task<ControlPlaneSnapshot> LoadAsync(string tenantNId, CancellationToken cancellationToken)
    {
        var sugar = _dbContext.SqlSugar;
        var revision = await sugar.Queryable<SystemDataProjectionRevisionTable>()
            .Where(x => x.TenantNId == tenantNId && x.Area == "control-plane")
            .FirstAsync(cancellationToken);
        var manifests = await sugar.Queryable<SystemDataModuleManifestTable>().Where(x => x.TenantNId == tenantNId && !x.IsDeleted).ToListAsync(cancellationToken);
        var resources = await sugar.Queryable<SystemDataUiResourceTable>().Where(x => x.TenantNId == tenantNId && !x.IsDeleted).ToListAsync(cancellationToken);
        var navigation = await sugar.Queryable<SystemDataNavigationTable>().Where(x => x.TenantNId == tenantNId && !x.IsDeleted).ToListAsync(cancellationToken);
        var navigationSet = await sugar.Queryable<SystemDataNavigationSetTable>().Where(x => x.TenantNId == tenantNId && !x.IsDeleted).FirstAsync(cancellationToken);
        var snapshots = await sugar.Queryable<SystemDataNavigationSnapshotTable>().Where(x => x.TenantNId == tenantNId && !x.IsDeleted).ToListAsync(cancellationToken);
        var features = await sugar.Queryable<SystemDataFeatureTable>().Where(x => x.TenantNId == tenantNId && !x.IsDeleted).ToListAsync(cancellationToken);
        var overrides = await sugar.Queryable<SystemDataFeatureOverrideTable>().Where(x => x.TenantNId == tenantNId && !x.IsDeleted).ToListAsync(cancellationToken);
        var catalog = await sugar.Queryable<SystemDataServiceCatalogTable>().Where(x => x.TenantNId == tenantNId && !x.IsDeleted).ToListAsync(cancellationToken);
        var theme = await sugar.Queryable<SystemDataThemePolicyTable>().Where(x => x.TenantNId == tenantNId && !x.IsDeleted).FirstAsync(cancellationToken);

        var manifestStates = manifests.Select(ToManifest).ToArray();
        var receipts = manifestStates.Where(x => x.PermissionReceiptVersion is not null && x.PermissionReceiptChecksum is not null)
            .Select(x => new PermissionReceipt(x.ModuleNId, x.PermissionReceiptVersion!, x.PermissionReceiptChecksum!, x.PermissionVerifiedOn is not null)).ToArray();
        return new ControlPlaneSnapshot(
            tenantNId,
            revision?.Revision ?? 0,
            manifestStates,
            resources.Select(ToResource).ToArray(),
            navigation.Select(ToNavigation).ToArray(),
            snapshots.Select(ToSnapshot).ToArray(),
            navigationSet?.ActiveSnapshotRevision,
            navigationSet?.PreviousSnapshotRevision,
            features.Select(ToFeature).ToArray(),
            overrides.Select(x => FeatureOverride.Create(tenantNId, x.FeatureNId, Enum.Parse<FeatureOverrideMode>(x.Mode, true), x.Reason)).ToArray(),
            catalog.Select(ToCatalog).ToArray(),
            theme is null ? null : ToTheme(theme),
            receipts);
    }

    public async Task<IReadOnlyCollection<string>> GetTenantNIdsAsync(CancellationToken cancellationToken) =>
        (await _dbContext.SqlSugar.Queryable<SystemDataProjectionRevisionTable>().Where(x => !x.IsDeleted).Select(x => x.TenantNId).ToListAsync(cancellationToken)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    public Task<bool> SeedAppliedAsync(string tenantNId, string seedKey, string seedVersion, string checksum, CancellationToken cancellationToken) =>
        _dbContext.SqlSugar.Queryable<SystemDataSeedLedgerTable>().AnyAsync(x => x.TenantNId == tenantNId && x.SeedKey == seedKey && x.SeedVersion == seedVersion && x.Checksum == checksum && !x.IsDeleted, cancellationToken);

    public async Task<long> CommitAsync(ControlPlaneSnapshot snapshot, long expectedRevision, ControlPlaneCommit commit, CancellationToken cancellationToken)
    {
        var sugar = _dbContext.SqlSugar;
        var nextRevision = expectedRevision + 1;
        sugar.Ado.BeginTran();
        try
        {
            var current = await sugar.Queryable<SystemDataProjectionRevisionTable>()
                .Where(x => x.TenantNId == snapshot.TenantNId && x.Area == "control-plane")
                .FirstAsync(cancellationToken);
            var now = DateTimeOffset.UtcNow;
            if (current is null)
            {
                if (expectedRevision != 0)
                    throw new ConcurrencyException("SystemData control-plane revision does not exist; initial revision must be zero.");

                // 初次提交以唯一租户/区域行作为 CAS 的创建分支；并发创建由唯一约束回滚，不能继续覆盖控制面。
                await sugar.Insertable(new SystemDataProjectionRevisionTable
                {
                    Id = Guid.NewGuid(),
                    TenantNId = snapshot.TenantNId,
                    Area = "control-plane",
                    Revision = nextRevision,
                    GeneratedOn = now,
                    IsFrozen = false,
                    IsLocked = false,
                    IsDeleted = false,
                    EntityType = "SystemData.ProjectionRevision",
                    CreatedOn = now,
                    LastUpdatedOn = now,
                    OptimisticVersion = 1,
                    ConcurrencyVersion = Guid.NewGuid(),
                }).ExecuteCommandAsync(cancellationToken);
            }
            else
            {
                // 关键区必须由数据库条件更新取得：不能依赖前面的普通 SELECT，否则两个请求会同时通过检查。
                var affected = await sugar.Updateable<SystemDataProjectionRevisionTable>()
                    .SetColumns(x => new SystemDataProjectionRevisionTable
                    {
                        Revision = nextRevision,
                        GeneratedOn = now,
                        LastUpdatedOn = now,
                    })
                    .Where(x => x.Id == current.Id && x.Revision == expectedRevision && !x.IsDeleted)
                    .ExecuteCommandAsync(cancellationToken);
                if (affected != 1)
                    throw new ConcurrencyException("SystemData control-plane revision changed; reload before retrying.");
            }

            await DeleteTenantRowsAsync(snapshot.TenantNId, cancellationToken);
            await InsertIfAnyAsync(snapshot.Manifests.Select(x => ToManifestTable(x, now)).ToArray(), cancellationToken);
            await InsertIfAnyAsync(snapshot.Resources.Select(x => ToResourceTable(x, now)).ToArray(), cancellationToken);
            await InsertIfAnyAsync(snapshot.DraftNodes.Select(x => ToNavigationTable(x, now)).ToArray(), cancellationToken);
            if (snapshot.DraftNodes.Count > 0 || snapshot.ActiveSnapshotRevision is not null || snapshot.PreviousSnapshotRevision is not null)
                await sugar.Insertable(ToNavigationSetTable(snapshot, now)).ExecuteCommandAsync(cancellationToken);
            await InsertIfAnyAsync(snapshot.Snapshots.Select(x => ToSnapshotTable(x, snapshot.TenantNId, now)).ToArray(), cancellationToken);
            await InsertIfAnyAsync(snapshot.Features.Select(x => ToFeatureTable(x, snapshot.TenantNId, now)).ToArray(), cancellationToken);
            await InsertIfAnyAsync(snapshot.Overrides.Select(x => ToOverrideTable(x, snapshot.TenantNId, now)).ToArray(), cancellationToken);
            await InsertIfAnyAsync(snapshot.Catalog.Select(x => ToCatalogTable(x, now)).ToArray(), cancellationToken);
            if (snapshot.Theme is not null)
                await sugar.Insertable(ToThemeTable(snapshot.Theme, now)).ExecuteCommandAsync(cancellationToken);

            foreach (var audit in commit.Audits)
                await sugar.Insertable(ToAuditTable(audit, now)).ExecuteCommandAsync(cancellationToken);
            foreach (var item in commit.Events)
            {
                if (!await sugar.Queryable<SystemDataOutboxTable>().AnyAsync(x => x.EventId == item.EventId, cancellationToken))
                    await sugar.Insertable(new SystemDataOutboxTable { EventId = item.EventId, EventType = item.EventType, EventVersion = item.EventVersion, TenantNId = item.TenantNId, Payload = item.Payload, EventCreatedTime = item.CreatedOn, RetryCount = 0 }).ExecuteCommandAsync(cancellationToken);
            }
            foreach (var seed in commit.Seeds ?? [])
                await sugar.Insertable(new SystemDataSeedLedgerTable { Id = Guid.NewGuid(), TenantNId = seed.TenantNId, SeedKey = seed.SeedKey, SeedVersion = seed.SeedVersion, Checksum = seed.Checksum, AppliedOn = now, IsFrozen = true, IsLocked = false, IsDeleted = false, EntityType = "SystemData.SeedLedger", CreatedOn = now, LastUpdatedOn = now, OptimisticVersion = 1, ConcurrencyVersion = Guid.NewGuid() }).ExecuteCommandAsync(cancellationToken);
            sugar.Ado.CommitTran();
            return nextRevision;
        }
        catch
        {
            sugar.Ado.RollbackTran();
            throw;
        }
    }

    private async Task DeleteTenantRowsAsync(string tenantNId, CancellationToken cancellationToken)
    {
        var sugar = _dbContext.SqlSugar;
        await sugar.Deleteable<SystemDataModuleManifestTable>().Where(x => x.TenantNId == tenantNId).ExecuteCommandAsync(cancellationToken);
        await sugar.Deleteable<SystemDataUiResourceTable>().Where(x => x.TenantNId == tenantNId).ExecuteCommandAsync(cancellationToken);
        await sugar.Deleteable<SystemDataNavigationTable>().Where(x => x.TenantNId == tenantNId).ExecuteCommandAsync(cancellationToken);
        await sugar.Deleteable<SystemDataNavigationSetTable>().Where(x => x.TenantNId == tenantNId).ExecuteCommandAsync(cancellationToken);
        await sugar.Deleteable<SystemDataNavigationSnapshotTable>().Where(x => x.TenantNId == tenantNId).ExecuteCommandAsync(cancellationToken);
        await sugar.Deleteable<SystemDataFeatureOverrideTable>().Where(x => x.TenantNId == tenantNId).ExecuteCommandAsync(cancellationToken);
        await sugar.Deleteable<SystemDataFeatureTable>().Where(x => x.TenantNId == tenantNId).ExecuteCommandAsync(cancellationToken);
        await sugar.Deleteable<SystemDataServiceCatalogTable>().Where(x => x.TenantNId == tenantNId).ExecuteCommandAsync(cancellationToken);
        await sugar.Deleteable<SystemDataThemePolicyTable>().Where(x => x.TenantNId == tenantNId).ExecuteCommandAsync(cancellationToken);
    }

    private static ModuleManifestState ToManifest(SystemDataModuleManifestTable x) => new(x.TenantNId, x.ModuleNId, x.ManifestVersion, x.Checksum, Deserialize<string[]>(x.PermissionNIdsJson), x.PermissionReceiptVersion, x.PermissionReceiptChecksum, x.PermissionVerifiedOn) { PermissionDeclarationItems = Deserialize<PermissionManifestEntry[]>(string.IsNullOrWhiteSpace(x.PermissionManifestJson) ? "[]" : x.PermissionManifestJson), ResourceDeclarations = Deserialize<ModuleResourceDeclaration[]>(string.IsNullOrWhiteSpace(x.ResourceManifestJson) ? "[]" : x.ResourceManifestJson), FeatureDeclarations = Deserialize<ModuleFeatureDeclaration[]>(string.IsNullOrWhiteSpace(x.FeatureManifestJson) ? "[]" : x.FeatureManifestJson) };
    private static UiResource ToResource(SystemDataUiResourceTable x) { var r = UiResource.Create(x.TenantNId, x.ResourceNId, x.OwnerModuleNId, x.ManifestVersion, Enum.Parse<UiResourceType>(x.ResourceType, true), x.Name, x.RouteName, x.RequiredPermissionNId, Deserialize<UiTerminal[]>(x.SupportedTerminalsJson)); if (x.Status.Equals(nameof(UiResourceStatus.Retired), StringComparison.OrdinalIgnoreCase)) r.Retire(); return r; }
    private static NavigationNode ToNavigation(SystemDataNavigationTable x) { var terminals = Deserialize<UiTerminal[]>(x.VisibleTerminalsJson); var n = x.Kind.Equals(nameof(NavigationNodeKind.Group), StringComparison.OrdinalIgnoreCase) ? NavigationNode.CreateGroup(x.TenantNId, x.NodeNId, x.Label, x.ParentNodeNId, x.NavigationSetNId, x.IconKey) : NavigationNode.CreateLink(x.TenantNId, x.NodeNId, x.Label, x.ParentNodeNId, x.NavigationSetNId, x.ResourceNId!, x.FeatureNId, terminals, x.IconKey); n.SetDisplayOrder(x.DisplayOrder); if (x.Status.Equals(nameof(NavigationNodeStatus.Inactive), StringComparison.OrdinalIgnoreCase)) n.Retire(); return n; }
    private static PublishedNavigationSnapshot ToSnapshot(SystemDataNavigationSnapshotTable x) => new(x.SnapshotRevision, x.PublishedOn, Deserialize<PublishedNavigationNode[]>(x.NodesJson), x.Checksum);
    private static FeatureDefinition ToFeature(SystemDataFeatureTable x) { var f = FeatureDefinition.Create(x.TenantNId, x.FeatureNId, x.OwnerModuleNId, x.Name, x.DefaultEnabled, x.Description).WithRevision(x.FeatureRevision); if (x.Status.Equals(nameof(FeatureStatus.Retired), StringComparison.OrdinalIgnoreCase)) f.Retire(); return f; }
    private static ServiceCatalogEntry ToCatalog(SystemDataServiceCatalogTable x) { var e = x.Kind.Equals(nameof(ServiceCatalogKind.External), StringComparison.OrdinalIgnoreCase) ? ServiceCatalogEntry.CreateExternal(x.TenantNId, x.ServiceNId, x.Name, x.EntryPoint, Deserialize<UiTerminal[]>(x.SupportedTerminalsJson)) : ServiceCatalogEntry.CreatePlatform(x.TenantNId, x.ServiceNId, x.Name, x.EntryPoint, x.HealthPath ?? "/health", Deserialize<UiTerminal[]>(x.SupportedTerminalsJson)); e.SetMetadata(x.Description, x.GatewayPathPrefix); if (!string.IsNullOrWhiteSpace(x.OwnerOrganizationNId) || !string.IsNullOrWhiteSpace(x.TechnicalLeadUserNId) || !string.IsNullOrWhiteSpace(x.TechnicalOwnerUserNId)) e.SetOwnerSnapshot(x.OwnerOrganizationNId, x.OwnerOrganizationNameSnapshot, x.TechnicalOwnerUserNId ?? x.TechnicalLeadUserNId, x.TechnicalLeadDisplayNameSnapshot, x.TechnicalOwnerAuthVersion); if (x.Status.Equals(nameof(ServiceCatalogStatus.Retired), StringComparison.OrdinalIgnoreCase)) e.Retire(); else if (x.Status.Equals(nameof(ServiceCatalogStatus.Inactive), StringComparison.OrdinalIgnoreCase)) e.SetInactive(); return e; }
    private static ThemePolicy ToTheme(SystemDataThemePolicyTable x) => ThemePolicy.Create(x.TenantNId, Deserialize<ThemePalette[]>(x.AllowedPalettesJson), Deserialize<ThemeMode[]>(x.AllowedModesJson), Deserialize<PcDensity[]>(x.AllowedPcDensitiesJson), Enum.Parse<ThemePalette>(x.DefaultPalette, true), Enum.Parse<ThemeMode>(x.DefaultMode, true), Enum.Parse<PcDensity>(x.DefaultPcDensity, true)).WithRevision(x.PolicyRevision);

    private static SystemDataModuleManifestTable ToManifestTable(ModuleManifestState x, DateTimeOffset now)
    {
        var row = Base<SystemDataModuleManifestTable>(x.TenantNId, now); row.ModuleNId = x.ModuleNId; row.ManifestVersion = x.ManifestVersion; row.Checksum = x.Checksum; row.PermissionNIdsJson = Serialize(x.PermissionNIds); row.PermissionManifestJson = Serialize(x.PermissionDeclarations()); row.ResourceManifestJson = Serialize(x.ResourceDeclarations); row.FeatureManifestJson = Serialize(x.FeatureDeclarations); row.PermissionReceiptVersion = x.PermissionReceiptVersion; row.PermissionReceiptChecksum = x.PermissionReceiptChecksum; row.PermissionVerifiedOn = x.PermissionVerifiedOn; return row;
    }
    private static SystemDataUiResourceTable ToResourceTable(UiResource x, DateTimeOffset now) { var row = Base<SystemDataUiResourceTable>(x.TenantNId, now); row.ResourceNId = x.NId; row.OwnerModuleNId = x.OwnerModuleNId; row.ManifestVersion = x.ManifestVersion; row.ResourceType = x.Type.ToString(); row.Name = x.Name; row.RouteName = x.RouteName; row.RequiredPermissionNId = x.RequiredPermissionNId; row.SupportedTerminalsJson = Serialize(x.SupportedTerminals); row.Status = x.Status.ToString(); return row; }
    private static SystemDataNavigationTable ToNavigationTable(NavigationNode x, DateTimeOffset now) { var row = Base<SystemDataNavigationTable>(x.TenantNId, now); row.NodeNId = x.NId; row.NavigationSetNId = x.NavigationSetNId; row.ParentNodeNId = x.ParentNodeNId; row.Kind = x.Kind.ToString(); row.Label = x.Label; row.IconKey = x.IconKey; row.ResourceNId = x.ResourceNId; row.FeatureNId = x.FeatureNId; row.DisplayOrder = x.DisplayOrder; row.VisibleTerminalsJson = Serialize(x.VisibleTerminals); row.Status = x.Status.ToString(); return row; }
    private static SystemDataNavigationSetTable ToNavigationSetTable(ControlPlaneSnapshot x, DateTimeOffset now) { var row = Base<SystemDataNavigationSetTable>(x.TenantNId, now); row.DraftRevision = x.Revision; row.ActiveSnapshotRevision = x.ActiveSnapshotRevision; row.PreviousSnapshotRevision = x.PreviousSnapshotRevision; return row; }
    private static SystemDataNavigationSnapshotTable ToSnapshotTable(PublishedNavigationSnapshot x, string tenantNId, DateTimeOffset now) { var row = Base<SystemDataNavigationSnapshotTable>(tenantNId, now); row.SnapshotRevision = x.Revision; row.PublishedOn = x.PublishedOn; row.Checksum = x.Checksum ?? NavigationSnapshotChecksum.Compute(x.Nodes); row.NodesJson = Serialize(x.Nodes); return row; }
    private static SystemDataFeatureTable ToFeatureTable(FeatureDefinition x, string tenantNId, DateTimeOffset now) { var row = Base<SystemDataFeatureTable>(tenantNId, now); row.FeatureNId = x.NId; row.OwnerModuleNId = x.OwnerModuleNId; row.Name = x.Name; row.Description = x.Description; row.DefaultEnabled = x.DefaultEnabled; row.Status = x.Status.ToString(); row.FeatureRevision = x.FeatureRevision; return row; }
    private static SystemDataFeatureOverrideTable ToOverrideTable(FeatureOverride x, string tenantNId, DateTimeOffset now) { var row = Base<SystemDataFeatureOverrideTable>(tenantNId, now); row.FeatureNId = x.FeatureNId; row.Mode = x.Mode.ToString(); row.Reason = x.Reason; return row; }
    private static SystemDataServiceCatalogTable ToCatalogTable(ServiceCatalogEntry x, DateTimeOffset now) { var row = Base<SystemDataServiceCatalogTable>(x.TenantNId, now); row.ServiceNId = x.NId; row.Kind = x.Kind.ToString(); row.Name = x.Name; row.Description = x.Description; row.EntryPoint = x.EntryPoint; row.GatewayPathPrefix = x.GatewayPathPrefix; row.HealthPath = x.HealthPath; row.OwnerOrganizationNId = x.OwnerOrganizationNId; row.OwnerOrganizationNameSnapshot = x.OwnerOrganizationNameSnapshot; row.OwnerDisplaySnapshot = x.OwnerDisplaySnapshot; row.TechnicalLeadUserNId = x.TechnicalLeadUserNId; row.TechnicalOwnerUserNId = x.TechnicalOwnerUserNId; row.TechnicalOwnerAuthVersion = x.TechnicalOwnerAuthVersion; row.TechnicalLeadDisplayNameSnapshot = x.TechnicalLeadDisplayNameSnapshot; row.SupportedTerminalsJson = Serialize(x.SupportedTerminals); row.Status = x.Status.ToString(); row.Source = x.Source.ToString(); return row; }
    private static SystemDataThemePolicyTable ToThemeTable(ThemePolicy x, DateTimeOffset now) { var row = Base<SystemDataThemePolicyTable>(x.TenantNId, now); row.AllowedPalettesJson = Serialize(x.AllowedPalettes); row.AllowedModesJson = Serialize(x.AllowedModes); row.AllowedPcDensitiesJson = Serialize(x.AllowedPcDensities); row.DefaultPalette = x.DefaultPalette.ToString(); row.DefaultMode = x.DefaultMode.ToString(); row.DefaultPcDensity = x.DefaultPcDensity.ToString(); row.PolicyRevision = x.PolicyRevision; return row; }
    private static SystemDataOperationAuditTable ToAuditTable(LocalAuditEntry x, DateTimeOffset now) => new() { Id = Guid.NewGuid(), IsFrozen = true, IsLocked = false, IsDeleted = false, EntityType = "SystemData.OperationAudit", CreatedOn = now, LastUpdatedOn = now, OptimisticVersion = 1, ConcurrencyVersion = Guid.NewGuid(), TenantNId = x.TenantNId, ActorUserNId = x.ActorUserNId, Action = x.Action, ObjectType = x.ObjectType, ObjectNId = x.ObjectNId, Reason = Limit(x.Reason), BeforeSummary = Limit(x.BeforeSummary), AfterSummary = Limit(x.AfterSummary), TraceId = Limit(x.TraceId) ?? string.Empty };
    private async Task InsertIfAnyAsync<T>(T[] rows, CancellationToken cancellationToken) where T : class, new()
    {
        foreach (var row in rows) await _dbContext.SqlSugar.Insertable(row).ExecuteCommandAsync(cancellationToken);
    }
    private static T Base<T>(string tenantNId, DateTimeOffset now) where T : ControlPlaneEntityTable, new() => new() { Id = Guid.NewGuid(), TenantNId = tenantNId, IsFrozen = false, IsLocked = false, IsDeleted = false, EntityType = typeof(T).Name, CreatedOn = now, LastUpdatedOn = now, OptimisticVersion = 1, ConcurrencyVersion = Guid.NewGuid() };
    private static string Serialize<T>(T value) => JsonSerializer.Serialize(value, JsonOptions);
    private static T Deserialize<T>(string value) => JsonSerializer.Deserialize<T>(value, JsonOptions) ?? throw new InvalidOperationException("Control plane JSON payload is invalid.");
    private static string? Limit(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim()[..Math.Min(value.Trim().Length, 1000)];
}
