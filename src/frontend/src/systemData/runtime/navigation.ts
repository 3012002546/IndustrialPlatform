import { House, Link, Menu, Monitor, Setting, Tickets, Tools, User } from '@element-plus/icons-vue'

import { isPcDensity, isThemeMode, isThemePalette } from '@/theme'
import type { NavigationGroup, NavigationItem } from '@/components/navigation/types'
import type { NavigationRuntimeNodeDto, ThemePolicyDto } from '@/api/systemData/types'
import type { PcDensity, ThemeMode, ThemePalette, UiPreferencesV1 } from '@/theme/types'

const ICONS = {
  house: House,
  link: Link,
  menu: Menu,
  monitor: Monitor,
  setting: Setting,
  tickets: Tickets,
  tools: Tools,
  user: User,
} as const

function iconFor(iconKey: string | null) {
  return (iconKey === null ? undefined : ICONS[iconKey.toLowerCase() as keyof typeof ICONS]) ?? Menu
}

function mapItem(node: NavigationRuntimeNodeDto): NavigationItem | null {
  if (node.kind.toLowerCase() === 'group') return null
  if (node.routeName === null || node.routeName.trim() === '') return null
  return {
    id: node.nodeNId,
    label: node.label,
    routeName: node.routeName,
    icon: iconFor(node.iconKey),
    ...(node.requiredPermissionNId === null ? {} : { permission: node.requiredPermissionNId }),
    ...(node.featureNId === null ? {} : { featureNId: node.featureNId }),
    children: node.children.map(mapItem).filter((item): item is NavigationItem => item !== null),
  }
}

/** Backend tree → PF-01 public NavigationGroup port. */
export function mapRuntimeNavigation(
  nodes: readonly NavigationRuntimeNodeDto[],
): NavigationGroup[] {
  return [...nodes]
    .sort((a, b) => a.displayOrder - b.displayOrder)
    .filter((node) => node.kind.toLowerCase() === 'group')
    .map((node) => ({
      id: node.nodeNId,
      label: node.label,
      icon: iconFor(node.iconKey),
      items: node.children
        .map(mapItem)
        .filter((item): item is NavigationItem => item !== null)
        .sort((a, b) => a.label.localeCompare(b.label, 'zh-Hans')),
    }))
}

function filterItem(
  item: NavigationItem,
  permissionNIds: ReadonlySet<string>,
  enabledFeatures: ReadonlySet<string>,
): NavigationItem | null {
  if (item.permission !== undefined && !permissionNIds.has(item.permission)) return null
  if (item.featureNId !== undefined && !enabledFeatures.has(item.featureNId)) return null
  const children = (item.children ?? [])
    .map((child) => filterItem(child, permissionNIds, enabledFeatures))
    .filter((child): child is NavigationItem => child !== null)
  return { ...item, ...(children.length === 0 ? {} : { children }) }
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
