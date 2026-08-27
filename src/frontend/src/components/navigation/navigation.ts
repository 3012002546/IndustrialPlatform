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
import { shallowReactive } from 'vue'

import { PERMISSIONS } from '@/permissions'

import type { NavigationGroup } from './types'

/**
 * 一级平台分组。PlatformToolRail 渲染此数组并管理当前分组;
 * PlatformFunctionTree 只渲染当前组的授权 items。
 */
const DEFAULT_PC_NAVIGATION_GROUPS: readonly NavigationGroup[] = [
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
      {
        id: 'systemdata-organizations',
        label: '行政组织与岗位',
        routeName: 'systemdata-organizations',
        icon: User,
        permission: PERMISSIONS.systemDataOrganizationView,
      },
      {
        id: 'systemdata-assignments',
        label: '用户任职',
        routeName: 'systemdata-assignments',
        icon: UserFilled,
        permission: PERMISSIONS.systemDataAssignmentView,
      },
      {
        id: 'systemdata-navigation',
        label: '导航与资源发布',
        routeName: 'systemdata-navigation',
        icon: Tickets,
        permission: PERMISSIONS.systemDataNavigationView,
      },
      {
        id: 'systemdata-features',
        label: '功能开关',
        routeName: 'systemdata-features',
        icon: Setting,
        permission: PERMISSIONS.systemDataFeatureView,
      },
      {
        id: 'systemdata-services',
        label: '服务目录',
        routeName: 'systemdata-services',
        icon: Monitor,
        permission: PERMISSIONS.systemDataServiceCatalogView,
      },
      {
        id: 'systemdata-themes',
        label: '租户主题策略',
        routeName: 'systemdata-themes',
        icon: Setting,
        permission: PERMISSIONS.systemDataThemePolicyView,
      },
      {
        id: 'systemdata-service-initialization',
        label: '服务初始化编排',
        routeName: 'systemdata-service-initialization',
        icon: Monitor,
        permission: PERMISSIONS.systemDataServiceInitializationView,
      },
    ],
  },
]

/** PF-01 公开导航端口:运行适配器只替换数组内容,不触碰壳组件内部实现。 */
export const pcNavigationGroups = shallowReactive<NavigationGroup[]>([
  ...DEFAULT_PC_NAVIGATION_GROUPS,
])

export function replacePcNavigationGroups(groups: readonly NavigationGroup[]): void {
  pcNavigationGroups.splice(0, pcNavigationGroups.length, ...groups)
}

export function resetPcNavigationGroups(): void {
  replacePcNavigationGroups(DEFAULT_PC_NAVIGATION_GROUPS)
}
