import { describe, expect, it } from 'vitest'

import {
  applyNavigationPolicy,
  mapRuntimeNavigation,
  themePolicyToTenantDefaults,
} from '@/systemData/runtime/navigation'
import type { NavigationRuntimeNodeDto, ThemePolicyDto } from '@/api/systemData/types'

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
    expect(groups[0]?.labelKey).toBe('shell.navigation.group.group-1')
    expect(groups[0]?.items[0]?.labelKey).toBe('shell.navigation.item.allowed')
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
