/**
 * PC 平台壳导航静态适配器(§7.7 授权导航视图)。
 * 只注册真实存在的 PC 工作台路由:工作台分组 + 系统管理分组(Identity 管理页)。
 * 不注册 SystemData、通知、聊天等假入口;菜单隐藏不替代 Router Guard 授权。
 * 图标使用正式 Element Plus 图标(@element-plus/icons-vue),禁止 Emoji/文本占位。
 */

import {
  Avatar,
  House,
  Lock,
  Monitor,
  Setting,
  Tickets,
  User,
  UserFilled,
} from '@element-plus/icons-vue'

import { PERMISSIONS } from '@/permissions'

import type { NavigationGroup } from './types'

/**
 * 一级平台分组。PlatformToolRail 渲染此数组并管理当前分组;
 * PlatformFunctionTree 只渲染当前组的授权 items。
 */
export const pcNavigationGroups: readonly NavigationGroup[] = [
  {
    id: 'workspace',
    label: '工作台',
    icon: House,
    items: [
      {
        id: 'pc-home',
        label: '首页',
        routeName: 'pc-home',
        icon: House,
        permission: PERMISSIONS.platformHomeView,
      },
      {
        id: 'terminal-preview',
        label: '终端预览',
        routeName: 'terminal-preview',
        icon: Monitor,
        anyPermissions: [PERMISSIONS.platformPdaView, PERMISSIONS.platformMobileView],
      },
    ],
  },
  {
    id: 'system',
    label: '系统管理',
    icon: Setting,
    items: [
      {
        id: 'identity-users',
        label: '用户管理',
        routeName: 'identity-users',
        icon: User,
        permission: PERMISSIONS.userView,
      },
      {
        id: 'identity-user-groups',
        label: '用户组管理',
        routeName: 'identity-user-groups',
        icon: UserFilled,
        permission: PERMISSIONS.userGroupView,
      },
      {
        id: 'identity-roles',
        label: '角色权限',
        routeName: 'identity-roles',
        icon: Avatar,
        permission: PERMISSIONS.roleView,
      },
      {
        id: 'identity-permissions',
        label: '权限目录',
        routeName: 'identity-permissions',
        icon: Lock,
        permission: PERMISSIONS.permissionView,
      },
      {
        id: 'identity-audits',
        label: '登录审计',
        routeName: 'identity-audits',
        icon: Tickets,
        permission: PERMISSIONS.auditLoginView,
      },
      {
        id: 'identity-sso-providers',
        label: '企业登录源',
        routeName: 'sso-providers',
        icon: Monitor,
        permission: PERMISSIONS.ssoView,
      },
      {
        id: 'identity-sso-clients',
        label: 'SSO Client',
        routeName: 'sso-clients',
        icon: Setting,
        permission: PERMISSIONS.ssoView,
      },
    ],
  },
]
