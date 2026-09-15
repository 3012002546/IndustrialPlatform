using IndustrialPlatform.Identity.Application.Management;
using IndustrialPlatform.SystemData.Application.ControlPlane;
using IndustrialPlatform.SystemData.Application.Reliability;

namespace IndustrialPlatform.Collaboration.EmbeddedHost;

/// <summary>
/// FixedDemo 复用同进程 Identity 权限目录完成 SystemData 基线校验，
/// 不通过 HTTP 回环，也不伪造缺失权限。真实 MES 接入不使用此演示适配器。
/// </summary>
public sealed class FixedDemoIdentityPermissionRegistry(IManagementStore managementStore) : IIdentityPermissionRegistry
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
