using System.Text.Json;
using IndustrialPlatform.Identity.Domain.Permissions;
using IndustrialPlatform.Identity.Domain.Roles;
using IndustrialPlatform.Identity.Domain.Sso;
using IndustrialPlatform.Identity.Domain.Users;
using IndustrialPlatform.Identity.Domain.UserGroups;
using IndustrialPlatform.Identity.Infrastructure.Persistence.Entities;

namespace IndustrialPlatform.Identity.Infrastructure.Persistence;

/// <summary>
/// POCO ↔ 聚合双向映射助手,集中承载 snake_case 物理列与领域类型的转换。
/// 持久化层专用(internal);领域不变量仍由聚合构造与业务方法维护,重建构造不重新校验。
/// </summary>
internal static class TableMapper
{
    public static UserTable ToTable(User user) => new()
    {
        Id = user.Id,
        IsFrozen = user.IsFrozen,
        IsLocked = user.IsLocked,
        IsDeleted = user.IsDeleted,
        EntityType = user.EntityType,
        CreatedOn = user.CreatedOn,
        LastUpdatedOn = user.LastUpdatedOn,
        OptimisticVersion = user.OptimisticVersion,
        ConcurrencyVersion = user.ConcurrencyVersion,
        TenantNId = user.TenantNId,
        NId = user.NId,
        NormalizedNId = user.NormalizedNId,
        LoginName = user.LoginName,
        NormalizedLoginName = user.NormalizedLoginName,
        Name = user.Name,
        PasswordHash = user.PasswordHash,
        Email = user.Email,
        Phone = user.Phone,
        Status = user.Status,
        FailedLoginCount = user.FailedLoginCount,
        LockedUntil = user.LockedUntil,
        AuthVersion = user.AuthVersion,
        LastLoginOn = user.LastLoginOn,
        MustChangePassword = user.MustChangePassword,
    };

    public static User ToUser(UserTable row, IReadOnlyCollection<UserRole> userRoles) => new(
        row.Id,
        row.TenantNId,
        row.NId,
        row.NormalizedNId,
        row.LoginName,
        row.NormalizedLoginName,
        row.Name,
        row.PasswordHash,
        row.Email,
        row.Phone,
        row.Status,
        row.FailedLoginCount,
        row.LockedUntil,
        row.AuthVersion,
        row.LastLoginOn,
        row.MustChangePassword,
        userRoles,
        row.IsFrozen,
        row.IsLocked,
        row.IsDeleted,
        row.EntityType,
        row.CreatedOn,
        row.LastUpdatedOn,
        row.OptimisticVersion,
        row.ConcurrencyVersion);

    public static UserRoleTable ToTable(UserRole userRole) => new()
    {
        Id = userRole.Id,
        IsFrozen = userRole.IsFrozen,
        IsLocked = userRole.IsLocked,
        IsDeleted = userRole.IsDeleted,
        EntityType = userRole.EntityType,
        CreatedOn = userRole.CreatedOn,
        LastUpdatedOn = userRole.LastUpdatedOn,
        OptimisticVersion = userRole.OptimisticVersion,
        ConcurrencyVersion = userRole.ConcurrencyVersion,
        TenantNId = userRole.TenantNId,
        UserId = userRole.UserId,
        UserIsDeleted = userRole.UserIsDeleted,
        RoleId = userRole.RoleId,
        RoleIsDeleted = userRole.RoleIsDeleted,
    };

    public static UserRole ToUserRole(UserRoleTable row) => new(
        row.Id,
        row.TenantNId,
        row.UserId,
        row.UserIsDeleted,
        row.RoleId,
        row.RoleIsDeleted,
        row.IsFrozen,
        row.IsLocked,
        row.IsDeleted,
        row.EntityType,
        row.CreatedOn,
        row.LastUpdatedOn,
        row.OptimisticVersion,
        row.ConcurrencyVersion);

