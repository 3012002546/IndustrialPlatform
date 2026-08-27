import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, describe, expect, it } from 'vitest'

import { registerSystemDataRuntimeApi } from '@/api/systemData/runtimeRegistry'
import type { SystemDataRuntimeApi } from '@/api/systemData/runtimeApi'
import { useSystemDataRuntimeStore } from '@/stores/systemData/runtimeStore'

const api: SystemDataRuntimeApi = {
  getNavigation: async () => ({
    kind: 'updated',
    etag: '"nav-1"',
    data: {
      revision: 1,
      degraded: false,
      nodes: [
        {
          nodeNId: 'system',
          kind: 'Group',
          label: '系统',
          resourceNId: null,
          routeName: null,
          requiredPermissionNId: null,
          featureNId: null,
          iconKey: 'setting',
          displayOrder: 1,
          children: [
            {
              nodeNId: 'services',
              kind: 'Link',
              label: '服务目录',
              resourceNId: 'service-catalog',
              routeName: 'systemdata-services',
              requiredPermissionNId: 'systemdata.service-catalog.view',
              featureNId: 'catalog',
              iconKey: 'tools',
              displayOrder: 1,
              children: [],
            },
          ],
        },
      ],
    },
  }),
  getFeatures: async () => ({
    kind: 'updated',
    etag: '"features-1"',
    data: { revision: 1, degraded: false, items: [{ featureNId: 'catalog', enabled: true }] },
  }),
  getThemePolicy: async () => ({
    kind: 'updated',
    etag: '"theme-1"',
    data: {
      policyRevision: 1,
      degraded: false,
      allowedPalettes: ['industrial-cyan'],
      allowedModes: ['system'],
      allowedPcDensities: ['comfortable'],
      defaultPalette: 'industrial-cyan',
      defaultMode: 'system',
      defaultPcDensity: 'comfortable',
    },
  }),
}

describe('SystemData runtime store', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    registerSystemDataRuntimeApi(api)
  })

  it('refreshes snapshots and exposes only authorized, enabled navigation', async () => {
    const store = useSystemDataRuntimeStore()
    store.setPermissions(['systemdata.service-catalog.view'])

    await store.refresh('Pc')

    expect(store.navigationGroups).toHaveLength(1)
    expect(store.navigationGroups[0]?.items[0]?.routeName).toBe('systemdata-services')
    expect(store.degraded).toBe(false)
  })
})
