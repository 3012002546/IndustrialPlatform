using System.Security.Cryptography;
using System.Text;
using IndustrialPlatform.SystemData.Application.ControlPlane;
using IndustrialPlatform.SystemData.Application.Reliability;
using IndustrialPlatform.SystemData.Domain.ControlPlane;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace IndustrialPlatform.SystemData.Infrastructure.Reliability;

/// <summary>
/// SDM-013～016 SystemBaseline/TenantBaseline。只有显式配置 BaselineTenantNId 才运行，
/// 不创建固定生产租户；每个 seed 与版本/checksum 写入同一控制面事务的 seed ledger。
/// </summary>
public sealed partial class SystemDataBaselineSeedRunner : BackgroundService
{
    private static readonly string[] SystemDataPermissions = [
        "systemdata.organization.view", "systemdata.organization.create", "systemdata.organization.update", "systemdata.organization.move", "systemdata.organization.status",
        "systemdata.position.view", "systemdata.position.create", "systemdata.position.update", "systemdata.position.status",
        "systemdata.assignment.view", "systemdata.assignment.manage",
        "systemdata.resource.view", "systemdata.navigation.view", "systemdata.navigation.manage", "systemdata.navigation.publish", "systemdata.navigation.rollback",
        "systemdata.feature.view", "systemdata.feature.manage", "systemdata.service-catalog.view", "systemdata.service-catalog.manage", "systemdata.theme-policy.view", "systemdata.theme-policy.manage",
        "systemdata.database-orchestration.view", "systemdata.database-orchestration.register", "systemdata.database-orchestration.plan", "systemdata.database-orchestration.apply", "systemdata.database-orchestration.approve", "systemdata.database-orchestration.backup", "systemdata.database-orchestration.cancel",
        "systemdata.service-initialization.view", "systemdata.service-initialization.register", "systemdata.service-initialization.plan", "systemdata.service-initialization.apply", "systemdata.service-initialization.approve", "systemdata.service-initialization.backup", "systemdata.service-initialization.cancel"];
    private readonly IConfiguration _configuration;
    private readonly IControlPlaneStore _store;
    private readonly ILogger<SystemDataBaselineSeedRunner> _logger;

    public SystemDataBaselineSeedRunner(IConfiguration configuration, IControlPlaneStore store, ILogger<SystemDataBaselineSeedRunner> logger)
    { _configuration = configuration; _store = store; _logger = logger; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var tenant = _configuration["SystemData:BaselineTenantNId"];
        if (string.IsNullOrWhiteSpace(tenant)) return;
        try { await ApplyAsync(tenant.Trim(), stoppingToken); }
        catch (Exception exception) when (exception is not OperationCanceledException) { LogSeedFailed(_logger, exception); }
    }

    public async Task ApplyAsync(string tenantNId, CancellationToken cancellationToken)
    {
        await ApplySeedAsync(tenantNId, "SDM-013", "1", ["permissions", "resources", "navigation"], state =>
        {
            var manifest = new ModuleManifestState(tenantNId, "systemdata", "1", Checksum("SDM-013"), SystemDataPermissions, null, null, null)
            {
                PermissionDeclarationItems = SystemDataPermissions.Select(x => new PermissionManifestEntry(x, x, x.Contains('.') ? x[..x.LastIndexOf('.')].Replace('.', ':') : "systemdata", null)).ToArray()
            };
            var resource = UiResource.Create(tenantNId, "systemdata.navigation", "systemdata", "1", UiResourceType.Page, "SystemData Navigation", "/systemdata/navigation", "systemdata.navigation.view", [UiTerminal.Pc, UiTerminal.Pda, UiTerminal.Mobile]);
            return state with { Manifests = state.Manifests.Append(manifest).ToArray(), Resources = state.Resources.Append(resource).ToArray() };
        }, cancellationToken);
        await ApplySeedAsync(tenantNId, "SDM-014", "1", ["features"], state =>
        {
            var feature = FeatureDefinition.Create(tenantNId, "systemdata.control-plane", "systemdata", "SystemData control plane", true);
            return state with { Features = state.Features.Append(feature).ToArray() };
        }, cancellationToken);
        await ApplySeedAsync(tenantNId, "SDM-015", "1", ["service-catalog"], state =>
        {
            var entry = ServiceCatalogEntry.CreatePlatform(tenantNId, "systemdata", "SystemData", "/api/v1/systemdata", "/health", [UiTerminal.Pc, UiTerminal.Pda, UiTerminal.Mobile]);
            return state with { Catalog = state.Catalog.Append(entry).ToArray() };
        }, cancellationToken);
        await ApplySeedAsync(tenantNId, "SDM-016", "1", ["theme-policy"], state =>
        {
            var theme = ThemePolicy.Create(tenantNId, [ThemePalette.IndustrialCyan, ThemePalette.TechnologyBlue, ThemePalette.NeutralGray], [ThemeMode.Light, ThemeMode.Dark, ThemeMode.System], [PcDensity.Comfortable, PcDensity.Compact], ThemePalette.IndustrialCyan, ThemeMode.Light, PcDensity.Comfortable);
            return state with { Theme = theme };
        }, cancellationToken);
    }

    private async Task ApplySeedAsync(string tenantNId, string key, string version, IReadOnlyCollection<string> _, Func<ControlPlaneSnapshot, ControlPlaneSnapshot> apply, CancellationToken cancellationToken)
    {
        var checksum = Checksum(key);
        if (await _store.SeedAppliedAsync(tenantNId, key, version, checksum, cancellationToken)) return;
        var state = await _store.LoadAsync(tenantNId, cancellationToken);
        var next = apply(state);
        await _store.CommitAsync(next, state.Revision, new ControlPlaneCommit([], [], [new SeedLedgerEntry(tenantNId, key, version, checksum)]), cancellationToken);
    }

    private static string Checksum(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    [LoggerMessage(EventId = 1, Level = LogLevel.Warning, Message = "SystemData baseline seed failed.")]
    private static partial void LogSeedFailed(ILogger logger, Exception exception);
}