    public static RoleTable ToTable(Role role) => new()
    {
        Id = role.Id,
        IsFrozen = role.IsFrozen,
        IsLocked = role.IsLocked,
        IsDeleted = role.IsDeleted,
        EntityType = role.EntityType,
        CreatedOn = role.CreatedOn,
        LastUpdatedOn = role.LastUpdatedOn,
        OptimisticVersion = role.OptimisticVersion,
        ConcurrencyVersion = role.ConcurrencyVersion,
        TenantNId = role.TenantNId,
        NId = role.NId,
        NormalizedNId = role.NormalizedNId,
        Name = role.Name,
        Description = role.Description,
        IsSystem = role.IsSystem,
    };

    public static Role ToRole(RoleTable row, IReadOnlyCollection<RolePermission> permissions) => new(
        row.Id,
        row.TenantNId,
        row.NId,
        row.NormalizedNId,
        row.Name,
        row.Description,
        row.IsSystem,
        permissions,
        row.IsFrozen,
        row.IsLocked,
        row.IsDeleted,
        row.EntityType,
        row.CreatedOn,
        row.LastUpdatedOn,
        row.OptimisticVersion,
        row.ConcurrencyVersion);

    public static RolePermissionTable ToTable(RolePermission rolePermission) => new()
    {
        Id = rolePermission.Id,
        IsFrozen = rolePermission.IsFrozen,
        IsLocked = rolePermission.IsLocked,
        IsDeleted = rolePermission.IsDeleted,
        EntityType = rolePermission.EntityType,
        CreatedOn = rolePermission.CreatedOn,
        LastUpdatedOn = rolePermission.LastUpdatedOn,
        OptimisticVersion = rolePermission.OptimisticVersion,
        ConcurrencyVersion = rolePermission.ConcurrencyVersion,
        RoleId = rolePermission.RoleId,
        RoleIsDeleted = rolePermission.RoleIsDeleted,
        PermissionId = rolePermission.PermissionId,
        PermissionIsDeleted = rolePermission.PermissionIsDeleted,
    };

    public static RolePermission ToRolePermission(RolePermissionTable row) => new(
        row.Id,
        row.RoleId,
        row.RoleIsDeleted,
        row.PermissionId,
        row.PermissionIsDeleted,
        row.IsFrozen,
        row.IsLocked,
        row.IsDeleted,
        row.EntityType,
        row.CreatedOn,
        row.LastUpdatedOn,
        row.OptimisticVersion,
        row.ConcurrencyVersion);

    public static PermissionTable ToTable(Permission permission) => new()
    {
        Id = permission.Id,
        IsFrozen = permission.IsFrozen,
        IsLocked = permission.IsLocked,
        IsDeleted = permission.IsDeleted,
        EntityType = permission.EntityType,
        CreatedOn = permission.CreatedOn,
        LastUpdatedOn = permission.LastUpdatedOn,
        OptimisticVersion = permission.OptimisticVersion,
        ConcurrencyVersion = permission.ConcurrencyVersion,
        NId = permission.NId,
        NormalizedNId = permission.NormalizedNId,
        Name = permission.Name,
        Type = permission.Type,
        ParentPermissionNId = permission.ParentPermissionNId,
        ParentPermissionId = null,
        ParentPermissionIsDeleted = null,
        Description = permission.Description,
        Status = permission.Status,
    };

    public static Permission ToPermission(PermissionTable row) => new(
        row.Id,
        row.NId,
        row.NormalizedNId,
        row.Name,
        row.Type,
        row.ParentPermissionNId,
        row.Description,
        row.Status,
        row.IsFrozen,
        row.IsLocked,
        row.IsDeleted,
        row.EntityType,
        row.CreatedOn,
        row.LastUpdatedOn,
        row.OptimisticVersion,
        row.ConcurrencyVersion);

    // ---- 用户组(TASK-ID-017)----

    public static UserGroupTable ToTable(UserGroup group) => new()
    {
        Id = group.Id,
        IsFrozen = group.IsFrozen,
        IsLocked = group.IsLocked,
        IsDeleted = group.IsDeleted,
        EntityType = group.EntityType,
        CreatedOn = group.CreatedOn,
        LastUpdatedOn = group.LastUpdatedOn,
        OptimisticVersion = group.OptimisticVersion,
        ConcurrencyVersion = group.ConcurrencyVersion,
        TenantNId = group.TenantNId,
        NId = group.NId,
        NormalizedNId = group.NormalizedNId,
        Name = group.Name,
        Description = group.Description,
        Status = group.Status,
    };

