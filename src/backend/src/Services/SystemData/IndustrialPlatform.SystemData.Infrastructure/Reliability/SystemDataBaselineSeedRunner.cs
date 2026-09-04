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
/// SDM-013～017 SystemBaseline/TenantBaseline。只有显式配置 BaselineTenantNId 才运行，
/// 生产环境还必须显式批准自动写入；每个 seed 与版本/checksum 写入同一控制面事务的 seed ledger。
/// </summary>
public sealed partial class SystemDataBaselineSeedRunner : BackgroundService
{
    private readonly SemaphoreSlim _applyGate = new(1, 1);
    private static readonly string[] SystemDataPermissions = [
        "systemdata.organization.view", "systemdata.organization.create", "systemdata.organization.update", "systemdata.organization.move", "systemdata.organization.status",
        "systemdata.position.view", "systemdata.position.create", "systemdata.position.update", "systemdata.position.status",
        "systemdata.assignment.view", "systemdata.assignment.manage",
        "systemdata.resource.view", "systemdata.navigation.view", "systemdata.navigation.manage", "systemdata.navigation.publish", "systemdata.navigation.rollback",
        "systemdata.feature.view", "systemdata.feature.manage", "systemdata.service-catalog.view", "systemdata.service-catalog.manage", "systemdata.theme-policy.view", "systemdata.theme-policy.manage",
        "systemdata.database-orchestration.view", "systemdata.database-orchestration.register", "systemdata.database-orchestration.plan", "systemdata.database-orchestration.apply", "systemdata.database-orchestration.approve", "systemdata.database-orchestration.backup", "systemdata.database-orchestration.cancel",
        "systemdata.service-initialization.view", "systemdata.service-initialization.register", "systemdata.service-initialization.plan", "systemdata.service-initialization.apply", "systemdata.service-initialization.approve", "systemdata.service-initialization.backup", "systemdata.service-initialization.cancel",
        "platform.home.view", "platform.pda.view", "platform.mobile.view",
        "identity.user.view", "identity.user-group.view", "identity.role.view", "identity.permission.view", "identity.audit.login.view", "identity.sso.view",
    ];
    private static readonly (string NId, string Name, string RouteName, string PermissionNId)[] DefaultResources = [
        // Keep the legacy systemdata.navigation resource intact; the homepage gets its own
        // platform-owned NId so old tenants can upgrade without overwriting a custom entry.
        ("systemdata.navigation.pc-home", "首页", "pc-home", "platform.home.view"),
        ("systemdata.navigation.terminal-preview", "终端预览", "terminal-preview", "platform.pda.view"),
        ("systemdata.navigation.identity-users", "用户管理", "identity-users", "identity.user.view"),
        ("systemdata.navigation.identity-user-groups", "用户组管理", "identity-user-groups", "identity.user-group.view"),
        ("systemdata.navigation.identity-roles", "角色权限", "identity-roles", "identity.role.view"),
        ("systemdata.navigation.identity-permissions", "权限目录", "identity-permissions", "identity.permission.view"),
        ("systemdata.navigation.identity-audits", "登录审计", "identity-audits", "identity.audit.login.view"),
        ("systemdata.navigation.identity-sso-providers", "企业登录源", "sso-providers", "identity.sso.view"),
        ("systemdata.navigation.identity-sso-clients", "SSO Client", "sso-clients", "identity.sso.view"),
        ("systemdata.navigation.systemdata-organizations", "行政组织与岗位", "systemdata-organizations", "systemdata.organization.view"),
        ("systemdata.navigation.systemdata-assignments", "用户任职", "systemdata-assignments", "systemdata.assignment.view"),
        ("systemdata.navigation.systemdata-navigation", "菜单管理", "systemdata-navigation", "systemdata.navigation.view"),
        ("systemdata.navigation.systemdata-features", "功能开关", "systemdata-features", "systemdata.feature.view"),
        ("systemdata.navigation.systemdata-themes", "租户主题策略", "systemdata-themes", "systemdata.theme-policy.view"),
        ("systemdata.navigation.systemdata-services", "服务目录", "systemdata-services", "systemdata.service-catalog.view"),
        ("systemdata.navigation.systemdata-service-initialization", "服务初始化编排", "systemdata-service-initialization", "systemdata.service-initialization.view"),
    ];
    internal const string CurrentManifestSeedKey = "SDM-017";
    internal const string CurrentManifestVersion = "2";
    internal const string ResourceConsistencySeedKey = "SDM-018";
    internal const string ResourceConsistencySeedVersion = "1";
    internal const string RequiredFeatureNId = "systemdata.control-plane";
    internal const string RequiredCatalogNId = "systemdata";
    internal static string CurrentManifestChecksum => Checksum(CurrentManifestSeedKey);
    internal static IReadOnlyCollection<string> RequiredPermissionNIds => SystemDataPermissions;
    internal static IReadOnlyCollection<(string NId, string Name, string RouteName, string PermissionNId)> RequiredResourceFacts => DefaultResources;

