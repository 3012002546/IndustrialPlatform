import {
  Avatar,
  House,
  Link,
  Lock,
  Menu,
  Monitor,
  Setting,
  Tickets,
  Tools,
  User,
  UserFilled,
} from '@element-plus/icons-vue'
import type { Component } from 'vue'

import { isPcDensity, isThemeMode, isThemePalette } from '@/theme'
import type {
  NavigationGroup,
  NavigationItem,
  NavigationSection,
} from '@/components/navigation/types'
import type { NavigationRuntimeNodeDto, ThemePolicyDto } from '@/api/systemData/types'
import type { PcDensity, ThemeMode, ThemePalette, UiPreferencesV1 } from '@/theme/types'
import { isRegisteredRouteName } from '@/router/routeNames'
import { getDefaultPcNavigationGroups } from '@/components/navigation/navigation'

const ICONS = new Map<string, Component>([
  ['avatar', Avatar],
  ['house', House],
  ['link', Link],
  ['lock', Lock],
  ['menu', Menu],
  ['monitor', Monitor],
  ['setting', Setting],
  ['tickets', Tickets],
  ['tools', Tools],
  ['user', User],
  ['userfilled', UserFilled],
])
const defaultGroups = getDefaultPcNavigationGroups()
const defaultItems = defaultGroups.flatMap((group) => group.items)

/* Retained only as a compatibility marker for the historical snapshot shape;
// runtime navigation never uses it to add nodes.
const LEGACY_DEFAULT_NAVIGATION_FINGERPRINT = JSON.stringify([
  [null, 'navigation.group.workspace', 'Group', '工作台', null, null, null, null, null, 0],
  ['navigation.group.workspace', 'navigation.link.pc-home', 'Link', '首页', 'systemdata.navigation.pc-home', 'pc-home', 'platform.home.view', null, null, 0],
  ['navigation.group.workspace', 'navigation.link.terminal-preview', 'Link', '终端预览', 'systemdata.navigation.terminal-preview', 'terminal-preview', 'platform.pda.view', null, null, 1],
  [null, 'navigation.group.system', 'Group', '系统管理', null, null, null, null, null, 1],
  ['navigation.group.system', 'navigation.group.identity-access', 'Group', '身份与访问', null, null, null, null, null, 0],
  ['navigation.group.identity-access', 'navigation.link.identity-users', 'Link', '用户管理', 'systemdata.navigation.identity-users', 'identity-users', 'identity.user.view', null, null, 0],
  ['navigation.group.identity-access', 'navigation.link.identity-user-groups', 'Link', '用户组管理', 'systemdata.navigation.identity-user-groups', 'identity-user-groups', 'identity.user-group.view', null, null, 1],
  ['navigation.group.identity-access', 'navigation.link.identity-roles', 'Link', '角色权限', 'systemdata.navigation.identity-roles', 'identity-roles', 'identity.role.view', null, null, 2],
  ['navigation.group.identity-access', 'navigation.link.identity-permissions', 'Link', '权限目录', 'systemdata.navigation.identity-permissions', 'identity-permissions', 'identity.permission.view', null, null, 3],
  ['navigation.group.identity-access', 'navigation.link.identity-audits', 'Link', '登录审计', 'systemdata.navigation.identity-audits', 'identity-audits', 'identity.audit.login.view', null, null, 4],
  ['navigation.group.identity-access', 'navigation.link.identity-sso-providers', 'Link', '企业登录源', 'systemdata.navigation.identity-sso-providers', 'sso-providers', 'identity.sso.view', null, null, 5],
  ['navigation.group.identity-access', 'navigation.link.identity-sso-clients', 'Link', 'SSO Client', 'systemdata.navigation.identity-sso-clients', 'sso-clients', 'identity.sso.view', null, null, 6],
  ['navigation.group.system', 'navigation.group.organization-people', 'Group', '组织与人员', null, null, null, null, null, 1],
  ['navigation.group.organization-people', 'navigation.link.systemdata-organizations', 'Link', '行政组织与岗位', 'systemdata.navigation.systemdata-organizations', 'systemdata-organizations', 'systemdata.organization.view', null, null, 0],
  ['navigation.group.organization-people', 'navigation.link.systemdata-assignments', 'Link', '用户任职', 'systemdata.navigation.systemdata-assignments', 'systemdata-assignments', 'systemdata.assignment.view', null, null, 1],
  ['navigation.group.system', 'navigation.group.menu-platform', 'Group', '菜单与平台配置', null, null, null, null, null, 2],
  ['navigation.group.menu-platform', 'navigation.link.systemdata-navigation', 'Link', '菜单管理', 'systemdata.navigation.systemdata-navigation', 'systemdata-navigation', 'systemdata.navigation.view', null, null, 0],
  ['navigation.group.menu-platform', 'navigation.link.systemdata-features', 'Link', '功能开关', 'systemdata.navigation.systemdata-features', 'systemdata-features', 'systemdata.feature.view', null, null, 1],
  ['navigation.group.menu-platform', 'navigation.link.systemdata-themes', 'Link', '租户主题策略', 'systemdata.navigation.systemdata-themes', 'systemdata-themes', 'systemdata.theme-policy.view', null, null, 2],
  ['navigation.group.system', 'navigation.group.service-operations', 'Group', '服务与运维', null, null, null, null, null, 3],
  ['navigation.group.service-operations', 'navigation.link.systemdata-services', 'Link', '服务目录', 'systemdata.navigation.systemdata-services', 'systemdata-services', 'systemdata.service-catalog.view', null, null, 0],
  ['navigation.group.reference-data', { labelKey: 'shell.navigation.group.reference-data', fallbackLabel: '基础配置' }],
  ['navigation.link.reference-data-dictionaries', { labelKey: 'shell.navigation.item.reference-data-dictionaries', fallbackLabel: '字典管理' }],
  ['navigation.link.reference-data-parameters', { labelKey: 'shell.navigation.item.reference-data-parameters', fallbackLabel: '参数管理' }],
  ['navigation.link.reference-data-dynamic-properties', { labelKey: 'shell.navigation.item.reference-data-dynamic-properties', fallbackLabel: '动态属性' }],
  ['navigation.link.reference-data-units-of-measure', { labelKey: 'shell.navigation.item.reference-data-units-of-measure', fallbackLabel: '计量单位' }],
  ['navigation.link.reference-data-metadata', { labelKey: 'shell.navigation.item.reference-data-metadata', fallbackLabel: '元数据定义' }],
  ['navigation.link.reference-data-coding-rules', { labelKey: 'shell.navigation.item.reference-data-coding-rules', fallbackLabel: '编码规则' }],
  ['navigation.link.reference-data-state-machines', { labelKey: 'shell.navigation.item.reference-data-state-machines', fallbackLabel: '状态机定义' }],
]) */

