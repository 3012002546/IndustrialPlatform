import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, describe, expect, it } from 'vitest'

import { registerSystemDataRuntimeApi } from '@/api/systemData/runtimeRegistry'
import type { SystemDataRuntimeApi } from '@/api/systemData/runtimeApi'
import { useSystemDataRuntimeStore } from '@/stores/systemData/runtimeStore'
import { createSystemDataTenantUiDefaultsSource } from '@/systemData/runtime/themeSource'

describe('SystemData runtime coordinator', () => {
  beforeEach(() => setActivePinia(createPinia()))

  it('shares one runtime refresh when theme binding and runtime bootstrap start together', async () => {
    const calls = { navigation: 0, features: 0, theme: 0 }
    const api: SystemDataRuntimeApi = {
      getNavigation: async () => {
        calls.navigation += 1
        return {
          kind: 'updated',
          etag: 'navigation',
          data: { revision: 1, configured: false, degraded: false, nodes: [] },
        }
      },
      getFeatures: async () => {
        calls.features += 1
        return {
          kind: 'updated',
          etag: 'features',
          data: { revision: 1, configured: false, degraded: false, items: [] },
        }
      },
      getThemePolicy: async () => {
        calls.theme += 1
        return {
          kind: 'updated',
          etag: 'theme',
          data: {
            policyRevision: 1,
            configured: true,
            degraded: false,
            allowedPalettes: ['industrial-cyan'],
            allowedModes: ['light'],
            allowedPcDensities: ['comfortable'],
            defaultPalette: 'industrial-cyan',
            defaultMode: 'light',
            defaultPcDensity: 'comfortable',
          },
        }
      },
    }
    registerSystemDataRuntimeApi(api)
    const store = useSystemDataRuntimeStore()
    const source = createSystemDataTenantUiDefaultsSource(store)

    const [first, second] = await Promise.all([
      source.load({ tenantId: 'tenant-a', userId: 'user-a' }),
      source.load({ tenantId: 'tenant-a', userId: 'user-a' }),
    ])

    expect(first).toEqual({ palette: 'industrial-cyan', mode: 'light', density: 'comfortable' })
    expect(second).toEqual(first)
    expect(calls).toEqual({ navigation: 1, features: 1, theme: 1 })
  })
})
