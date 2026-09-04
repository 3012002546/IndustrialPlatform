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
  return {
    id: node.nodeNId,
    label: node.label,
    labelKey: '',
    fallbackLabel: node.label,
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
  return [...nodes]
    .sort((a, b) => a.displayOrder - b.displayOrder || a.nodeNId.localeCompare(b.nodeNId))
    .filter((node) => node.kind.toLowerCase() === 'group')
    .map((node) => {
      const sections: NavigationSection[] = []
      const items: NavigationItem[] = []
      for (const child of node.children) {
        if (child.kind.toLowerCase() === 'group') {
          const sectionId = child.nodeNId
          sections.push({
            id: sectionId,
            label: child.label,
            labelKey: '',
            fallbackLabel: child.label,
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
        id: node.nodeNId,
        label: node.label,
        labelKey: '',
        fallbackLabel: node.label,
        icon: iconFor(node),
        displayOrder: node.displayOrder,
        sections,
        items: items.sort(
          (a, b) => (a.displayOrder ?? 0) - (b.displayOrder ?? 0) || a.id.localeCompare(b.id),
        ),
      }
    })
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