function builtinNavigationText(): Map<string, { labelKey: string; fallbackLabel: string }> {
  const result = new Map<string, { labelKey: string; fallbackLabel: string }>()

  const add = (key: string, labelKey: string, fallbackLabel: string): void => {
    result.set(key, { labelKey, fallbackLabel })
  }
  const addItem = (item: NavigationItem): void => {
    add(
      `navigation.link.${item.id}`,
      item.labelKey ?? `shell.navigation.item.${item.id}`,
      item.fallbackLabel ?? item.label,
    )
    if (item.routeName !== undefined) {
      add(
        `route:${item.routeName}`,
        item.labelKey ?? `shell.navigation.item.${item.id}`,
        item.fallbackLabel ?? item.label,
      )
    }
    item.children?.forEach(addItem)
  }

  for (const group of defaultGroups) {
    add(
      `navigation.group.${group.id}`,
      group.labelKey ?? `shell.navigation.group.${group.id}`,
      group.fallbackLabel ?? group.label,
    )
    for (const section of group.sections ?? []) {
      add(
        `navigation.group.${section.id}`,
        section.labelKey ?? `shell.navigation.section.${section.id}`,
        section.fallbackLabel ?? section.label,
      )
    }
    group.items.forEach(addItem)
  }

  return result
}

// Build this from the canonical navigation metadata so every platform-owned
// group/section/item follows the same locale contract as the static shell.
const BUILTIN_NAVIGATION_TEXT = builtinNavigationText()

function runtimeText(node: NavigationRuntimeNodeDto): {
  label: string
  labelKey: string
  fallbackLabel: string
} {
  const builtIn =
    BUILTIN_NAVIGATION_TEXT.get(node.nodeNId) ??
    (node.routeName === null ? undefined : BUILTIN_NAVIGATION_TEXT.get(`route:${node.routeName}`))
  if (builtIn === undefined || node.label !== builtIn.fallbackLabel)
    return { label: node.label, labelKey: '', fallbackLabel: node.label }
  return { label: node.label, ...builtIn }
}

function iconFor(node: NavigationRuntimeNodeDto): Component {
  const key = node.iconKey?.trim().replace(/[-_]/g, '').toLowerCase()
  if (key) return ICONS.get(key) ?? Menu

  // Older/default imports have no iconKey. Reuse only icon metadata, never the
  // baseline tree: published labels, parents, order and permissions stay intact.
  const fallback =
    node.kind.toLowerCase() === 'group'
      ? defaultGroups.find(
          (group) => node.nodeNId === group.id || node.nodeNId === `navigation.group.${group.id}`,
        )
      : defaultItems.find((item) => item.routeName === node.routeName)
  return fallback?.icon ?? Menu
}

