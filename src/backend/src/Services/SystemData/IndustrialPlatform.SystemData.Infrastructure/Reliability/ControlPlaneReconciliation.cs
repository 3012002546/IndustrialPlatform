using IndustrialPlatform.SystemData.Application.Auditing;
using IndustrialPlatform.SystemData.Application.Administration;
using IndustrialPlatform.SystemData.Application.ControlPlane;
using IndustrialPlatform.SystemData.Application.IdentityDirectory;
using IndustrialPlatform.SystemData.Application.Reliability;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace IndustrialPlatform.SystemData.Infrastructure.Reliability;

public sealed partial class ControlPlaneReconciliationHostedService : BackgroundService
{
    private readonly IControlPlaneTenantSource _tenants;
    private readonly IControlPlaneStore _store;
    private readonly IIdentityPermissionRegistry _permissions;
    private readonly IIdentityUserDirectory _users;
    private readonly ILogger<ControlPlaneReconciliationHostedService> _logger;

    public ControlPlaneReconciliationHostedService(IControlPlaneTenantSource tenants, IControlPlaneStore store, IIdentityPermissionRegistry permissions, IIdentityUserDirectory users, ILogger<ControlPlaneReconciliationHostedService> logger)
    {
        _tenants = tenants; _store = store; _permissions = permissions; _users = users; _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await ReconcileOnceAsync(stoppingToken); }
            catch (Exception exception) when (exception is not OperationCanceledException) { LogReconciliationFailed(_logger, exception); }
            await Task.Delay(TimeSpan.FromMinutes(15), stoppingToken);
        }
    }

    public async Task ReconcileOnceAsync(CancellationToken cancellationToken)
    {
        foreach (var tenant in await _tenants.GetTenantNIdsAsync(cancellationToken))
        {
            var state = await _store.LoadAsync(tenant, cancellationToken);
            var manifests = state.Manifests.ToArray();
            var receipts = state.PermissionReceipts.ToDictionary(x => x.ModuleNId, StringComparer.OrdinalIgnoreCase);
            var catalog = state.Catalog.ToArray();
            var changed = false;
            foreach (var manifest in manifests)
            {
                var receipt = await _permissions.VerifyAsync(new PermissionManifestV1(manifest.ModuleNId, manifest.ManifestVersion, manifest.Checksum, manifest.PermissionDeclarations()), cancellationToken);
                if (receipt is null)
                {
                    SystemDataMetrics.PermissionRegistryFailures.Add(1);
                    continue;
                }
                var updated = manifest with { PermissionReceiptVersion = receipt.ManifestVersion, PermissionReceiptChecksum = receipt.Checksum, PermissionVerifiedOn = receipt.VerifiedOn };
                var index = Array.IndexOf(manifests, manifest);
                manifests[index] = updated;
                receipts[manifest.ModuleNId] = new(manifest.ModuleNId, receipt.ManifestVersion, receipt.Checksum, receipt.Verified);
                changed = true;
            }
            foreach (var entry in catalog)
            {
                if (string.IsNullOrWhiteSpace(entry.TechnicalOwnerUserNId)) continue;
                try
                {
                    var user = await _users.GetAsync(tenant, entry.TechnicalOwnerUserNId, cancellationToken);
                    if (user is null || !string.Equals(user.Status, "Active", StringComparison.OrdinalIgnoreCase)) continue;
                    if (!string.Equals(entry.TechnicalOwnerAuthVersion, user.AuthVersion, StringComparison.Ordinal) || !string.Equals(entry.TechnicalLeadDisplayNameSnapshot, user.Name, StringComparison.Ordinal))
                    {
                        entry.SetOwnerSnapshot(entry.OwnerOrganizationNId, entry.OwnerOrganizationNameSnapshot, user.UserNId, user.Name, user.AuthVersion);
                        changed = true;
                    }
                }
                catch (IdentityDirectoryUnavailableException)
                {
                    SystemDataMetrics.IdentityFailures.Add(1);
                }
            }
            if (changed)
            {
                await _store.CommitAsync(state with { Manifests = manifests, PermissionReceipts = receipts.Values.ToArray(), Catalog = catalog }, state.Revision,
                    new ControlPlaneCommit([new ControlPlaneEvent(Guid.NewGuid(), "SystemData.PermissionReconciled.v1", "v1", tenant, "{\"reconciled\":true}", DateTimeOffset.UtcNow)], [new LocalAuditEntry(tenant, "system", "permission.reconcile", "PermissionManifest", tenant, null, null, "reconciled", Guid.NewGuid().ToString("N"))]), cancellationToken);
            }
        }
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Warning, Message = "SystemData Identity permission reconciliation failed.")]
    private static partial void LogReconciliationFailed(ILogger logger, Exception exception);
}
