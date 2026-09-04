using IndustrialPlatform.Identity.Application.Management;
using IndustrialPlatform.SystemData.Application.ControlPlane;
using IndustrialPlatform.SystemData.Application.Reliability;

namespace IndustrialPlatform.UnifiedHost;

/// <summary>
/// UnifiedHost 内直接读取 Identity 权限目录，避免 SystemData 基线种子通过 HTTP 回环自身。
/// 只返回当前目录已存在权限的验证回执，不伪造缺失权限。
/// </summary>
public sealed class InProcessIdentityPermissionRegistry : IIdentityPermissionRegistry
{
    private readonly IManagementStore _managementStore;

    public InProcessIdentityPermissionRegistry(IManagementStore managementStore) => _managementStore = managementStore;

    public async Task<PermissionRegistrationReceipt?> VerifyAsync(
        PermissionManifestV1 manifest,
        CancellationToken cancellationToken)
    {
        var permissionNIds = (await _managementStore.GetAllPermissionsAsync(cancellationToken))
            .Select(permission => permission.NId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (manifest.Permissions.Any(permission => !permissionNIds.Contains(permission.PermissionNId)))
            return null;

        return new PermissionRegistrationReceipt(
            manifest.ModuleNId,
            manifest.ManifestVersion,
            manifest.Checksum,
            true,
            DateTimeOffset.UtcNow);
    }
}
