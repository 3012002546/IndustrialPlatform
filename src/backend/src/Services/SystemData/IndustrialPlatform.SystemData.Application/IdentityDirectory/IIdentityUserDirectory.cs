using IndustrialPlatform.SystemData.Contracts.Administration;

namespace IndustrialPlatform.SystemData.Application.IdentityDirectory;

/// <summary>
/// 身份用户目录应用端口(TASK-SD-006,05 方案 §9.7/§11.2)。
/// SystemData 不直接依赖 Identity.Contracts 或直连不稳定真实 Identity endpoint;
/// 本端口由宿主层(或测试替身)从真实 Identity 数据源适配实现。
/// 目录不可用时新写入 fail-closed(抛
/// <see cref="Administration.IdentityDirectoryUnavailableException"/> → 503 SD_IDENTITY_DIRECTORY_UNAVAILABLE);
/// 读取使用既有显示快照,不追溯改写历史。
/// </summary>
public interface IIdentityUserDirectory
{
    /// <summary>
    /// 按 (租户, 用户) 查询目录条目;用户不存在返回 <c>null</c>。
    /// 实现不可用/超时/鉴权失败时必须抛
    /// <see cref="Administration.IdentityDirectoryUnavailableException"/>,调用方 fail-closed。
    /// </summary>
    Task<IdentityUserDirectoryEntryV1?> GetAsync(string tenantNId, string userNId, CancellationToken cancellationToken);
}
