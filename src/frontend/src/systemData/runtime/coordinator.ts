import type { Pinia } from 'pinia'
import type { Plugin } from 'vue'
import { watch } from 'vue'

import { useAuthStore } from '@/stores/authStore'
import { useDeviceStore } from '@/stores/deviceStore'
import { useSystemDataRuntimeStore } from '@/stores/systemData/runtimeStore'

function toApiTerminal(terminal: 'pc' | 'pda' | 'mobile'): 'Pc' | 'Pda' | 'Mobile' {
  return terminal === 'pc' ? 'Pc' : terminal === 'pda' ? 'Pda' : 'Mobile'
}

/** Installs login/permission/terminal/focus/5-minute runtime snapshot triggers. */
export function createSystemDataRuntimePlugin(pinia: Pinia): Plugin {
  return {
    install() {
      const authStore = useAuthStore(pinia)
      const deviceStore = useDeviceStore(pinia)
      const runtimeStore = useSystemDataRuntimeStore(pinia)

      const refreshIfAuthenticated = (): void => {
        const user = authStore.user
        if (!authStore.isAuthenticated || user === null) {
          runtimeStore.clear()
          return
        }
        runtimeStore.setPermissions(user.permissions)
        void runtimeStore.refresh(toApiTerminal(deviceStore.terminal))
      }

      watch(() => authStore.user?.userId, refreshIfAuthenticated, { immediate: true })
      watch(() => authStore.user?.permissions.join('\u0000'), refreshIfAuthenticated)
      watch(() => deviceStore.terminal, refreshIfAuthenticated)

      if (typeof window !== 'undefined') {
        const onFocus = () => refreshIfAuthenticated()
        window.addEventListener('focus', onFocus)
        window.setInterval(refreshIfAuthenticated, 5 * 60 * 1000)
      }
    },
  }
}
