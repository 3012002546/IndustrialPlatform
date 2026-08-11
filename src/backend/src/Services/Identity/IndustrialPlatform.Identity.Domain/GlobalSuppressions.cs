using System.Diagnostics.CodeAnalysis;

// "Permission" 是领域术语(§9.2 权限),与 CA1711 预留后缀清单中的 "Permission" 撞名。
// 领域类型名按方案保持 Permission / RolePermission,不重命名。
[assembly: SuppressMessage(
    "Naming",
    "CA1711:Identifiers should not have incorrect suffix",
    Justification = "Permission is the domain term (权限) per spec §9.2; not a reserved suffix misuse.",
    Scope = "type",
    Target = "~T:IndustrialPlatform.Identity.Domain.Permissions.Permission")]

[assembly: SuppressMessage(
    "Naming",
    "CA1711:Identifiers should not have incorrect suffix",
    Justification = "RolePermission is the role-permission relation entity per spec §9.3; not a reserved suffix misuse.",
    Scope = "type",
    Target = "~T:IndustrialPlatform.Identity.Domain.Roles.RolePermission")]