    public static UserGroup ToUserGroup(
        UserGroupTable row,
        IReadOnlyCollection<UserGroupMembership> memberships,
        IReadOnlyCollection<UserGroupRole> roles) => new(
        row.Id,
        row.TenantNId,
        row.NId,
        row.NormalizedNId,
        row.Name,
        row.Description,
        row.Status,
        memberships,
        roles,
        row.IsFrozen,
        row.IsLocked,
        row.IsDeleted,
        row.EntityType,
        row.CreatedOn,
        row.LastUpdatedOn,
        row.OptimisticVersion,
        row.ConcurrencyVersion);

    public static UserGroupMembershipTable ToTable(UserGroupMembership membership) => new()
    {
        Id = membership.Id,
        IsFrozen = membership.IsFrozen,
        IsLocked = membership.IsLocked,
        IsDeleted = membership.IsDeleted,
        EntityType = membership.EntityType,
        CreatedOn = membership.CreatedOn,
        LastUpdatedOn = membership.LastUpdatedOn,
        OptimisticVersion = membership.OptimisticVersion,
        ConcurrencyVersion = membership.ConcurrencyVersion,
        TenantNId = membership.TenantNId,
        UserGroupId = membership.UserGroupId,
        UserGroupIsDeleted = membership.UserGroupIsDeleted,
        UserId = membership.UserId,
        UserIsDeleted = membership.UserIsDeleted,
    };

    public static UserGroupMembership ToUserGroupMembership(UserGroupMembershipTable row) => new(
        row.Id,
        row.TenantNId,
        row.UserGroupId,
        row.UserGroupIsDeleted,
        row.UserId,
        row.UserIsDeleted,
        row.IsFrozen,
        row.IsLocked,
        row.IsDeleted,
        row.EntityType,
        row.CreatedOn,
        row.LastUpdatedOn,
        row.OptimisticVersion,
        row.ConcurrencyVersion);

    public static UserGroupRoleTable ToTable(UserGroupRole role) => new()
    {
        Id = role.Id,
        IsFrozen = role.IsFrozen,
        IsLocked = role.IsLocked,
        IsDeleted = role.IsDeleted,
        EntityType = role.EntityType,
        CreatedOn = role.CreatedOn,
        LastUpdatedOn = role.LastUpdatedOn,
        OptimisticVersion = role.OptimisticVersion,
        ConcurrencyVersion = role.ConcurrencyVersion,
        TenantNId = role.TenantNId,
        UserGroupId = role.UserGroupId,
        UserGroupIsDeleted = role.UserGroupIsDeleted,
        RoleId = role.RoleId,
        RoleIsDeleted = role.RoleIsDeleted,
    };

    public static UserGroupRole ToUserGroupRole(UserGroupRoleTable row) => new(
        row.Id,
        row.TenantNId,
        row.UserGroupId,
        row.UserGroupIsDeleted,
        row.RoleId,
        row.RoleIsDeleted,
        row.IsFrozen,
        row.IsLocked,
        row.IsDeleted,
        row.EntityType,
        row.CreatedOn,
        row.LastUpdatedOn,
        row.OptimisticVersion,
        row.ConcurrencyVersion);

