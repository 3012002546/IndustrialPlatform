using IndustrialPlatform.Identity.Application.Management;
using IndustrialPlatform.SystemData.Application.Administration;
using IndustrialPlatform.SystemData.Application.IdentityDirectory;
using IndustrialPlatform.SystemData.Contracts.Administration;

namespace IndustrialPlatform.UnifiedHost;

/// <summary>
/// UnifiedHost 的进程内 Identity 目录适配器。它读取真实 Identity
/// ManagementStore，不依赖回环 HTTP，也不在目录不可用时伪造 Active 用户。
/// </summary>
public sealed class InProcessIdentityUserDirectory : IIdentityUserDirectory
{
    private readonly IManagementStore _managementStore;

    public InProcessIdentityUserDirectory(IManagementStore managementStore) => _managementStore = managementStore;

    public async Task<IdentityUserDirectoryEntryV1?> GetAsync(string tenantNId, string userNId, CancellationToken cancellationToken)
    {
        StoredUser? user;
        try
        {
            user = await _managementStore.GetUserAsync(userNId, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new IdentityDirectoryUnavailableException($"Identity user directory is unavailable: {exception.Message}");
        }

        if (user is null || !string.Equals(user.TenantNId, tenantNId, StringComparison.Ordinal)) return null;
        return new IdentityUserDirectoryEntryV1
        {
            TenantNId = user.TenantNId,
            UserNId = user.NId,
            LoginName = user.LoginName,
            Name = user.Name,
            Status = user.Status.ToString(),
            AuthVersion = user.ConcurrencyVersion.ToString("N"),
            IsSystemAdmin = user.EffectiveRoleNIds.Any(value => string.Equals(value, "system_admin", StringComparison.OrdinalIgnoreCase)),
        };
    }
}