function mapItem(node: NavigationRuntimeNodeDto, sectionId?: string): NavigationItem | null {
  if (node.kind.toLowerCase() === 'group') return null
  if (node.routeName === null || node.routeName.trim() === '') return null
  if (!isRegisteredRouteName(node.routeName)) return null
  const text = runtimeText(node)
  return {
    id: node.nodeNId,
    ...text,
    routeName: node.routeName,
    icon: iconFor(node),
    ...(node.requiredPermissionNId === null ? {} : { permission: node.requiredPermissionNId }),
    ...(node.featureNId === null ? {} : { featureNId: node.featureNId }),
    ...(sectionId === undefined ? {} : { sectionId }),
    displayOrder: node.displayOrder,
    children: node.children
      .map((child) => mapItem(child, sectionId))
      .filter((item): item is NavigationItem => item !== null)
      .sort((a, b) => (a.displayOrder ?? 0) - (b.displayOrder ?? 0) || a.id.localeCompare(b.id)),
  }
}

/** Backend tree → PF-01 public NavigationGroup port. */
export function mapRuntimeNavigation(
  nodes: readonly NavigationRuntimeNodeDto[],
): NavigationGroup[] {
  const groups = [...nodes]
    .sort((a, b) => a.displayOrder - b.displayOrder || a.nodeNId.localeCompare(b.nodeNId))
    .filter((node) => node.kind.toLowerCase() === 'group')
    .map((node) => {
      const sections: NavigationSection[] = []
      const items: NavigationItem[] = []
      for (const child of node.children) {
        if (child.kind.toLowerCase() === 'group') {
          const sectionId = child.nodeNId
          const text = runtimeText(child)
          sections.push({
            id: sectionId,
            ...text,
            displayOrder: child.displayOrder,
          })
          for (const item of child.children) {
            const mapped = mapItem(item, sectionId)
            if (mapped !== null) items.push(mapped)
          }
        } else {
          const mapped = mapItem(child)
          if (mapped !== null) items.push(mapped)
        }
      }
      sections.sort(
        (a, b) => (a.displayOrder ?? 0) - (b.displayOrder ?? 0) || a.id.localeCompare(b.id),
      )
      return {
        ...runtimeText(node),
        id: node.nodeNId,
        icon: iconFor(node),
        displayOrder: node.displayOrder,
        sections,
        items: items.sort(
          (a, b) => (a.displayOrder ?? 0) - (b.displayOrder ?? 0) || a.id.localeCompare(b.id),
        ),
      }
    })
  return groups
}

function filterItem(
  item: NavigationItem,
  permissionNIds: ReadonlySet<string>,
  enabledFeatures: ReadonlySet<string> | undefined,
): NavigationItem | null {
  if (item.permission !== undefined && !permissionNIds.has(item.permission)) return null
  if (
    item.anyPermissions !== undefined &&
    !item.anyPermissions.some((permission) => permissionNIds.has(permission))
  ) {
    return null
  }
  if (
    enabledFeatures !== undefined &&
    item.featureNId !== undefined &&
    !enabledFeatures.has(item.featureNId)
  )
    return null
  const children = (item.children ?? [])
    .map((child) => filterItem(child, permissionNIds, enabledFeatures))
    .filter((child): child is NavigationItem => child !== null)
  return item.children === undefined ? { ...item } : { ...item, children }
}

/** Apply current AuthUser permissions and effective features, then remove empty groups. */
export function applyNavigationPolicy(
  groups: readonly NavigationGroup[],
  permissionNIds: readonly string[],
  enabledFeatures: ReadonlySet<string>,
): NavigationGroup[] {
  const permissions = new Set(permissionNIds)
  return groups
    .map((group) => ({
      ...group,
      items: group.items
        .map((item) => filterItem(item, permissions, enabledFeatures))
        .filter((item): item is NavigationItem => item !== null),
    }))
    .filter((group) => group.items.length > 0)
}

/** Apply only permission policy to a navigation already filtered by runtime features. */
export function applyPermissionPolicy(
  groups: readonly NavigationGroup[],
  permissionNIds: readonly string[],
): NavigationGroup[] {
  const permissions = new Set(permissionNIds)
  return groups
    .map((group) => ({
      ...group,
      items: group.items
        .map((item) => filterItem(item, permissions, undefined))
        .filter((item): item is NavigationItem => item !== null),
    }))
    .filter((group) => group.items.length > 0)
}

export function featureNIds(
  items: ReadonlyArray<{ featureNId: string; enabled: boolean }>,
): Set<string> {
  return new Set(items.filter((item) => item.enabled).map((item) => item.featureNId))
}

export function themePolicyToTenantDefaults(
  policy: ThemePolicyDto,
): Partial<Omit<UiPreferencesV1, 'version' | 'updatedAt'>> {
  const result: Partial<Omit<UiPreferencesV1, 'version' | 'updatedAt'>> = {}
  if (
    isThemePalette(policy.defaultPalette) &&
    policy.allowedPalettes.includes(policy.defaultPalette)
  )
    result.palette = policy.defaultPalette as ThemePalette
  if (isThemeMode(policy.defaultMode) && policy.allowedModes.includes(policy.defaultMode))
    result.mode = policy.defaultMode as ThemeMode
  if (
    isPcDensity(policy.defaultPcDensity) &&
    policy.allowedPcDensities.includes(policy.defaultPcDensity)
  )
    result.density = policy.defaultPcDensity as PcDensity
  return result
}
