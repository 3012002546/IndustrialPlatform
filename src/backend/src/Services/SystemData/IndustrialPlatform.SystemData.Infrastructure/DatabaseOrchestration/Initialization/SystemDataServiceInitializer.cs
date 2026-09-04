using IndustrialPlatform.Application.Abstractions.Initialization;
using IndustrialPlatform.Infrastructure.Database;
using IndustrialPlatform.SystemData.Infrastructure.Persistence.Migrations;
using IndustrialPlatform.SystemData.Application.ControlPlane;
using IndustrialPlatform.SystemData.Infrastructure.Reliability;
using IndustrialPlatform.SystemData.Domain.ControlPlane;
using SqlSugar;

namespace IndustrialPlatform.SystemData.Infrastructure.DatabaseOrchestration.Initialization;

/// <summary>SystemData 自有初始化器，只执行自己的 Migration Ledger。</summary>
public sealed class SystemDataServiceInitializer : IServiceInitializer
{
    private readonly ISchemaMigrationRunner _migrationRunner;
    private readonly SqlSugarDbContext _dbContext;
    private readonly IControlPlaneStore? _controlPlaneStore;
    private readonly SystemDataBaselineSeedRunner? _baselineSeeder;

    public SystemDataServiceInitializer(
        ISchemaMigrationRunner migrationRunner,
        SqlSugarDbContext dbContext,
        IControlPlaneStore? controlPlaneStore = null,
        SystemDataBaselineSeedRunner? baselineSeeder = null)
    {
        _migrationRunner = migrationRunner;
        _dbContext = dbContext;
        _controlPlaneStore = controlPlaneStore;
        _baselineSeeder = baselineSeeder;
    }

    public string ServiceKey => "systemdata";
    public string ModuleKey => "systemdata";

