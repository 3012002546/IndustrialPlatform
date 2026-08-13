namespace IndustrialPlatform.Identity.Contracts.Management;

/// <summary>
/// 权限目录树节点(§16.3 GET /permissions/tree)。只读展示,
/// 第一阶段不提供任意创建或改名 Permission.NId 的 API。Type 为 PermissionType 枚举名。
/// </summary>
public sealed record PermissionTreeNode(
    string PermissionNId,
    string Name,
    string Type,
    string? ParentPermissionNId,
    string? Description,
    string Status,
    IReadOnlyList<PermissionTreeNode> Children);
