using IndustrialPlatform.Identity.Contracts.Management;

namespace IndustrialPlatform.Identity.Application.Management;

/// <summary>
/// 权限目录查询用例(§16.3)。权限为平台级数据,不按租户隔离,只读展示。
/// </summary>
public interface IPermissionQueryService
{
    /// <summary>按 ParentPermissionNId 组织权限目录树,节点按规范化标识升序。</summary>
    Task<IReadOnlyList<PermissionTreeNode>> GetTreeAsync(CancellationToken cancellationToken);
}