    // ---- SSO(TASK-ID-013)----
    public static SsoProviderTable ToTable(IdentitySsoProvider provider) => new()
    {
        Id = provider.Id,
        IsFrozen = provider.IsFrozen,
        IsLocked = provider.IsLocked,
        IsDeleted = provider.IsDeleted,
        EntityType = provider.EntityType,
        CreatedOn = provider.CreatedOn,
        LastUpdatedOn = provider.LastUpdatedOn,
        OptimisticVersion = provider.OptimisticVersion,
        ConcurrencyVersion = provider.ConcurrencyVersion,
        TenantNId = provider.TenantNId,
        NId = provider.NId,
        NormalizedNId = provider.NormalizedNId,
        Name = provider.Name,
        Protocol = provider.Protocol,
        AuthorityOrMetadataUrl = provider.AuthorityOrMetadataUrl,
        ClientIdOrEntityId = provider.ClientIdOrEntityId,
        SecretOrCertificateReference = provider.SecretOrCertificateReference,
        CallbackPath = provider.CallbackPath,
        Enabled = provider.Enabled,
        AutoRedirect = provider.AutoRedirect,
        ProvisioningMode = provider.ProvisioningMode,
        LogoutMode = provider.LogoutMode,
        AllowedEmailDomainsJson = JsonSerializer.Serialize(provider.AllowedEmailDomains),
        JitDefaultRoleNIdsJson = JsonSerializer.Serialize(provider.JitDefaultRoleNIds),
    };

    public static IdentitySsoProvider ToSsoProvider(SsoProviderTable row) => new(
        row.Id,
        row.TenantNId,
        row.NId,
        row.NormalizedNId,
        row.Name,
        row.Protocol,
        row.AuthorityOrMetadataUrl,
        row.ClientIdOrEntityId,
        row.SecretOrCertificateReference,
        row.CallbackPath,
        row.Enabled,
        row.AutoRedirect,
        row.ProvisioningMode,
        row.LogoutMode,
        DeserializeList(row.AllowedEmailDomainsJson),
        DeserializeList(row.JitDefaultRoleNIdsJson),
        row.IsFrozen,
        row.IsLocked,
        row.IsDeleted,
        row.EntityType,
        row.CreatedOn,
        row.LastUpdatedOn,
        row.OptimisticVersion,
        row.ConcurrencyVersion);

    public static SsoExternalAccountTable ToTable(IdentityExternalAccount account) => new()
    {
        Id = account.Id,
        IsFrozen = account.IsFrozen,
        IsLocked = account.IsLocked,
        IsDeleted = account.IsDeleted,
        EntityType = account.EntityType,
        CreatedOn = account.CreatedOn,
        LastUpdatedOn = account.LastUpdatedOn,
        OptimisticVersion = account.OptimisticVersion,
        ConcurrencyVersion = account.ConcurrencyVersion,
        NId = account.NId,
        NormalizedNId = account.NormalizedNId,
        SsoProviderId = account.SsoProviderId,
        SsoProviderIsDeleted = account.SsoProviderIsDeleted,
        ExternalSubject = account.ExternalSubject,
        UserId = account.UserId,
        UserIsDeleted = account.UserIsDeleted,
        ExternalName = account.ExternalName,
        ExternalEmail = account.ExternalEmail,
        LastLoginOn = account.LastLoginOn,
    };

    public static IdentityExternalAccount ToSsoExternalAccount(SsoExternalAccountTable row) => new(
        row.Id,
        row.NId,
        row.NormalizedNId,
        row.SsoProviderId,
        row.SsoProviderIsDeleted,
        row.ExternalSubject,
        row.UserId,
        row.UserIsDeleted,
        row.ExternalName,
        row.ExternalEmail,
        row.LastLoginOn,
        row.IsFrozen,
        row.IsLocked,
        row.IsDeleted,
        row.EntityType,
        row.CreatedOn,
        row.LastUpdatedOn,
        row.OptimisticVersion,
        row.ConcurrencyVersion);

    public static SsoClientTable ToTable(IdentitySsoClient client) => new()
    {
        Id = client.Id,
        IsFrozen = client.IsFrozen,
        IsLocked = client.IsLocked,
        IsDeleted = client.IsDeleted,
        EntityType = client.EntityType,
        CreatedOn = client.CreatedOn,
        LastUpdatedOn = client.LastUpdatedOn,
        OptimisticVersion = client.OptimisticVersion,
        ConcurrencyVersion = client.ConcurrencyVersion,
        TenantNId = client.TenantNId,
        NId = client.NId,
        NormalizedNId = client.NormalizedNId,
        Name = client.Name,
        OAuthClientId = client.OAuthClientId,
        Enabled = client.Enabled,
    };

