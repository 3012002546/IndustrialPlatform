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
/// SDM-013～019 及 PF05 Collaboration SystemBaseline/TenantBaseline。只有显式配置 BaselineTenantNId 才运行，
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
        "systemdata.file.upload", "systemdata.file.read", "systemdata.file.download", "systemdata.file.manage", "systemdata.file.delete",
        "systemdata.notification.inbox.read", "systemdata.notification.announcement.read", "systemdata.notification.announcement.manage", "systemdata.notification.announcement.publish", "systemdata.notification.system.send",
        "systemdata.audit.write", "systemdata.audit.read", "systemdata.audit.export", "systemdata.audit.retention.manage",
        "platform.home.view", "platform.pda.view", "platform.mobile.view",
        "identity.user.view", "identity.user-group.view", "identity.role.view", "identity.permission.view", "identity.audit.login.view", "identity.sso.view",
    ];
    private static readonly string[] ReferenceDataPermissions = [
        "referencedata.dictionary.view", "referencedata.dictionary.create", "referencedata.dictionary.update", "referencedata.dictionary.publish", "referencedata.dictionary.disable",
        "referencedata.parameter.view", "referencedata.parameter.create", "referencedata.parameter.update", "referencedata.parameter.disable", "referencedata.parameter.read-secret-reference",
        "referencedata.dynamic-property.view", "referencedata.dynamic-property.create", "referencedata.dynamic-property.update", "referencedata.dynamic-property.publish", "referencedata.dynamic-property.disable",
        "referencedata.unit-of-measure.view", "referencedata.unit-of-measure.create", "referencedata.unit-of-measure.update", "referencedata.unit-of-measure.publish", "referencedata.unit-of-measure.disable",
        "referencedata.metadata.view", "referencedata.metadata.create", "referencedata.metadata.update", "referencedata.metadata.publish", "referencedata.metadata.disable",
        "referencedata.coding-rule.view", "referencedata.coding-rule.create", "referencedata.coding-rule.update", "referencedata.coding-rule.publish", "referencedata.coding-rule.disable", "referencedata.coding-rule.preview", "referencedata.coding-rule.generate",
        "referencedata.state-machine.view", "referencedata.state-machine.create", "referencedata.state-machine.update", "referencedata.state-machine.publish", "referencedata.state-machine.disable",
        "referencedata.platform.manage",
    ];
    private static readonly string[] CollaborationPermissions = [
        "collaboration.messaging.read", "collaboration.messaging.conversation.start", "collaboration.messaging.write",
        "collaboration.messaging.read-cursor.update", "collaboration.messaging.conversation.hide", "collaboration.messaging.conversation.restore",
        "collaboration.messaging.retract", "collaboration.messaging.attachment.send", "collaboration.messaging.attachment.download",
        "collaboration.compliance.read", "collaboration.compliance.view", "collaboration.compliance.read-original", "collaboration.compliance.dispose",
        "collaboration.compliance.export.request", "collaboration.compliance.export.download", "collaboration.compliance.export.approve",
        "collaboration.compliance.legal-hold.create", "collaboration.compliance.legal-hold.review", "collaboration.compliance.legal-hold.release",
        "collaboration.compliance.legal-hold.release.approve", "collaboration.compliance.retention.manage", "collaboration.compliance.retention.update",
        "collaboration.presence.connect", "collaboration.presence.read", "collaboration.presence.write",
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
        ("systemdata.navigation.systemdata-files", "文件管理", "systemdata-files", "systemdata.file.read"),
        ("systemdata.navigation.systemdata-notifications", "通知公告", "systemdata-notifications", "systemdata.notification.announcement.read"),
        ("systemdata.navigation.systemdata-audits", "审计查询", "systemdata-audits", "systemdata.audit.read"),
    ];
    private static readonly (string NId, string Name, string RouteName, string PermissionNId)[] ReferenceDataResources = [
        ("referencedata.navigation.dictionaries", "字典管理", "reference-data-dictionaries", "referencedata.dictionary.view"),
        ("referencedata.navigation.parameters", "基础配置", "reference-data-parameters", "referencedata.parameter.view"),
        ("referencedata.navigation.dynamic-properties", "动态属性", "reference-data-dynamic-properties", "referencedata.dynamic-property.view"),
        ("referencedata.navigation.units-of-measure", "计量单位", "reference-data-units-of-measure", "referencedata.unit-of-measure.view"),
        ("referencedata.navigation.metadata", "元数据定义", "reference-data-metadata", "referencedata.metadata.view"),
        ("referencedata.navigation.coding-rules", "编码规则", "reference-data-coding-rules", "referencedata.coding-rule.view"),
        ("referencedata.navigation.state-machines", "状态机定义", "reference-data-state-machines", "referencedata.state-machine.view"),
    ];
    private static readonly (string NId, string Name, string RouteName, string PermissionNId, UiTerminal Terminal)[] CollaborationResources = [
        ("collaboration.page.pc-chat", "聊天", "collaboration-chat", "collaboration.messaging.read", UiTerminal.Pc),
        ("collaboration.page.pda-chat", "聊天", "pda-collaboration-chat", "collaboration.messaging.read", UiTerminal.Pda),
        ("collaboration.page.mobile-chat", "聊天", "mobile-collaboration-chat", "collaboration.messaging.read", UiTerminal.Mobile),
        ("collaboration.page.pc-compliance-search", "受控查看", "collaboration-compliance-search", "collaboration.compliance.read", UiTerminal.Pc),
        ("collaboration.page.pc-legal-holds", "保全案件", "collaboration-legal-holds", "collaboration.compliance.read", UiTerminal.Pc),
        ("collaboration.page.pc-exports", "导出记录", "collaboration-exports", "collaboration.compliance.read", UiTerminal.Pc),
        ("collaboration.page.pc-retention", "保留策略", "collaboration-retention", "collaboration.compliance.retention.manage", UiTerminal.Pc),
    ];
    private static readonly (string NId, string? ParentNId, string RouteName, string ResourceNId, string PermissionNId, UiTerminal Terminal, int DisplayOrder)[] CollaborationNavigation = [
        ("collaboration.nav.pc-chat", "navigation.group.workspace", "collaboration-chat", "collaboration.page.pc-chat", "collaboration.messaging.read", UiTerminal.Pc, 2),
        ("collaboration.nav.pda-chat", null, "pda-collaboration-chat", "collaboration.page.pda-chat", "collaboration.messaging.read", UiTerminal.Pda, 50),
        ("collaboration.nav.mobile-chat", null, "mobile-collaboration-chat", "collaboration.page.mobile-chat", "collaboration.messaging.read", UiTerminal.Mobile, 50),
        ("collaboration.nav.pc-compliance-search", "navigation.group.collaboration", "collaboration-compliance-search", "collaboration.page.pc-compliance-search", "collaboration.compliance.read", UiTerminal.Pc, 0),
        ("collaboration.nav.pc-legal-holds", "navigation.group.collaboration", "collaboration-legal-holds", "collaboration.page.pc-legal-holds", "collaboration.compliance.read", UiTerminal.Pc, 1),
        ("collaboration.nav.pc-exports", "navigation.group.collaboration", "collaboration-exports", "collaboration.page.pc-exports", "collaboration.compliance.read", UiTerminal.Pc, 2),
        ("collaboration.nav.pc-retention", "navigation.group.collaboration", "collaboration-retention", "collaboration.page.pc-retention", "collaboration.compliance.retention.manage", UiTerminal.Pc, 3),
    ];
    internal const string CurrentManifestSeedKey = "SDM-017";
    internal const string CurrentManifestVersion = "3";
    internal const string ResourceConsistencySeedKey = "SDM-018";
    internal const string ResourceConsistencySeedVersion = "2";
    internal const string ReferenceDataManifestSeedKey = "SDM-019";
    internal const string ReferenceDataManifestVersion = "3";
    internal const string CollaborationManifestSeedKey = "collaboration.baseline";
    internal const string CollaborationNavigationSeedKey = "collaboration.navigation";
    // Compliance pages extend the previously applied chat-only seeds. Keep the
    // 1.0.0 ledger immutable and apply the additive declarations under a new version.
    internal const string CollaborationManifestVersion = "1.1.0";
    internal const string RequiredFeatureNId = "systemdata.control-plane";
    internal const string RequiredCatalogNId = "systemdata";
    internal static string CurrentManifestChecksum => Checksum(CurrentManifestSeedKey);
    internal static IReadOnlyCollection<string> RequiredPermissionNIds => SystemDataPermissions;
    internal static IReadOnlyCollection<(string NId, string Name, string RouteName, string PermissionNId)> RequiredResourceFacts => DefaultResources;
    internal static string ReferenceDataManifestChecksum => Checksum(ReferenceDataManifestSeedKey);
    internal static IReadOnlyCollection<string> RequiredReferenceDataPermissionNIds => ReferenceDataPermissions;
    internal static IReadOnlyCollection<(string NId, string Name, string RouteName, string PermissionNId)> RequiredReferenceDataResourceFacts => ReferenceDataResources;
    internal static string CollaborationManifestChecksum => Checksum(CollaborationManifestSeedKey);
    internal static string CollaborationNavigationChecksum => Checksum(CollaborationNavigationSeedKey);
    internal static IReadOnlyCollection<string> RequiredCollaborationPermissionNIds => CollaborationPermissions;
    internal static IReadOnlyCollection<(string NId, string Name, string RouteName, string PermissionNId, UiTerminal Terminal)> RequiredCollaborationResourceFacts => CollaborationResources;
    internal static IReadOnlyCollection<(string NId, string? ParentNId, string RouteName, string ResourceNId, string PermissionNId, UiTerminal Terminal, int DisplayOrder)> RequiredCollaborationNavigationFacts => CollaborationNavigation;

    internal static bool IsReferenceDataBootstrapReady(ControlPlaneSnapshot controlPlane)
    {
        var manifest = controlPlane.Manifests.SingleOrDefault(item => item.ModuleNId.Equals("referencedata", StringComparison.OrdinalIgnoreCase));
        var receipt = controlPlane.PermissionReceipts.SingleOrDefault(item => item.ModuleNId.Equals("referencedata", StringComparison.OrdinalIgnoreCase));
        var requiredPermissions = ReferenceDataPermissions.Order(StringComparer.OrdinalIgnoreCase).ToArray();
        var manifestPermissions = manifest?.PermissionNIds.Order(StringComparer.OrdinalIgnoreCase).ToArray();
        var requiredResourcesReady = ReferenceDataResources.All(required =>
            controlPlane.Resources.Any(resource =>
                resource.NId.Equals(required.NId, StringComparison.OrdinalIgnoreCase)
                && resource.OwnerModuleNId.Equals("referencedata", StringComparison.OrdinalIgnoreCase)
                && resource.Type == UiResourceType.Page
                && resource.ManifestVersion == ReferenceDataManifestVersion
                && resource.RouteName == required.RouteName
                && resource.RequiredPermissionNId == required.PermissionNId
                && resource.Status == UiResourceStatus.Active));
        return manifest is not null
            && manifest.ManifestVersion == ReferenceDataManifestVersion
            && manifest.Checksum.Equals(ReferenceDataManifestChecksum, StringComparison.OrdinalIgnoreCase)
            && manifestPermissions is not null
            && manifestPermissions.SequenceEqual(requiredPermissions, StringComparer.OrdinalIgnoreCase)
            && manifest.PermissionReceiptVersion == ReferenceDataManifestVersion
            && string.Equals(manifest.PermissionReceiptChecksum, ReferenceDataManifestChecksum, StringComparison.OrdinalIgnoreCase)
            && receipt is { Verified: true }
            && receipt.ManifestVersion == ReferenceDataManifestVersion
            && receipt.Checksum.Equals(ReferenceDataManifestChecksum, StringComparison.OrdinalIgnoreCase)
            && requiredResourcesReady;
    }

    internal static bool IsCollaborationBootstrapReady(ControlPlaneSnapshot controlPlane)
    {
        var manifest = controlPlane.Manifests.SingleOrDefault(item => item.ModuleNId.Equals("collaboration", StringComparison.OrdinalIgnoreCase));
        var receipt = controlPlane.PermissionReceipts.SingleOrDefault(item => item.ModuleNId.Equals("collaboration", StringComparison.OrdinalIgnoreCase));
        var requiredPermissions = CollaborationPermissions.Order(StringComparer.OrdinalIgnoreCase).ToArray();
        var manifestPermissions = manifest?.PermissionNIds.Order(StringComparer.OrdinalIgnoreCase).ToArray();
        var requiredResourcesReady = CollaborationResources.All(required =>
            controlPlane.Resources.Any(resource =>
                resource.NId.Equals(required.NId, StringComparison.OrdinalIgnoreCase)
                && resource.OwnerModuleNId.Equals("collaboration", StringComparison.OrdinalIgnoreCase)
                && resource.Type == UiResourceType.Page
                && resource.ManifestVersion == CollaborationManifestVersion
                && resource.RouteName == required.RouteName
                && resource.RequiredPermissionNId == required.PermissionNId
                && resource.SupportedTerminals.Count == 1
                && resource.SupportedTerminals.Contains(required.Terminal)
                && resource.Status == UiResourceStatus.Active));
        var activeSnapshot = controlPlane.ActiveSnapshotRevision is { } activeRevision
            ? controlPlane.Snapshots.SingleOrDefault(item => item.Revision == activeRevision)
            : null;
        var navigationReady = CollaborationNavigation.All(required =>
            controlPlane.DraftNodes.Any(node => node.NId.Equals(required.NId, StringComparison.OrdinalIgnoreCase))
            && (activeSnapshot is null || activeSnapshot.Nodes.Any(node => node.NodeNId.Equals(required.NId, StringComparison.OrdinalIgnoreCase))));
        return manifest is not null
            && manifest.ManifestVersion == CollaborationManifestVersion
            && manifest.Checksum.Equals(CollaborationManifestChecksum, StringComparison.OrdinalIgnoreCase)
            && manifestPermissions is not null
            && manifestPermissions.SequenceEqual(requiredPermissions, StringComparer.OrdinalIgnoreCase)
            && manifest.PermissionReceiptVersion == CollaborationManifestVersion
            && string.Equals(manifest.PermissionReceiptChecksum, CollaborationManifestChecksum, StringComparison.OrdinalIgnoreCase)
            && receipt is { Verified: true }
            && receipt.ManifestVersion == CollaborationManifestVersion
            && receipt.Checksum.Equals(CollaborationManifestChecksum, StringComparison.OrdinalIgnoreCase)
            && requiredResourcesReady
            && navigationReady;
    }

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

            var referenceDataChecksum = ReferenceDataManifestChecksum;
            var referenceDataPermissionManifest = new PermissionManifestV1(
                "referencedata",
                ReferenceDataManifestVersion,
                referenceDataChecksum,
                ReferenceDataPermissions.Select(x => new PermissionManifestEntry(
                    x,
                    x,
                    x.EndsWith(".view", StringComparison.OrdinalIgnoreCase) ? "Page" : "Action",
                    null)).ToArray());
            var referenceDataPermissionReceipt = _permissionRegistry is null
                ? null
                : await _permissionRegistry.VerifyAsync(referenceDataPermissionManifest, cancellationToken);
            if (referenceDataPermissionReceipt is null
                || !referenceDataPermissionReceipt.Verified
                || !string.Equals(referenceDataPermissionReceipt.ModuleNId, referenceDataPermissionManifest.ModuleNId, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(referenceDataPermissionReceipt.ManifestVersion, referenceDataPermissionManifest.ManifestVersion, StringComparison.Ordinal)
                || !string.Equals(referenceDataPermissionReceipt.Checksum, referenceDataPermissionManifest.Checksum, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Identity permission registry did not verify the ReferenceData baseline manifest.");
            }

            var collaborationChecksum = CollaborationManifestChecksum;
            var collaborationPermissionManifest = new PermissionManifestV1(
                "collaboration",
                CollaborationManifestVersion,
                collaborationChecksum,
                CollaborationPermissions.Select(x => new PermissionManifestEntry(
                    x,
                    x,
                    x.EndsWith(".read", StringComparison.OrdinalIgnoreCase) ? "Page" : "Action",
                    null)).ToArray());
            var collaborationPermissionReceipt = _permissionRegistry is null
                ? null
                : await _permissionRegistry.VerifyAsync(collaborationPermissionManifest, cancellationToken);
            if (collaborationPermissionReceipt is null
                || !collaborationPermissionReceipt.Verified
                || !string.Equals(collaborationPermissionReceipt.ModuleNId, collaborationPermissionManifest.ModuleNId, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(collaborationPermissionReceipt.ManifestVersion, collaborationPermissionManifest.ManifestVersion, StringComparison.Ordinal)
                || !string.Equals(collaborationPermissionReceipt.Checksum, collaborationPermissionManifest.Checksum, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Identity permission registry did not verify the Collaboration baseline manifest.");
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
            await ApplySeedAsync(tenantNId, CurrentManifestSeedKey, CurrentManifestVersion, ["permissions", "resources", "navigation"], state =>
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
            await ApplySeedAsync(tenantNId, ReferenceDataManifestSeedKey, ReferenceDataManifestVersion, ["permissions", "resources"], state =>
            {
                var manifest = new ModuleManifestState(
                    tenantNId,
                    "referencedata",
                    ReferenceDataManifestVersion,
                    referenceDataChecksum,
                    ReferenceDataPermissions,
                    referenceDataPermissionReceipt.ManifestVersion,
                    referenceDataPermissionReceipt.Checksum,
                    referenceDataPermissionReceipt.VerifiedOn)
                {
                    PermissionDeclarationItems = referenceDataPermissionManifest.Permissions,
                    ResourceDeclarations = ReferenceDataResources
                        .Select(item => new ModuleResourceDeclaration(
                            item.NId,
                            ReferenceDataManifestVersion,
                            nameof(UiResourceType.Page),
                            item.Name,
                            item.RouteName,
                            item.PermissionNId,
                            [UiTerminal.Pc, UiTerminal.Pda, UiTerminal.Mobile]))
                        .ToArray(),
                };
                var resources = state.Resources.ToList();
                foreach (var item in ReferenceDataResources)
                {
                    var resource = UiResource.Create(
                        tenantNId,
                        item.NId,
                        "referencedata",
                        ReferenceDataManifestVersion,
                        UiResourceType.Page,
                        item.Name,
                        item.RouteName,
                        item.PermissionNId,
                        [UiTerminal.Pc, UiTerminal.Pda, UiTerminal.Mobile]);
                    var current = resources.FirstOrDefault(existing => existing.NId.Equals(resource.NId, StringComparison.OrdinalIgnoreCase));
                    if (current is null)
                    {
                        resources.Add(resource);
                        continue;
                    }

                    if (!current.OwnerModuleNId.Equals("referencedata", StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException($"ReferenceData resource identity collision: {resource.NId}.");
                    var shouldRenameDisplay = IsTrustedReferenceDataDisplayRename(current, resource);
                    if (current.Type != resource.Type
                        || !string.Equals(current.RouteName, resource.RouteName, StringComparison.Ordinal)
                        || !string.Equals(current.RequiredPermissionNId, resource.RequiredPermissionNId, StringComparison.OrdinalIgnoreCase)
                        || !current.SupportedTerminals.ToHashSet().SetEquals(resource.SupportedTerminals))
                        throw new InvalidOperationException($"ReferenceData resource declaration conflict: {resource.NId}.");
                    if (current.ManifestVersion != ReferenceDataManifestVersion || shouldRenameDisplay)
                        resources[resources.IndexOf(current)] = current.RebindManifestVersion(
                            ReferenceDataManifestVersion,
                            shouldRenameDisplay ? resource.Name : current.Name);
                }

                return state with
                {
                    Manifests = state.Manifests
                        .Where(existing => !existing.ModuleNId.Equals("referencedata", StringComparison.OrdinalIgnoreCase))
                        .Append(manifest)
                        .ToArray(),
                    PermissionReceipts = state.PermissionReceipts
                        .Where(existing => !existing.ModuleNId.Equals("referencedata", StringComparison.OrdinalIgnoreCase))
                        .Append(new PermissionReceipt("referencedata", referenceDataPermissionReceipt.ManifestVersion, referenceDataPermissionReceipt.Checksum, referenceDataPermissionReceipt.Verified))
                        .ToArray(),
                    Resources = resources,
                };
            }, cancellationToken);
            await ApplySeedAsync(tenantNId, CollaborationManifestSeedKey, CollaborationManifestVersion, ["permissions", "resources"], state =>
            {
                var manifest = new ModuleManifestState(
                    tenantNId,
                    "collaboration",
                    CollaborationManifestVersion,
                    collaborationChecksum,
                    CollaborationPermissions,
                    collaborationPermissionReceipt.ManifestVersion,
                    collaborationPermissionReceipt.Checksum,
                    collaborationPermissionReceipt.VerifiedOn)
                {
                    PermissionDeclarationItems = collaborationPermissionManifest.Permissions,
                    ResourceDeclarations = CollaborationResources
                        .Select(item => new ModuleResourceDeclaration(
                            item.NId,
                            CollaborationManifestVersion,
                            nameof(UiResourceType.Page),
                            item.Name,
                            item.RouteName,
                            item.PermissionNId,
                            [item.Terminal]))
                        .ToArray(),
                };
                var resources = state.Resources.ToList();
                foreach (var item in CollaborationResources)
                {
                    var resource = UiResource.Create(
                        tenantNId,
                        item.NId,
                        "collaboration",
                        CollaborationManifestVersion,
                        UiResourceType.Page,
                        item.Name,
                        item.RouteName,
                        item.PermissionNId,
                        [item.Terminal]);
                    var current = resources.FirstOrDefault(existing => existing.NId.Equals(resource.NId, StringComparison.OrdinalIgnoreCase));
                    if (current is null)
                    {
                        resources.Add(resource);
                        continue;
                    }

                    if (!current.OwnerModuleNId.Equals("collaboration", StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException($"Collaboration resource identity collision: {resource.NId}.");
                    if (current.Type != resource.Type
                        || !string.Equals(current.Name, resource.Name, StringComparison.Ordinal)
                        || !string.Equals(current.RouteName, resource.RouteName, StringComparison.Ordinal)
                        || !string.Equals(current.RequiredPermissionNId, resource.RequiredPermissionNId, StringComparison.OrdinalIgnoreCase)
                        || !current.SupportedTerminals.ToHashSet().SetEquals(resource.SupportedTerminals))
                        throw new InvalidOperationException($"Collaboration resource declaration conflict: {resource.NId}.");
                    if (current.ManifestVersion != CollaborationManifestVersion)
                        resources[resources.IndexOf(current)] = current.RebindManifestVersion(CollaborationManifestVersion);
                }

                return state with
                {
                    Manifests = state.Manifests
                        .Where(existing => !existing.ModuleNId.Equals("collaboration", StringComparison.OrdinalIgnoreCase))
                        .Append(manifest)
                        .ToArray(),
                    PermissionReceipts = state.PermissionReceipts
                        .Where(existing => !existing.ModuleNId.Equals("collaboration", StringComparison.OrdinalIgnoreCase))
                        .Append(new PermissionReceipt("collaboration", collaborationPermissionReceipt.ManifestVersion, collaborationPermissionReceipt.Checksum, collaborationPermissionReceipt.Verified))
                        .ToArray(),
                    Resources = resources,
                };
            }, cancellationToken);
            await ApplySeedAsync(tenantNId, CollaborationNavigationSeedKey, CollaborationManifestVersion, ["navigation"],
                state => ApplyCollaborationNavigationSeed(state, tenantNId), cancellationToken);
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

    private static ControlPlaneSnapshot ApplyCollaborationNavigationSeed(ControlPlaneSnapshot state, string tenantNId)
    {
        var draftNodes = state.DraftNodes.ToList();
        foreach (var expected in CollaborationNavigation)
        {
            EnsureCollaborationDraftParent(draftNodes, tenantNId, expected.ParentNId);
            var current = draftNodes.FirstOrDefault(node => node.NId.Equals(expected.NId, StringComparison.OrdinalIgnoreCase));
            if (current is null)
            {
                var node = NavigationNode.CreateLink(
                    tenantNId,
                    expected.NId,
                    CollaborationResources.Single(resource => resource.NId == expected.ResourceNId).Name,
                    expected.ParentNId,
                    "PLATFORM_NAVIGATION",
                    expected.ResourceNId,
                    null,
                    [expected.Terminal]);
                node.SetDisplayOrder(expected.DisplayOrder);
                draftNodes.Add(node);
                continue;
            }

            // An inactive node is an explicit tenant choice (for example a retired
            // menu entry); preserve it and do not resurrect it during an upgrade.
            if (current.Status == NavigationNodeStatus.Inactive)
                continue;
            EnsureCollaborationNavigationMatches(current, expected);
        }

        var activeRevision = state.ActiveSnapshotRevision;
        if (activeRevision is not { } revision)
            return state with { Revision = state.Revision + 1, DraftNodes = draftNodes };

        var activeSnapshot = state.Snapshots.FirstOrDefault(snapshot => snapshot.Revision == revision)
            ?? throw new InvalidOperationException("Collaboration navigation seed cannot update a missing active snapshot.");
        var publishedNodes = activeSnapshot.Nodes.ToList();
        var changed = false;
        foreach (var expected in CollaborationNavigation)
        {
            var draftNode = draftNodes.First(node => node.NId.Equals(expected.NId, StringComparison.OrdinalIgnoreCase));
            if (draftNode.Status == NavigationNodeStatus.Inactive)
                continue;

            EnsurePublishedParent(expected.ParentNId, draftNodes, publishedNodes, ref changed);
            var existing = publishedNodes.FirstOrDefault(node => node.NodeNId.Equals(expected.NId, StringComparison.OrdinalIgnoreCase));
            if (existing is not null)
            {
                EnsureCollaborationPublishedNavigationMatches(existing, expected);
                continue;
            }

            var resource = state.Resources.FirstOrDefault(resource => resource.NId.Equals(expected.ResourceNId, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException($"Collaboration navigation resource is missing: {expected.ResourceNId}.");
            publishedNodes.Add(new PublishedNavigationNode(
                expected.NId,
                expected.ParentNId,
                NavigationNodeKind.Link,
                resource.Name,
                null,
                resource.NId,
                resource.RouteName,
                resource.RequiredPermissionNId,
                null,
                expected.DisplayOrder,
                [expected.Terminal]));
            changed = true;
        }

        if (!changed)
            return state with { Revision = state.Revision + 1, DraftNodes = draftNodes };

        var nextRevision = state.Revision + 1;
        var nextSnapshot = new PublishedNavigationSnapshot(
            nextRevision,
            DateTimeOffset.UtcNow,
            publishedNodes,
            NavigationSnapshotChecksum.Compute(publishedNodes));
        return state with
        {
            Revision = nextRevision,
            DraftNodes = draftNodes,
            Snapshots = state.Snapshots.Append(nextSnapshot).ToArray(),
            ActiveSnapshotRevision = nextRevision,
            PreviousSnapshotRevision = state.ActiveSnapshotRevision,
        };
    }

    private static void EnsureCollaborationDraftParent(List<NavigationNode> draftNodes, string tenantNId, string? parentNId)
    {
        if (parentNId is null)
            return;

        var parent = draftNodes.FirstOrDefault(node => node.NId.Equals(parentNId, StringComparison.OrdinalIgnoreCase));
        if (parent is not null)
        {
            if (parent.Status == NavigationNodeStatus.Inactive || parent.Kind != NavigationNodeKind.Group)
                throw new InvalidOperationException($"Collaboration navigation parent is not an active group: {parentNId}.");
            return;
        }

        var isCollaboration = parentNId.Equals("navigation.group.collaboration", StringComparison.OrdinalIgnoreCase);
        var group = NavigationNode.CreateGroup(tenantNId, parentNId, isCollaboration ? "协作" : "工作台", null, "PLATFORM_NAVIGATION", isCollaboration ? "chat-dot-round" : "house");
        group.SetDisplayOrder(isCollaboration ? 2 : 0);
        draftNodes.Add(group);
    }

    private static void EnsurePublishedParent(
        string? parentNId,
        IReadOnlyCollection<NavigationNode> draftNodes,
        List<PublishedNavigationNode> publishedNodes,
        ref bool changed)
    {
        if (parentNId is null || publishedNodes.Any(node => node.NodeNId.Equals(parentNId, StringComparison.OrdinalIgnoreCase)))
            return;

        var parent = draftNodes.FirstOrDefault(node => node.NId.Equals(parentNId, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Collaboration navigation parent is missing: {parentNId}.");
        if (parent.Status == NavigationNodeStatus.Inactive || parent.Kind != NavigationNodeKind.Group)
            throw new InvalidOperationException($"Collaboration navigation parent is not an active group: {parentNId}.");
        publishedNodes.Add(new PublishedNavigationNode(
            parent.NId,
            parent.ParentNodeNId,
            NavigationNodeKind.Group,
            parent.Label,
            parent.IconKey,
            null,
            null,
            null,
            null,
            parent.DisplayOrder,
            parent.VisibleTerminals));
        changed = true;
    }

    private static void EnsureCollaborationNavigationMatches(
        NavigationNode node,
        (string NId, string? ParentNId, string RouteName, string ResourceNId, string PermissionNId, UiTerminal Terminal, int DisplayOrder) expected)
    {
        if (node.Kind != NavigationNodeKind.Link
            || !string.Equals(node.ParentNodeNId, expected.ParentNId, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(node.ResourceNId, expected.ResourceNId, StringComparison.OrdinalIgnoreCase)
            || node.DisplayOrder != expected.DisplayOrder
            || !node.VisibleTerminals.ToHashSet().SetEquals([expected.Terminal]))
            throw new InvalidOperationException($"Collaboration navigation declaration conflict: {expected.NId}.");
    }

    private static void EnsureCollaborationPublishedNavigationMatches(
        PublishedNavigationNode node,
        (string NId, string? ParentNId, string RouteName, string ResourceNId, string PermissionNId, UiTerminal Terminal, int DisplayOrder) expected)
    {
        if (node.Kind != NavigationNodeKind.Link
            || !string.Equals(node.ParentNodeNId, expected.ParentNId, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(node.RouteName, expected.RouteName, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(node.RequiredPermissionNId, expected.PermissionNId, StringComparison.OrdinalIgnoreCase)
            || node.DisplayOrder != expected.DisplayOrder
            || !node.VisibleTerminals.ToHashSet().SetEquals([expected.Terminal]))
            throw new InvalidOperationException($"Collaboration published navigation declaration conflict: {expected.NId}.");
    }

    private static bool IsTrustedReferenceDataDisplayRename(UiResource current, UiResource expected) =>
        (current.NId.Equals("referencedata.navigation.metadata", StringComparison.OrdinalIgnoreCase)
            && string.Equals(current.Name, "元数据 Schema", StringComparison.Ordinal)
            && string.Equals(expected.Name, "元数据定义", StringComparison.Ordinal))
        || (current.NId.Equals("referencedata.navigation.parameters", StringComparison.OrdinalIgnoreCase)
            && string.Equals(current.Name, "参数管理", StringComparison.Ordinal)
            && string.Equals(expected.Name, "基础配置", StringComparison.Ordinal));

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
