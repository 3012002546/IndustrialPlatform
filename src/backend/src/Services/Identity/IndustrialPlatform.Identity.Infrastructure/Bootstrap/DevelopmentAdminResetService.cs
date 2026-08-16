using IndustrialPlatform.Identity.Application.Bootstrap;
using IndustrialPlatform.Identity.Domain.Passwords;
using IndustrialPlatform.Identity.Infrastructure.Persistence.Seeds;

namespace IndustrialPlatform.Identity.Infrastructure.Bootstrap;

/// <summary>
/// Development 命令行专用的 admin 凭据重置服务。复用 bootstrap 存储、密码生成与
/// 凭据交付账本；不暴露 HTTP 入口，明文凭据只在本次调用结果中出现。
/// </summary>
public sealed class DevelopmentAdminResetService
{
    private readonly IBootstrapStore _store;
    private readonly IBootstrapCredentialStore _credentials;
    private readonly ITemporaryPasswordGenerator _generator;
    private readonly IPasswordHasher _hasher;

    public DevelopmentAdminResetService(
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

    /// <summary>重置健康的内置 admin，并生成一套新的单次交付凭据。</summary>
    public async Task<BootstrapSeedResult> ResetAsync(
        string tenantNId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantNId);

        var admin = await _store.GetAdminIncludingDeletedAsync(
            tenantNId,
            BootstrapSeedCatalog.BootstrapUserNId,
            cancellationToken);
        if (!admin.Exists || admin.IsDeleted || !admin.IsActive || !admin.HasSystemAdminRole)
        {
            throw new InvalidOperationException("内置 admin 不存在或状态异常，不能执行 Development 快速重置。");
        }

        var temporaryPassword = _generator.Generate(
            BootstrapSeedCatalog.BootstrapPasswordMinLength,
            BootstrapSeedCatalog.BootstrapLoginName,
            BootstrapSeedCatalog.BootstrapUserNId);
        var deliveryReference = BootstrapHashing.NewReference();
        var recoveryReference = BootstrapHashing.NewReference();

        var previousDelivery = await _credentials.GetLatestAsync(
            tenantNId,
            admin.UserNId,
            cancellationToken);
        if (previousDelivery is not null)
        {
            await _credentials.MarkRecoveredAsync(previousDelivery.DeliveryId, cancellationToken);
        }

        await _store.UpdateAdminPasswordAsync(
            tenantNId,
            admin.UserNId,
            _hasher.Hash(temporaryPassword),
            cancellationToken);

        var delivery = await _credentials.CreatePendingAsync(
            tenantNId,
            admin.UserNId,
            BootstrapHashing.HashReference(deliveryReference),
            BootstrapHashing.HashReference(recoveryReference),
            cancellationToken);
        await _credentials.ClaimAsync(delivery.DeliveryId, cancellationToken);

        return new BootstrapSeedResult(
            tenantNId,
            admin.UserNId,
            BootstrapSeedCatalog.BootstrapLoginName,
            temporaryPassword,
            deliveryReference,
            recoveryReference,
            delivery.DeliveryId);
    }
}
