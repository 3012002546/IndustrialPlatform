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

import type { NavigationGroup, NavigationItem, NavigationSection } from './types'

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
    sections: [
      {
        id: 'identity-access',
        label: '身份与访问',
        labelKey: 'shell.navigation.section.identity-access',
        fallbackLabel: '身份与访问',
      },
      {
        id: 'organization-platform',
        label: '组织与平台',
        labelKey: 'shell.navigation.section.organization-platform',
        fallbackLabel: '组织与平台',
      },
    ],
    items: [
      {
        id: 'identity-users',
        label: '用户管理',
        sectionId: 'identity-access',
        routeName: 'identity-users',
        icon: User,
        permission: PERMISSIONS.userView,
      },
      {
        id: 'identity-user-groups',
        label: '用户组管理',
        sectionId: 'identity-access',
        routeName: 'identity-user-groups',
        icon: UserFilled,
        permission: PERMISSIONS.userGroupView,
      },
      {
        id: 'identity-roles',
        label: '角色权限',
        sectionId: 'identity-access',
        routeName: 'identity-roles',
        icon: Avatar,
        permission: PERMISSIONS.roleView,
      },
      {
        id: 'identity-permissions',
        label: '权限目录',
        sectionId: 'identity-access',
        routeName: 'identity-permissions',
        icon: Lock,
        permission: PERMISSIONS.permissionView,
      },
      {
        id: 'identity-audits',
        label: '登录审计',
        sectionId: 'identity-access',
        routeName: 'identity-audits',
        icon: Tickets,
        permission: PERMISSIONS.auditLoginView,
      },
      {
        id: 'identity-sso-providers',
        label: '企业登录源',
        sectionId: 'identity-access',
        routeName: 'sso-providers',
        icon: Monitor,
        permission: PERMISSIONS.ssoView,
      },
      {
        id: 'identity-sso-clients',
        label: 'SSO Client',
        sectionId: 'identity-access',
        routeName: 'sso-clients',
        icon: Setting,
        permission: PERMISSIONS.ssoView,
      },
      {
        id: 'systemdata-organizations',
        label: '行政组织与岗位',
        sectionId: 'organization-platform',
        routeName: 'systemdata-organizations',
        icon: User,
        permission: PERMISSIONS.systemDataOrganizationView,
      },
      {
        id: 'systemdata-assignments',
        label: '用户任职',
        sectionId: 'organization-platform',
        routeName: 'systemdata-assignments',
        icon: UserFilled,
        permission: PERMISSIONS.systemDataAssignmentView,
      },
      {
        id: 'systemdata-navigation',
        label: '导航与资源发布',
        sectionId: 'organization-platform',
        routeName: 'systemdata-navigation',
        icon: Tickets,
        permission: PERMISSIONS.systemDataNavigationView,
      },
      {
        id: 'systemdata-features',
        label: '功能开关',
        sectionId: 'organization-platform',
        routeName: 'systemdata-features',
        icon: Setting,
        permission: PERMISSIONS.systemDataFeatureView,
      },
      {
        id: 'systemdata-services',
        label: '服务目录',
        sectionId: 'organization-platform',
        routeName: 'systemdata-services',
        icon: Monitor,
        permission: PERMISSIONS.systemDataServiceCatalogView,
      },
      {
        id: 'systemdata-themes',
        label: '租户主题策略',
        sectionId: 'organization-platform',
        routeName: 'systemdata-themes',
        icon: Setting,
        permission: PERMISSIONS.systemDataThemePolicyView,
      },
      {
        id: 'systemdata-service-initialization',
        label: '服务初始化编排',
        sectionId: 'organization-platform',
        routeName: 'systemdata-service-initialization',
        icon: Monitor,
        permission: PERMISSIONS.systemDataServiceInitializationView,
      },
    ],
  },
]

function normalizeItem(item: NavigationItem): NavigationItem {
  return {
    ...item,
    labelKey: item.labelKey ?? `shell.navigation.item.${item.id}`,
    fallbackLabel: item.fallbackLabel ?? item.label,
    ...(item.children === undefined
      ? {}
      : { children: item.children.map(normalizeItem) }),
  }
}

function normalizeSection(section: NavigationSection): NavigationSection {
  return {
    ...section,
    labelKey: section.labelKey ?? `shell.navigation.section.${section.id}`,
    fallbackLabel: section.fallbackLabel ?? section.label,
  }
}

export function normalizeNavigationGroups(
  groups: readonly NavigationGroup[],
): NavigationGroup[] {
  return groups.map((group) => ({
    ...group,
    labelKey: group.labelKey ?? `shell.navigation.group.${group.id}`,
    fallbackLabel: group.fallbackLabel ?? group.label,
    ...(group.sections === undefined
      ? {}
      : { sections: group.sections.map(normalizeSection) }),
    items: group.items.map(normalizeItem),
  }))
}

/** PF-01 公开导航端口:运行适配器只替换数组内容,不触碰壳组件内部实现。 */
export const pcNavigationGroups = shallowReactive<NavigationGroup[]>([
  ...normalizeNavigationGroups(DEFAULT_PC_NAVIGATION_GROUPS),
])

export function replacePcNavigationGroups(groups: readonly NavigationGroup[]): void {
  pcNavigationGroups.splice(0, pcNavigationGroups.length, ...normalizeNavigationGroups(groups))
}

export function resetPcNavigationGroups(): void {
  replacePcNavigationGroups(DEFAULT_PC_NAVIGATION_GROUPS)
}

/** 返回一份不与当前响应式导航共享对象的静态基线,供运行时权限过滤使用。 */
export function getDefaultPcNavigationGroups(): NavigationGroup[] {
  return normalizeNavigationGroups(DEFAULT_PC_NAVIGATION_GROUPS)
}
