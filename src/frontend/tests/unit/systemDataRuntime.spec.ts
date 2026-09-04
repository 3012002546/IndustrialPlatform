import { describe, expect, it } from 'vitest'
import { Avatar, House, Lock, Menu, Tools, UserFilled } from '@element-plus/icons-vue'

import {
  applyNavigationPolicy,
  applyPermissionPolicy,
  mapRuntimeNavigation,
  themePolicyToTenantDefaults,
} from '@/systemData/runtime/navigation'
import type { NavigationRuntimeNodeDto, ThemePolicyDto } from '@/api/systemData/types'
import type { NavigationGroup } from '@/components/navigation/types'
import { getDefaultPcNavigationGroups } from '@/components/navigation/navigation'

const node = (overrides: Partial<NavigationRuntimeNodeDto>): NavigationRuntimeNodeDto => ({
  nodeNId: 'group-1',
  kind: 'Group',
  label: '系统',
  resourceNId: null,
  routeName: null,
  requiredPermissionNId: null,
  featureNId: null,
  iconKey: 'setting',
  displayOrder: 1,
  children: [],
  ...overrides,
})

describe('SystemData runtime navigation adapter', () => {
  it('keeps the existing platform icons when published defaults have no icon key', () => {
    const defaults = getDefaultPcNavigationGroups()
    const groups = mapRuntimeNavigation(
      defaults.map((group) =>
        node({
          nodeNId: `navigation.group.${group.id}`,
          label: `Renamed ${group.id}`,
          iconKey: null,
          children: group.items.map((item) =>
            node({
              nodeNId: `custom.${item.id}`,
              kind: 'Link',
              label: `Renamed ${item.id}`,
              routeName: item.routeName,
              iconKey: ' ',
            }),
          ),
        }),
      ),
    )

    for (const group of defaults) {
      const mapped = groups.find((item) => item.id === `navigation.group.${group.id}`)
      expect(mapped?.icon).toBe(group.icon)
      for (const item of group.items) {
        expect(mapped?.items.find((entry) => entry.routeName === item.routeName)?.icon).toBe(
          item.icon,
        )
      }
    }
  })

  it.each([
    [' UserFilled ', UserFilled],
    ['user-filled', UserFilled],
    ['Avatar', Avatar],
    ['Lock', Lock],
    ['tools', Tools],
    ['unknown-icon', Menu],
    ['constructor', Menu],
  ])('uses an explicit icon key before the route default: %s', (iconKey, icon) => {
    const groups = mapRuntimeNavigation([
      node({
        iconKey,
        children: [node({ kind: 'Link', routeName: 'identity-users', iconKey })],
      }),
    ])

    expect(groups[0]?.icon).toBe(icon)
    expect(groups[0]?.items[0]?.icon).toBe(icon)
  })

  it('only fills icons and preserves a moved menu without restoring default nodes', () => {
    const groups = mapRuntimeNavigation([
      node({
        nodeNId: 'custom-root',
        iconKey: null,
        children: [
          node({
            nodeNId: 'service-operations',
            children: [
              node({
                nodeNId: 'moved-theme',
                kind: 'Link',
                label: 'Custom theme',
                routeName: 'systemdata-themes',
                iconKey: null,
                requiredPermissionNId: 'systemdata.theme-policy.view',
              }),
            ],
          }),
        ],
      }),
    ])
    const themeIcon = getDefaultPcNavigationGroups()
      .flatMap((group) => group.items)
      .find((item) => item.routeName === 'systemdata-themes')?.icon

    expect(groups).toHaveLength(1)
    expect(groups[0]?.icon).toBe(Menu)
    expect(groups[0]?.items).toHaveLength(1)
    expect(groups[0]?.items[0]).toMatchObject({
      id: 'moved-theme',
      label: 'Custom theme',
      sectionId: 'service-operations',
      permission: 'systemdata.theme-policy.view',
    })
    expect(groups[0]?.items[0]?.icon).toBe(themeIcon)
  })

  it('intersects permissions, removes disabled features, and prunes empty groups', () => {
    const groups = mapRuntimeNavigation([
      node({
        children: [
          node({
            nodeNId: 'allowed',
            kind: 'Link',
            label: '可见',
            routeName: 'systemdata-services',
            requiredPermissionNId: 'systemdata.service-catalog.view',
            featureNId: 'feature-on',
          }),
          node({
            nodeNId: 'denied',
            kind: 'Link',
            label: '无权限',
            routeName: 'forbidden',
            requiredPermissionNId: 'systemdata.feature.view',
          }),
        ],
      }),
      node({ nodeNId: 'empty', label: '空分组', children: [] }),
    ])

    expect(
      applyNavigationPolicy(groups, ['systemdata.service-catalog.view'], new Set(['feature-on'])),
    ).toEqual([
      expect.objectContaining({
        id: 'group-1',
        items: [expect.objectContaining({ id: 'allowed', routeName: 'systemdata-services' })],
      }),
    ])
    const systemGroup = groups.find((group) => group.id === 'group-1')
    expect(systemGroup?.labelKey).toBe('')
    expect(systemGroup?.items[0]?.labelKey).toBe('')
  })

  it('does not create a runtime RouterLink for an unregistered future page route', () => {
    const groups = mapRuntimeNavigation([
      node({
        children: [
          node({
            nodeNId: 'future-page',
            kind: 'Link',
            label: '未来页面',
            routeName: 'future-page-not-registered',
          }),
        ],
      }),
    ])

    expect(groups[0]?.items).toEqual([])
  })

  it('filters anyPermissions and does not leak filtered children or empty groups', () => {
    const groups: NavigationGroup[] = [
      {
        id: 'platform',
        label: '平台',
        icon: House,
        items: [
          {
            id: 'terminal',
            label: '终端',
            routeName: 'terminal-preview',
            anyPermissions: ['platform.pda.view', 'platform.mobile.view'],
          },
          {
            id: 'platform-parent',
            label: '容器',
            routeName: 'pc-home',
            children: [
              {
                id: 'users',
                label: '用户',
                routeName: 'identity-users',
                permission: 'identity.user.view',
              },
            ],
          },
        ],
      },
    ]

    const filtered = applyNavigationPolicy(groups, ['platform.home.view'], new Set())

    expect(filtered).toEqual([
      expect.objectContaining({
        items: [expect.objectContaining({ id: 'platform-parent', children: [] })],
      }),
    ])
  })

  it('permission-only filtering preserves an already enabled runtime feature', () => {
    const groups: NavigationGroup[] = [
      {
        id: 'system',
        label: '系统',
        icon: House,
        items: [
          {
            id: 'enabled',
            label: '已启用',
            routeName: 'systemdata-services',
            permission: 'systemdata.service-catalog.view',
            featureNId: 'f1',
          },
          {
            id: 'denied',
            label: '无权限',
            routeName: 'systemdata-features',
            permission: 'systemdata.feature.view',
            featureNId: 'f1',
          },
        ],
      },
    ]

    expect(applyPermissionPolicy(groups, ['systemdata.service-catalog.view'])).toEqual([
      expect.objectContaining({
        items: [expect.objectContaining({ id: 'enabled', featureNId: 'f1' })],
      }),
    ])
  })
})

describe('SystemData theme adapter', () => {
  it('maps only the PF-01 allowed theme values into tenant defaults', () => {
    const policy: ThemePolicyDto = {
      policyRevision: 4,
      degraded: false,
      allowedPalettes: ['technology-blue'],
      allowedModes: ['dark'],
      allowedPcDensities: ['compact'],
      defaultPalette: 'technology-blue',
      defaultMode: 'dark',
      defaultPcDensity: 'compact',
    }

    expect(themePolicyToTenantDefaults(policy)).toEqual({
      palette: 'technology-blue',
      mode: 'dark',
      density: 'compact',
    })
  })
})
