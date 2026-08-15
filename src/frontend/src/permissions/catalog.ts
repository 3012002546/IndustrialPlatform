/**
 * 前端权限目录(与后端 PermissionCatalog 对齐,§9.2/§29A.5)。
 * 路由 meta、导航菜单与 PermissionGate 一律引用常量,避免字符串漂移。
 */

export const PERMISSIONS = {
  userView: 'identity.user.view',
  userCreate: 'identity.user.create',
  userUpdate: 'identity.user.update',
  userStatus: 'identity.user.status',
  userAssignRole: 'identity.user.assign-role',
  userDelete: 'identity.user.delete',
  userRestore: 'identity.user.restore',
  userResetPassword: 'identity.user.reset-password',
  roleView: 'identity.role.view',
  roleCreate: 'identity.role.create',
  roleUpdate: 'identity.role.update',
  roleAssignPermission: 'identity.role.assign-permission',
  permissionView: 'identity.permission.view',
  auditLoginView: 'identity.audit.login.view',
  userGroupView: 'identity.user-group.view',
  userGroupCreate: 'identity.user-group.create',
  userGroupUpdate: 'identity.user-group.update',
  userGroupStatus: 'identity.user-group.status',
  userGroupAssignMember: 'identity.user-group.assign-member',
  userGroupAssignRole: 'identity.user-group.assign-role',
  userGroupDelete: 'identity.user-group.delete',
  userGroupRestore: 'identity.user-group.restore',
  ssoView: 'identity.sso.view',
  ssoManage: 'identity.sso.manage',
  ssoTest: 'identity.sso.test',
  platformHomeView: 'platform.home.view',
  platformPdaView: 'platform.pda.view',
  platformMobileView: 'platform.mobile.view',
} as const

export type PermissionNId = (typeof PERMISSIONS)[keyof typeof PERMISSIONS]
