using IndustrialPlatform.Identity.Contracts.Management;
using IndustrialPlatform.Identity.Domain.Permissions;

namespace IndustrialPlatform.Identity.Application.Management;

/// <summary>
/// 权限目录查询实现(§16.3)。读取全部未删除权限并按 ParentPermissionNId 递归组装树;
/// 第一阶段不提供创建或改名 Permission.NId 的 API。
/// </summary>
public sealed class PermissionQueryService : IPermissionQueryService
{
    private readonly IManagementStore _store;

    public PermissionQueryService(IManagementStore store)
    {
        _store = store;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<PermissionTreeNode>> GetTreeAsync(CancellationToken cancellationToken)
    {
        var permissions = await _store.GetAllPermissionsAsync(cancellationToken);

        var childrenByParent = permissions
            .Where(p => !string.IsNullOrWhiteSpace(p.ParentPermissionNId))
            .GroupBy(p => p.ParentPermissionNId!, StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<Permission>)g.ToList(),
                StringComparer.Ordinal);
        var knownNIds = permissions
            .Select(p => p.NId)
            .ToHashSet(StringComparer.Ordinal);

        var roots = permissions
            .Where(p => string.IsNullOrWhiteSpace(p.ParentPermissionNId) || !knownNIds.Contains(p.ParentPermissionNId!))
            .OrderBy(p => p.NormalizedNId, StringComparer.Ordinal)
            .Select(p => ToNode(p, childrenByParent))
            .ToList();

        return roots;
    }

    private static PermissionTreeNode ToNode(
        Permission permission,
        IReadOnlyDictionary<string, IReadOnlyList<Permission>> childrenByParent)
    {
        var children = childrenByParent.TryGetValue(permission.NId, out var childList) ? childList : [];
        return new PermissionTreeNode(
            permission.NId,
            permission.Name,
            permission.Type.ToString(),
            permission.ParentPermissionNId,
            permission.Description,
            permission.Status.ToString(),
            children
                .OrderBy(c => c.NormalizedNId, StringComparer.Ordinal)
                .Select(c => ToNode(c, childrenByParent))
                .ToList(),
            IsProtectedPermission(permission.NId));
    }

    private static bool IsProtectedPermission(string permissionNId) =>
        permissionNId.StartsWith("systemdata.service-initialization.", StringComparison.OrdinalIgnoreCase)
        || permissionNId.StartsWith("systemdata.database-orchestration.", StringComparison.OrdinalIgnoreCase);
}