    public async Task<ServiceInitializationState> InspectAsync(ServiceInitializationContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var applied = (await _dbContext.SqlSugar.Queryable<SchemaMigrationRecord>()
                    .Select(record => record.MigrationId)
                    .ToListAsync(cancellationToken))
                .ToHashSet(StringComparer.Ordinal);
            var expected = SystemDataSchemaMigrations.All.Select(step => step.Id).ToArray();
            var missingMigrations = expected.Where(id => !applied.Contains(id)).ToArray();
            var missingColumns = await FindMissingCriticalColumnsAsync(cancellationToken);
            var migrationReady = missingMigrations.Length == 0 && missingColumns.Count == 0;
            if (!migrationReady)
            {
                var facts = new List<string>();
                if (missingMigrations.Length > 0) facts.Add($"缺少迁移:{string.Join(',', missingMigrations)}");
                if (missingColumns.Count > 0) facts.Add($"缺少列:{string.Join(',', missingColumns)}");
                var observedVersion = expected.LastOrDefault(applied.Contains);
                return NotReady(context.DesiredVersion, $"SystemData 本地架构尚未完成验证({string.Join(';', facts)})。", observedVersion);
            }

            var seedReady = true;
            var bootstrapReady = true;
            if (_controlPlaneStore is not null)
            {
                var controlPlane = await _controlPlaneStore.LoadAsync(context.TenantNId, cancellationToken);
                var feature = controlPlane.Features.SingleOrDefault(item => item.NId.Equals(SystemDataBaselineSeedRunner.RequiredFeatureNId, StringComparison.OrdinalIgnoreCase));
                var catalog = controlPlane.Catalog.SingleOrDefault(item => item.NId.Equals(SystemDataBaselineSeedRunner.RequiredCatalogNId, StringComparison.OrdinalIgnoreCase));
                var manifest = controlPlane.Manifests.SingleOrDefault(item => item.ModuleNId.Equals(ModuleKey, StringComparison.OrdinalIgnoreCase));
                var receipt = controlPlane.PermissionReceipts.SingleOrDefault(item => item.ModuleNId.Equals(ModuleKey, StringComparison.OrdinalIgnoreCase));
                seedReady = feature is { Status: FeatureStatus.Active, DefaultEnabled: true }
                    && feature.OwnerModuleNId.Equals(ModuleKey, StringComparison.OrdinalIgnoreCase)
                    && catalog is { Status: ServiceCatalogStatus.Active, Kind: ServiceCatalogKind.Platform, EntryPoint: "/api/v1/systemdata", HealthPath: "/health" }
                    && catalog.SupportedTerminals.Contains(UiTerminal.Pc)
                    && catalog.SupportedTerminals.Contains(UiTerminal.Pda)
                    && catalog.SupportedTerminals.Contains(UiTerminal.Mobile)
                    && SystemDataBaselineSeedRunner.IsCurrentTheme(controlPlane.Theme);
                var requiredPermissions = SystemDataBaselineSeedRunner.RequiredPermissionNIds.Order(StringComparer.OrdinalIgnoreCase).ToArray();
                var manifestPermissions = manifest?.PermissionNIds.Order(StringComparer.OrdinalIgnoreCase).ToArray();
                var requiredResourcesReady = SystemDataBaselineSeedRunner.RequiredResourceFacts.All(required =>
                    controlPlane.Resources.Any(resource =>
                        resource.NId.Equals(required.NId, StringComparison.OrdinalIgnoreCase)
                        && resource.OwnerModuleNId.Equals(ModuleKey, StringComparison.OrdinalIgnoreCase)
                        && resource.Type == UiResourceType.Page
                        && resource.ManifestVersion == SystemDataBaselineSeedRunner.CurrentManifestVersion
                        && resource.RouteName == required.RouteName
                        && resource.RequiredPermissionNId == required.PermissionNId
                        && resource.Status == UiResourceStatus.Active));
                bootstrapReady = manifest is not null
                    && manifest.ManifestVersion == SystemDataBaselineSeedRunner.CurrentManifestVersion
                    && manifest.Checksum.Equals(SystemDataBaselineSeedRunner.CurrentManifestChecksum, StringComparison.OrdinalIgnoreCase)
                    && manifestPermissions is not null
                    && manifestPermissions.SequenceEqual(requiredPermissions, StringComparer.OrdinalIgnoreCase)
                    && manifest.PermissionReceiptVersion == SystemDataBaselineSeedRunner.CurrentManifestVersion
                    && string.Equals(manifest.PermissionReceiptChecksum, SystemDataBaselineSeedRunner.CurrentManifestChecksum, StringComparison.OrdinalIgnoreCase)
                    && receipt is { Verified: true }
                    && receipt.ManifestVersion == SystemDataBaselineSeedRunner.CurrentManifestVersion
                    && receipt.Checksum.Equals(SystemDataBaselineSeedRunner.CurrentManifestChecksum, StringComparison.OrdinalIgnoreCase)
                    && requiredResourcesReady;
            }

            // 版本按声明的迁移序列计算，而不是按 AppliedOn 或字符串最大值，兼容历史补录旧编号。
            return Ready(expected[^1], seedReady, bootstrapReady);
        }
        catch (Exception exception) when (IsMissingLocalTable(exception))
        {
            return NotReady(context.DesiredVersion, "SystemData 本地迁移账本尚未创建。");
        }
    }

    public Task<ServiceInitializationPlan> PlanAsync(
        ServiceInitializationContext context,
        ServiceInitializationState inspection,
        CancellationToken cancellationToken) =>
        Task.FromResult(CreatePlan(context, inspection));

    public async Task<ServiceInitializationState> ApplyAsync(
        ServiceInitializationContext context,
        ServiceInitializationPlan plan,
        CancellationToken cancellationToken)
    {
        await _migrationRunner.ApplyPendingAsync(cancellationToken);
        if (_baselineSeeder is not null)
            await _baselineSeeder.ApplyAsync(context.TenantNId, cancellationToken);
        return await InspectAsync(context, cancellationToken);
    }

    public Task<ServiceInitializationState> VerifyAsync(ServiceInitializationContext context, CancellationToken cancellationToken) =>
        InspectAsync(context, cancellationToken);

    private ServiceInitializationState NotReady(string desiredVersion, string reason, string? observedVersion = null) =>
        new(ServiceKey, ModuleKey, observedVersion, false, false, false, false, reason);

    private ServiceInitializationState Ready(string version, bool seedReady, bool bootstrapReady) =>
        new(ServiceKey, ModuleKey, version, true, seedReady, bootstrapReady, seedReady && bootstrapReady,
            seedReady && bootstrapReady ? null : "SystemData 控制面种子或引导事实尚未完成。");

    private ServiceInitializationPlan CreatePlan(
        ServiceInitializationContext context,
        ServiceInitializationState inspection)
    {
        var desiredVersion = string.IsNullOrWhiteSpace(context.DesiredVersion)
            ? SystemDataSchemaMigrations.All[^1].Id
            : context.DesiredVersion;
        var steps = new List<string>();
        if (!inspection.MigrationReady || !string.Equals(inspection.ObservedVersion, desiredVersion, StringComparison.Ordinal))
            steps.Add("migration");
        if (!inspection.RequiredSeedReady)
            steps.Add("required-seed");
        if (!inspection.BootstrapReady)
            steps.Add("bootstrap");
        if (steps.Count > 0)
            steps.Add("verify");

        return new ServiceInitializationPlan(
            ServiceKey,
            ModuleKey,
            inspection.ObservedVersion,
            desiredVersion,
            steps.Count > 0,
            steps);
    }

    private async Task<IReadOnlyList<string>> FindMissingCriticalColumnsAsync(CancellationToken cancellationToken)
    {
        var required = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["system_data_database_registration"] = ["uses_service_initializer"],
            ["system_data_database_plan"] = ["service_requires_apply"],
        };
        var missing = new List<string>();
        foreach (var pair in required)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var columns = _dbContext.SqlSugar.CurrentConnectionConfig.DbType == DbType.Sqlite
                ? _dbContext.SqlSugar.Ado.GetDataTable($"SELECT name FROM pragma_table_info('{pair.Key}')")
                    .Rows.Cast<System.Data.DataRow>()
                    .Select(row => row["name"]?.ToString())
                : _dbContext.SqlSugar.Ado.GetDataTable(
                        "SELECT column_name FROM information_schema.columns "
                        + $"WHERE table_schema = current_schema() AND table_name = '{pair.Key}'")
                    .Rows.Cast<System.Data.DataRow>()
                    .Select(row => row["column_name"]?.ToString());
            var actual = columns.Where(value => value is not null).Select(value => value!).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var column in pair.Value.Where(column => !actual.Contains(column)))
                missing.Add($"{pair.Key}.{column}");
        }
        return missing;
    }

    private static bool IsMissingLocalTable(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            var message = current.Message;
            if (message.Contains("no such table", StringComparison.OrdinalIgnoreCase)
                || (message.Contains("relation", StringComparison.OrdinalIgnoreCase)
                    && message.Contains("does not exist", StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        return false;
    }
}
