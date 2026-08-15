using IndustrialPlatform.Identity.Application.Bootstrap;

namespace IndustrialPlatform.Identity.Application.Bootstrap;

/// <summary>
/// bootstrap admin 与初始化状态存储端口(§29A.4):admin 聚合装载/改密,
/// Schema 版本、种子账本与系统角色完整性读取。实现由基础设施层提供。
/// </summary>
public interface IBootstrapStore
{
    /// <summary>按稳定 UserNId 读取 admin(含软删除墓碑);不存在返回快照 Exists=false。</summary>
    Task<BootstrapAdminSnapshot> GetAdminIncludingDeletedAsync(
        string tenantNId,
        string userNId,
        CancellationToken cancellationToken = default);

    /// <summary>紧急恢复改密:推进密码哈希与安全版本(撤销会话),按当前版本乐观更新。</summary>
    Task UpdateAdminPasswordAsync(
        string tenantNId,
        string userNId,
        string newPasswordHash,
        CancellationToken cancellationToken = default);

    /// <summary>最近已应用 Schema 迁移版本(无迁移返回空串)。</summary>
    Task<string> GetSchemaVersionAsync(CancellationToken cancellationToken = default);

    /// <summary>租户内全部种子账本记录(最新版本优先)。</summary>
    Task<IReadOnlyList<SeedVersionStatus>> GetSeedLedgerAsync(
        string tenantNId,
        CancellationToken cancellationToken = default);

    /// <summary>系统角色(SYSTEM_ADMIN)是否已存在且拥有目录全部权限。</summary>
    Task<bool> IsSystemAdminRoleCompleteAsync(
        string tenantNId,
        CancellationToken cancellationToken = default);
}
