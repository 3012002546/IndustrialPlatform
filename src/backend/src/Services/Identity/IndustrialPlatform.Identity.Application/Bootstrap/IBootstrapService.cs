namespace IndustrialPlatform.Identity.Application.Bootstrap;

/// <summary>
/// bootstrap 用例服务(§29A.4):状态查询、readiness 与紧急恢复门禁。
/// 密码生成、admin 引导与种子执行由基础设施层提供,本服务只编排非敏感状态机。
/// </summary>
public interface IBootstrapService
{
    /// <summary>查询 bootstrap 状态(§29A.5 GET /bootstrap/status):只返回状态/版本,不含 Secret。</summary>
    Task<BootstrapStatusResult> GetStatusAsync(string tenantNId, CancellationToken cancellationToken = default);

    /// <summary>计算与 SystemData readiness 兼容的初始化状态。</summary>
    Task<IdentityReadinessResult> GetReadinessAsync(string tenantNId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 紧急恢复(§29A.5 POST /bootstrap/recover):校验一次性恢复引用与审批关联,
    /// 生成新的随机临时密码并只返回一次;旧凭据全部作废。
    /// </summary>
    Task<BootstrapRecoveryResult> RecoverAdminAsync(
        string tenantNId,
        string recoveryReference,
        string approvalReference,
        CancellationToken cancellationToken = default);
}