    public static IdentitySsoClient ToSsoClient(SsoClientTable row, IReadOnlyCollection<IdentitySsoClientEndpoint> endpoints) => new(
        row.Id,
        row.TenantNId,
        row.NId,
        row.NormalizedNId,
        row.Name,
        row.OAuthClientId,
        row.Enabled,
        endpoints,
        row.IsFrozen,
        row.IsLocked,
        row.IsDeleted,
        row.EntityType,
        row.CreatedOn,
        row.LastUpdatedOn,
        row.OptimisticVersion,
        row.ConcurrencyVersion);

    public static SsoClientEndpointTable ToTable(IdentitySsoClientEndpoint endpoint) => new()
    {
        Id = endpoint.Id,
        IsFrozen = endpoint.IsFrozen,
        IsLocked = endpoint.IsLocked,
        IsDeleted = endpoint.IsDeleted,
        EntityType = endpoint.EntityType,
        CreatedOn = endpoint.CreatedOn,
        LastUpdatedOn = endpoint.LastUpdatedOn,
        OptimisticVersion = endpoint.OptimisticVersion,
        ConcurrencyVersion = endpoint.ConcurrencyVersion,
        SsoClientId = endpoint.SsoClientId,
        SsoClientIsDeleted = endpoint.SsoClientIsDeleted,
        NId = endpoint.NId,
        EndpointType = endpoint.EndpointType,
        Uri = endpoint.Uri,
        NormalizedUri = endpoint.NormalizedUri,
        Enabled = endpoint.Enabled,
    };

    public static IdentitySsoClientEndpoint ToSsoClientEndpoint(SsoClientEndpointTable row) => new(
        row.Id,
        row.SsoClientId,
        row.SsoClientIsDeleted,
        row.NId,
        row.EndpointType,
        row.Uri,
        row.NormalizedUri,
        row.Enabled,
        row.IsFrozen,
        row.IsLocked,
        row.IsDeleted,
        row.EntityType,
        row.CreatedOn,
        row.LastUpdatedOn,
        row.OptimisticVersion,
        row.ConcurrencyVersion);

    public static SsoBrowserSessionTable ToTable(IdentitySsoBrowserSession session) => new()
    {
        Id = session.Id,
        IsFrozen = session.IsFrozen,
        IsLocked = session.IsLocked,
        IsDeleted = session.IsDeleted,
        EntityType = session.EntityType,
        CreatedOn = session.CreatedOn,
        LastUpdatedOn = session.LastUpdatedOn,
        OptimisticVersion = session.OptimisticVersion,
        ConcurrencyVersion = session.ConcurrencyVersion,
        TenantNId = session.TenantNId,
        NId = session.NId,
        NormalizedNId = session.NormalizedNId,
        ProviderNId = session.ProviderNId,
        UserId = session.UserId,
        UserIsDeleted = session.UserIsDeleted,
        SessionHandleHash = session.SessionHandleHash,
        AuthVersion = session.AuthVersion,
        LastActivityOn = session.LastActivityOn,
        ExpiresOn = session.ExpiresOn,
        RevokedOn = session.RevokedOn,
        RevokeReason = session.RevokeReason,
    };

    public static IdentitySsoBrowserSession ToSsoBrowserSession(SsoBrowserSessionTable row) => new(
        row.Id,
        row.TenantNId,
        row.NId,
        row.NormalizedNId,
        row.ProviderNId,
        row.UserId,
        row.UserIsDeleted,
        row.SessionHandleHash,
        row.AuthVersion,
        row.LastActivityOn,
        row.ExpiresOn,
        row.RevokedOn,
        row.RevokeReason,
        row.IsFrozen,
        row.IsLocked,
        row.IsDeleted,
        row.EntityType,
        row.CreatedOn,
        row.LastUpdatedOn,
        row.OptimisticVersion,
        row.ConcurrencyVersion);

    /// <summary>反序列化 JSON 列表字段(Provider 的域/角色列表);损坏数据按空列表容错。</summary>
    private static List<string> DeserializeList(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<List<string>>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
