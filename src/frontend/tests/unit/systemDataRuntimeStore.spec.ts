import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, describe, expect, it, vi } from 'vitest'

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

  it('keeps the real static navigation when SystemData has no published snapshot', async () => {
    registerSystemDataRuntimeApi({
      ...api,
      getNavigation: async () => ({
        kind: 'updated',
        etag: '"nav-unconfigured"',
        data: { revision: 2, configured: false, degraded: false, nodes: [] },
      }),
    })
    const store = useSystemDataRuntimeStore()
    store.setPermissions(['platform.home.view'])

    await store.refresh('Pc')

    expect(store.navigationGroups).toEqual([
      expect.objectContaining({
        id: 'workspace',
        items: [expect.objectContaining({ id: 'pc-home', routeName: 'pc-home' })],
      }),
    ])
  })

  it('waits for the server revision returned after publish before accepting runtime navigation', async () => {
    let releaseFirst!: () => void
    const firstResponse = new Promise<{
      kind: 'updated'
      etag: string
      data: { revision: number; degraded: boolean; nodes: [] }
    }>((resolve) => {
      releaseFirst = () =>
        resolve({
          kind: 'updated',
          etag: '"nav-1"',
          data: { revision: 1, degraded: false, nodes: [] },
        })
    })
    let navigationCalls = 0
    registerSystemDataRuntimeApi({
      ...api,
      getNavigation: vi.fn(() => {
        navigationCalls += 1
        return navigationCalls === 1
          ? firstResponse
          : Promise.resolve({
              kind: 'updated' as const,
              etag: '"nav-3"',
              data: { revision: 3, degraded: false, nodes: [] },
            })
      }),
    })
    const store = useSystemDataRuntimeStore()

    const firstRefresh = store.refresh('Pc')
    const targetRefresh = store.refresh('Pc', 3)
    releaseFirst()
    expect(await firstRefresh).toBe(true)
    expect(await targetRefresh).toBe(true)
    expect(navigationCalls).toBe(2)
    expect(store.navigationRevision).toBe(3)
  })
})