    internal static bool IsCurrentTheme(ThemePolicy? theme) => theme is not null
        && theme.AllowedPalettes.Count > 0
        && theme.AllowedPalettes.All(value => Enum.IsDefined(value))
        && theme.AllowedPalettes.Contains(theme.DefaultPalette)
        && theme.AllowedModes.Count > 0
        && theme.AllowedModes.All(value => Enum.IsDefined(value))
        && theme.AllowedModes.Contains(theme.DefaultMode)
        && theme.AllowedPcDensities.Count > 0
        && theme.AllowedPcDensities.All(value => Enum.IsDefined(value))
        && theme.AllowedPcDensities.Contains(theme.DefaultPcDensity);

    private readonly IConfiguration _configuration;
    private readonly IControlPlaneStore _store;
    private readonly ILogger<SystemDataBaselineSeedRunner> _logger;
    private readonly IIdentityPermissionRegistry? _permissionRegistry;
    private readonly IHostEnvironment? _environment;

    public SystemDataBaselineSeedRunner(
        IConfiguration configuration,
        IControlPlaneStore store,
        ILogger<SystemDataBaselineSeedRunner> logger,
        IIdentityPermissionRegistry? permissionRegistry = null,
        IHostEnvironment? environment = null)
    {
        _configuration = configuration;
        _store = store;
        _logger = logger;
        _permissionRegistry = permissionRegistry;
        _environment = environment;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var tenant = _configuration["SystemData:BaselineTenantNId"];
        if (string.IsNullOrWhiteSpace(tenant)) return;
        if (string.Equals(_environment?.EnvironmentName, Environments.Production, StringComparison.OrdinalIgnoreCase)
            && !_configuration.GetValue<bool>("SystemData:AllowProductionBaselineSeed"))
        {
            return;
        }
        try { await ApplyAsync(tenant.Trim(), stoppingToken); }
        catch (Exception exception) when (exception is not OperationCanceledException) { LogSeedFailed(_logger, exception); }
    }

