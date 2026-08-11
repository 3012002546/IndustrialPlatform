using IndustrialPlatform.Identity.Domain.Permissions;
using IndustrialPlatform.Identity.Domain.Roles;
using IndustrialPlatform.Identity.Domain.Users;
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
}
