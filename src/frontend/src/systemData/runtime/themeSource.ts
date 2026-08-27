import type { SystemDataRuntimeApi } from '@/api/systemData/runtimeApi'
import type { ThemePolicyDto } from '@/api/systemData/types'
import type { TenantUiDefaultsSource } from '@/stores/themeStore'
import type { UserUiScope } from '@/theme'

import { themePolicyToTenantDefaults } from './navigation'

const MAX_STALE_MS = 5 * 60 * 1000

/** TenantUiDefaultsSource backed by SystemData; failures fail soft to PF-01 defaults. */
export function createSystemDataTenantUiDefaultsSource(
  api: SystemDataRuntimeApi,
): TenantUiDefaultsSource {
  let etag: string | undefined
  let cached: { data: ThemePolicyDto; verifiedAt: number } | null = null

  return {
    async load(_scope: UserUiScope) {
      void _scope
      const now = Date.now()
      try {
        const result = await api.getThemePolicy(etag)
        if (result.kind === 'updated') {
          etag = result.etag
          cached = { data: result.data, verifiedAt: now }
          return themePolicyToTenantDefaults(result.data)
        }
        if (cached !== null) {
          cached = { ...cached, verifiedAt: now }
          return themePolicyToTenantDefaults(cached.data)
        }
      } catch {
        if (cached !== null && now - cached.verifiedAt <= MAX_STALE_MS) {
          return themePolicyToTenantDefaults(cached.data)
        }
      }
      return {}
    },
  }
}
