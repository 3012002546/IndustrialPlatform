using IndustrialPlatform.Identity.Application.Bootstrap;

namespace IndustrialPlatform.Identity.Application.Bootstrap;

/// <summary>
/// bootstrap 凭据交付存储端口(§29A.4):只保存引用哈希与状态机,明文密码绝不入库。
/// 实现由基础设施层提供。
/// </summary>
public interface IBootstrapCredentialStore
{
    /// <summary>创建 Pending 交付记录,返回应用层视图。交付引用哈希可为空(仅恢复路径)。</summary>
    Task<BootstrapCredentialDelivery> CreatePendingAsync(
        string tenantNId,
        string userNId,
        string? deliveryReferenceHash,
        string recoveryReferenceHash,
        CancellationToken cancellationToken = default);

    /// <summary>读取该用户最新交付记录;不存在返回 <c>null</c>。</summary>
    Task<BootstrapCredentialDelivery?> GetLatestAsync(
        string tenantNId,
        string userNId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 一次性领取:Pending → Delivered。已 Delivered/Recovered 时抛
    /// <see cref="BootstrapCredentialAlreadyRetrievedException"/>(§29A.4 只能领取一次)。
    /// </summary>
    Task<BootstrapCredentialDelivery> ClaimAsync(Guid deliveryId, CancellationToken cancellationToken = default);

    /// <summary>紧急恢复后作废旧记录:置为 Recovered 并记录时间。</summary>
    Task<BootstrapCredentialDelivery> MarkRecoveredAsync(Guid deliveryId, CancellationToken cancellationToken = default);
}
