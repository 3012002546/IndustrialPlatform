using IndustrialPlatform.Identity.Domain.Passwords;

namespace IndustrialPlatform.Identity.Application.Bootstrap;

/// <summary>
/// bootstrap 用例实现(§29A.4):状态/readiness 计算与紧急恢复门禁。
/// 所有写路径都通过存储端口完成,明文密码只在方法返回的受保护结果中出现一次。
/// </summary>
public sealed class BootstrapService : IBootstrapService
{
    private readonly IBootstrapStore _store;
    private readonly IBootstrapCredentialStore _credentials;
    private readonly ITemporaryPasswordGenerator _generator;
    private readonly IPasswordHasher _hasher;

    public BootstrapService(
        IBootstrapStore store,
        IBootstrapCredentialStore credentials,
        ITemporaryPasswordGenerator generator,
        IPasswordHasher hasher)
    {
        _store = store;
        _credentials = credentials;
        _generator = generator;
        _hasher = hasher;
    }

    /// <inheritdoc />
    public async Task<BootstrapStatusResult> GetStatusAsync(string tenantNId, CancellationToken cancellationToken = default)
    {
        var admin = await _store.GetAdminIncludingDeletedAsync(tenantNId, BootstrapSeedCatalog.BootstrapUserNId, cancellationToken);
        var schemaVersion = await _store.GetSchemaVersionAsync(cancellationToken);
        var seeds = await _store.GetSeedLedgerAsync(tenantNId, cancellationToken);
        var delivery = await _credentials.GetLatestAsync(tenantNId, admin.UserNId, cancellationToken);

        var seedsReady = AreRequiredSeedsApplied(seeds);
        var adminHealthy = admin.Exists && !admin.IsDeleted && admin.IsActive && admin.HasSystemAdminRole;

        var state = !admin.Exists || !seedsReady || string.IsNullOrWhiteSpace(schemaVersion)
            ? BootstrapState.Pending
            : adminHealthy
                ? BootstrapState.Ready
                : BootstrapState.RecoveryRequired;

        return new BootstrapStatusResult(
            state,
            schemaVersion,
            seeds,
            admin.Exists,
            admin.MustChangePassword,
            delivery?.State == BootstrapDeliveryState.Delivered);
    }

    /// <inheritdoc />
    public async Task<IdentityReadinessResult> GetReadinessAsync(string tenantNId, CancellationToken cancellationToken = default)
    {
        var admin = await _store.GetAdminIncludingDeletedAsync(tenantNId, BootstrapSeedCatalog.BootstrapUserNId, cancellationToken);
        var schemaVersion = await _store.GetSchemaVersionAsync(cancellationToken);
        var seeds = await _store.GetSeedLedgerAsync(tenantNId, cancellationToken);

        var migrationReady = !string.IsNullOrWhiteSpace(schemaVersion);
        var requiredSeedReady = AreRequiredSeedsApplied(seeds);
        var bootstrapReady = admin.Exists && !admin.IsDeleted && admin.IsActive && admin.HasSystemAdminRole;
        var ready = migrationReady && requiredSeedReady && bootstrapReady;
        var state = !admin.Exists || !requiredSeedReady || !migrationReady
            ? BootstrapState.Pending
            : bootstrapReady
                ? BootstrapState.Ready
                : BootstrapState.RecoveryRequired;

        return new IdentityReadinessResult(
            ServiceKey: "identity",
            ModuleKey: "identity",
            LogicalDatabaseName: "identity_db",
            SchemaVersion: schemaVersion,
            BootstrapStatus: state,
            MigrationReady: migrationReady,
            RequiredSeedReady: requiredSeedReady,
            BootstrapReady: bootstrapReady,
            Ready: ready,
            Reason: ready ? null : "初始化未完成或 admin 异常,须先完成引导/恢复。",
            Seeds: seeds);
    }

    /// <inheritdoc />
    public async Task<BootstrapRecoveryResult> RecoverAdminAsync(
        string tenantNId,
        string recoveryReference,
        string approvalReference,
        CancellationToken cancellationToken = default)
    {
        // 审批关联是部署审批门禁(§29A.5);缺失直接拒绝,不泄漏细节。
        if (string.IsNullOrWhiteSpace(approvalReference))
        {
            throw new BootstrapRecoveryRejectedException();
        }

        var admin = await _store.GetAdminIncludingDeletedAsync(tenantNId, BootstrapSeedCatalog.BootstrapUserNId, cancellationToken);
        if (!admin.Exists)
        {
            throw new BootstrapRecoveryRejectedException();
        }

        var delivery = await _credentials.GetLatestAsync(tenantNId, admin.UserNId, cancellationToken);
        if (delivery is null)
        {
            throw new BootstrapRecoveryRejectedException();
        }

        // 一次性恢复引用哈希比对;引用本身绝不落库。
        if (!string.Equals(
                BootstrapHashing.HashReference(recoveryReference),
                delivery.RecoveryReferenceHash,
                StringComparison.Ordinal))
        {
            throw new BootstrapRecoveryRejectedException();
        }

        var newPassword = _generator.Generate(
            BootstrapSeedCatalog.BootstrapPasswordMinLength,
            BootstrapSeedCatalog.BootstrapLoginName,
            BootstrapSeedCatalog.BootstrapUserNId);
        var newHash = _hasher.Hash(newPassword);

        await _store.UpdateAdminPasswordAsync(tenantNId, admin.UserNId, newHash, cancellationToken);
        await _credentials.MarkRecoveredAsync(delivery.DeliveryId, cancellationToken);

        // 新交付记录:新恢复引用只在本次响应出现;领取即标记 Delivered。
        var newRecoveryReference = BootstrapHashing.NewReference();
        var newDelivery = await _credentials.CreatePendingAsync(
            tenantNId,
            admin.UserNId,
            deliveryReferenceHash: null,
            BootstrapHashing.HashReference(newRecoveryReference),
            cancellationToken);
        await _credentials.ClaimAsync(newDelivery.DeliveryId, cancellationToken);

        return new BootstrapRecoveryResult(admin.UserNId, newPassword, newRecoveryReference, newDelivery.DeliveryId);
    }

    /// <summary>三个必需种子(SystemCatalog/TenantSecurity/BootstrapAdmin)的最新版本是否均为 Applied。</summary>
    private static bool AreRequiredSeedsApplied(IReadOnlyList<SeedVersionStatus> seeds)
    {
        var required = new HashSet<string>(StringComparer.Ordinal)
        {
            BootstrapSeedCatalog.SystemCatalogSeedKey,
            BootstrapSeedCatalog.TenantSecuritySeedKey,
            BootstrapSeedCatalog.BootstrapAdminSeedKey,
        };

        foreach (var seed in seeds)
        {
            required.Remove(seed.SeedKey);
        }

        return required.Count == 0;
    }
}
