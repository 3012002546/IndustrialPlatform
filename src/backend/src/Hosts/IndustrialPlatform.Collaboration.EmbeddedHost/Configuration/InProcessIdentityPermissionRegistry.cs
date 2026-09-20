using IndustrialPlatform.Identity.Application.Management;
using IndustrialPlatform.SystemData.Application.ControlPlane;
using IndustrialPlatform.SystemData.Application.Reliability;

namespace IndustrialPlatform.Collaboration.EmbeddedHost.Configuration;

/// <summary>
/// 独立宿主复用同进程 Identity 权限目录完成模块初始化基线校验。
/// 这只核对模块权限清单，不向 MES 用户分配权限。
/// </summary>
public sealed class InProcessIdentityPermissionRegistry(IManagementStore managementStore) : IIdentityPermissionRegistry
{
    public async Task<PermissionRegistrationReceipt?> VerifyAsync(
        PermissionManifestV1 manifest,
        CancellationToken cancellationToken)
    {
        var permissionNIds = (await managementStore.GetAllPermissionsAsync(cancellationToken))
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
