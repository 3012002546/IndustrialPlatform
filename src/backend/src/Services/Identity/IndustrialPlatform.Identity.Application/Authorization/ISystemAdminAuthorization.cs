using IndustrialPlatform.SharedKernel.Exceptions;
using IndustrialPlatform.Identity.Application.Management;

namespace IndustrialPlatform.Identity.Application.Authorization;

/// <summary>
/// 当前租户 SYSTEM_ADMIN 资格的唯一应用层判定入口。
/// 维护系统管理员关系时必须以当前权威快照为准，不能信任请求体或旧 JWT 权限声明。
/// </summary>
public interface ISystemAdminAuthorization
{
    Task<bool> IsSystemAdminAsync(
        string tenantNId,
        string userNId,
        CancellationToken cancellationToken);

    Task EnsureSystemAdminAsync(
        string tenantNId,
        string userNId,
        CancellationToken cancellationToken);
}

public sealed class SystemAdminAuthorization : ISystemAdminAuthorization
{
    private readonly IAuthorizationDataStore _dataStore;

    public SystemAdminAuthorization(IAuthorizationDataStore dataStore)
    {
        ArgumentNullException.ThrowIfNull(dataStore);
        _dataStore = dataStore;
    }

    public async Task<bool> IsSystemAdminAsync(
        string tenantNId,
        string userNId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(tenantNId) || string.IsNullOrWhiteSpace(userNId))
        {
            return false;
        }

        var snapshot = await _dataStore.GetSnapshotAsync(tenantNId, userNId, cancellationToken);
        return snapshot is { Status: Domain.Users.UserStatus.Active, IsSystemAdmin: true }
            && string.Equals(snapshot.TenantNId, tenantNId, StringComparison.Ordinal)
            && string.Equals(snapshot.UserNId, userNId, StringComparison.Ordinal);
    }

    public async Task EnsureSystemAdminAsync(
        string tenantNId,
        string userNId,
        CancellationToken cancellationToken)
    {
        if (!await IsSystemAdminAsync(tenantNId, userNId, cancellationToken))
        {
            throw new BusinessRuleViolationException("只有当前租户的有效 SYSTEM_ADMIN 才能维护系统管理员。");
        }
    }
}
