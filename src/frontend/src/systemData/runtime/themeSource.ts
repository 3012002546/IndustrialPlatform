import { isRef, type Ref } from 'vue'
import type { ThemePolicyDto } from '@/api/systemData/types'
import type { SystemDataRuntimeStoreState } from '@/stores/systemData/runtimeStore'
import type { TenantUiDefaultsSource } from '@/stores/themeStore'
import type { UserUiScope } from '@/theme'

import { themePolicyToTenantDefaults } from './navigation'

/** Reads the already-coordinated runtime store; failures fail soft to PF-01 defaults. */
export function createSystemDataTenantUiDefaultsSource(runtimeStore: {
  refresh: SystemDataRuntimeStoreState['refresh']
  themePolicy: ThemePolicyDto | null | Ref<ThemePolicyDto | null>
}): TenantUiDefaultsSource {
  return {
    async load(_scope: UserUiScope) {
      void _scope
      try {
        await runtimeStore.refresh('Pc')
        const policy = isRef(runtimeStore.themePolicy)
          ? runtimeStore.themePolicy.value
          : runtimeStore.themePolicy
        return policy === null ? {} : themePolicyToTenantDefaults(policy)
      } catch {
        return {}
      }
    },
  }
}