    public async Task ApplyAsync(string tenantNId, CancellationToken cancellationToken)
    {
        await _applyGate.WaitAsync(cancellationToken);
        try
        {
            var checksum = CurrentManifestChecksum;
            var permissionManifest = new PermissionManifestV1(
                "systemdata",
                CurrentManifestVersion,
                checksum,
                SystemDataPermissions.Select(x => new PermissionManifestEntry(
                    x,
                    x,
                    x.Contains('.') ? x[..x.LastIndexOf('.')].Replace('.', ':') : "systemdata",
                    null)).ToArray());
            var permissionReceipt = _permissionRegistry is null
                ? null
                : await _permissionRegistry.VerifyAsync(permissionManifest, cancellationToken);
            if (permissionReceipt is null
                || !permissionReceipt.Verified
                || !string.Equals(permissionReceipt.ModuleNId, permissionManifest.ModuleNId, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(permissionReceipt.ManifestVersion, permissionManifest.ManifestVersion, StringComparison.Ordinal)
                || !string.Equals(permissionReceipt.Checksum, permissionManifest.Checksum, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Identity permission registry did not verify the SystemData baseline manifest.");
            }

            await ApplySeedAsync(tenantNId, "SDM-013", "1", ["permissions", "resources", "navigation"], state =>
            {
                var checksum = Checksum("SDM-013");
                var manifest = new ModuleManifestState(tenantNId, "systemdata", "1", checksum, SystemDataPermissions, "1", checksum, DateTimeOffset.UtcNow)
                {
                    PermissionDeclarationItems = SystemDataPermissions.Select(x => new PermissionManifestEntry(x, x, x.Contains('.') ? x[..x.LastIndexOf('.')].Replace('.', ':') : "systemdata", null)).ToArray()
                };
                var resources = DefaultResources.Select(item => UiResource.Create(tenantNId, item.NId, "systemdata", "1", UiResourceType.Page, item.Name, item.RouteName, item.PermissionNId, [UiTerminal.Pc, UiTerminal.Pda, UiTerminal.Mobile])).ToArray();
                return state with
                {
                    Manifests = state.Manifests.Any(x => x.ModuleNId.Equals("systemdata", StringComparison.OrdinalIgnoreCase))
                        ? state.Manifests
                        : state.Manifests.Append(manifest).ToArray(),
                    PermissionReceipts = state.PermissionReceipts.Any(x => x.ModuleNId.Equals("systemdata", StringComparison.OrdinalIgnoreCase))
                        ? state.PermissionReceipts
                        : state.PermissionReceipts.Append(new PermissionReceipt("systemdata", "1", checksum, true)).ToArray(),
                    Resources = state.Resources
                        .Concat(resources.Where(resource => !state.Resources.Any(existing => existing.NId.Equals(resource.NId, StringComparison.OrdinalIgnoreCase))))
                        .ToArray()
                };
            }, cancellationToken);
            // SDM-017 是扩展后的基线声明。新 seed 不依赖旧 SDM-013 ledger，
            // 因而旧租户也会补齐新增资源并更新当前 manifest/receipt；已有自定义资源保持不变。
            await ApplySeedAsync(tenantNId, CurrentManifestSeedKey, "1", ["permissions", "resources", "navigation"], state =>
            {
                var manifest = new ModuleManifestState(
                    tenantNId,
                    "systemdata",
                    CurrentManifestVersion,
                    checksum,
                    SystemDataPermissions,
                    permissionReceipt.ManifestVersion,
                    permissionReceipt.Checksum,
                    permissionReceipt.VerifiedOn)
                {
                    PermissionDeclarationItems = permissionManifest.Permissions,
                };
                var resources = state.Resources.ToList();
                foreach (var item in DefaultResources)
                {
                    if (resources.Any(existing => existing.NId.Equals(item.NId, StringComparison.OrdinalIgnoreCase)))
                        continue;
                    resources.Add(UiResource.Create(
                        tenantNId,
                        item.NId,
                        "systemdata",
                        CurrentManifestVersion,
                        UiResourceType.Page,
                        item.Name,
                        item.RouteName,
                        item.PermissionNId,
                        [UiTerminal.Pc, UiTerminal.Pda, UiTerminal.Mobile]));
                }
                return state with
                {
                    Manifests = state.Manifests
                        .Where(existing => !existing.ModuleNId.Equals("systemdata", StringComparison.OrdinalIgnoreCase))
                        .Append(manifest)
                        .ToArray(),
                    PermissionReceipts = state.PermissionReceipts
                        .Where(existing => !existing.ModuleNId.Equals("systemdata", StringComparison.OrdinalIgnoreCase))
                        .Append(new PermissionReceipt("systemdata", permissionReceipt.ManifestVersion, permissionReceipt.Checksum, permissionReceipt.Verified))
                        .ToArray(),
                    Resources = resources,
                };
            }, cancellationToken);
            // SDM-017 updated the manifest/receipt but historical tenants may still
            // carry the v1 resource rows created by SDM-013.  Keep this as a new,
            // idempotent seed so old ledgers remain immutable and only known
            // built-in resource identities can be rebound.
            await ApplySeedAsync(tenantNId, ResourceConsistencySeedKey, ResourceConsistencySeedVersion, ["resources"], state =>
            {
                var trustedFacts = DefaultResources
                    .Append((NId: "systemdata.navigation", Name: "旧导航入口", RouteName: "/systemdata/navigation", PermissionNId: "systemdata.navigation.view"))
                    .ToArray();
                var resources = state.Resources
                    .Select(resource =>
                    {
                        var fact = trustedFacts.FirstOrDefault(item => item.NId.Equals(resource.NId, StringComparison.OrdinalIgnoreCase));
                        var isTrustedIdentity = fact.NId is not null
                            && resource.OwnerModuleNId.Equals("systemdata", StringComparison.OrdinalIgnoreCase)
                            && resource.Type == UiResourceType.Page
                            && string.Equals(resource.RouteName, fact.RouteName, StringComparison.Ordinal)
                            && string.Equals(resource.RequiredPermissionNId, fact.PermissionNId, StringComparison.Ordinal);
                        return isTrustedIdentity && resource.ManifestVersion != CurrentManifestVersion
                            ? resource.RebindManifestVersion(CurrentManifestVersion)
                            : resource;
                    })
                    .ToArray();
                return state with { Resources = resources };
            }, cancellationToken);
            await ApplySeedAsync(tenantNId, "SDM-014", "1", ["features"], state =>
            {
                var feature = FeatureDefinition.Create(tenantNId, RequiredFeatureNId, "systemdata", "SystemData control plane", true);
                return state with { Features = state.Features.Any(x => x.NId.Equals(feature.NId, StringComparison.OrdinalIgnoreCase)) ? state.Features : state.Features.Append(feature).ToArray() };
            }, cancellationToken);
            await ApplySeedAsync(tenantNId, "SDM-015", "1", ["service-catalog"], state =>
            {
                var entry = ServiceCatalogEntry.CreatePlatform(tenantNId, RequiredCatalogNId, "SystemData", "/api/v1/systemdata", "/health", [UiTerminal.Pc, UiTerminal.Pda, UiTerminal.Mobile]);
                return state with { Catalog = state.Catalog.Any(x => x.NId.Equals(entry.NId, StringComparison.OrdinalIgnoreCase)) ? state.Catalog : state.Catalog.Append(entry).ToArray() };
            }, cancellationToken);
            await ApplySeedAsync(tenantNId, "SDM-016", "1", ["theme-policy"], state =>
            {
                var theme = ThemePolicy.Create(tenantNId, [ThemePalette.IndustrialCyan, ThemePalette.TechnologyBlue, ThemePalette.NeutralGray], [ThemeMode.Light, ThemeMode.Dark, ThemeMode.System], [PcDensity.Comfortable, PcDensity.Compact], ThemePalette.IndustrialCyan, ThemeMode.Light, PcDensity.Comfortable);
                return state with { Theme = state.Theme ?? theme };
            }, cancellationToken);
        }
        finally
        {
            _applyGate.Release();
        }
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
