/**
 * 前端权限目录(与后端 PermissionCatalog 对齐,§9.2)。
 * 路由 meta、导航菜单与 PermissionGate 一律引用常量,避免字符串漂移。
 */

export const PERMISSIONS = {
  userView: 'identity.user.view',
  userCreate: 'identity.user.create',
  userUpdate: 'identity.user.update',
  userStatus: 'identity.user.status',
  userAssignRole: 'identity.user.assign-role',
  roleView: 'identity.role.view',
  roleCreate: 'identity.role.create',
  roleUpdate: 'identity.role.update',
  roleAssignPermission: 'identity.role.assign-permission',
  permissionView: 'identity.permission.view',
  auditLoginView: 'identity.audit.login.view',
  ssoView: 'identity.sso.view',
  ssoManage: 'identity.sso.manage',
  ssoTest: 'identity.sso.test',
  platformHomeView: 'platform.home.view',
  platformPdaView: 'platform.pda.view',
  platformMobileView: 'platform.mobile.view',
} as const

export type PermissionNId = (typeof PERMISSIONS)[keyof typeof PERMISSIONS]
