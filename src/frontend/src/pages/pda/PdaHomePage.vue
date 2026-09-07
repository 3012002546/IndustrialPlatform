<script setup lang="ts">
import { computed } from 'vue'
import { useRoute } from 'vue-router'

import AppEmptyState from '@/components/base/AppEmptyState.vue'
import AppPage from '@/components/base/AppPage.vue'
import TimeGreetingHeader from '@/components/home/TimeGreetingHeader.vue'
import TerminalFeatureMenu from '@/components/home/TerminalFeatureMenu.vue'
import { loadRuntimeConfig } from '@/config/runtimeConfig'
import { resolveActiveTerminal, type TerminalType } from '@/device'
import { systemDataPageCopy } from '@/localization/systemData'
import { usePlatformLocale } from '@/localization/localeContext'
import { PERMISSIONS } from '@/permissions'
import { useAuthStore } from '@/stores/authStore'
import { useDeviceStore } from '@/stores/deviceStore'

const TERMINAL_LABELS: Record<TerminalType, string> = {
  pc: 'PC',
  pda: 'PDA',
  mobile: 'Mobile',
}

const route = useRoute()
const authStore = useAuthStore()
const deviceStore = useDeviceStore()
const authMode = loadRuntimeConfig().authMode
const locale = usePlatformLocale()
const copy = computed(() => systemDataPageCopy(locale.value, 'terminalFeatureMenu'))
const hasFeature = computed(() =>
  authStore.hasPermission(PERMISSIONS.systemDataFileRead) ||
  authStore.hasPermission(PERMISSIONS.systemDataNotificationInboxRead),
)

const displayName = computed(() => authStore.user?.displayName ?? '')
// 终端文案单事实源:显式路由 meta.terminal 优先,无显式路由回退设备建议(§7.11)。
const terminalLabel = computed(() => {
  const active = resolveActiveTerminal(route.meta.terminal, deviceStore.terminal)
  return TERMINAL_LABELS[active] ?? active
})
const authModeLabel = computed(() => (authMode === 'mock' ? copy.value.mockMode : copy.value.httpMode))
</script>

<template>
  <AppPage>
    <TimeGreetingHeader
      terminal="pda"
      :display-name="displayName"
      :description="copy.pdaDescription"
    />

    <dl class="pda-home__meta">
      <div class="pda-home__meta-item">
        <dt>{{ copy.currentTerminal }}</dt>
        <dd data-testid="terminal">{{ terminalLabel }}</dd>
      </div>
      <div class="pda-home__meta-item">
        <dt>{{ copy.authMode }}</dt>
        <dd data-testid="auth-mode">{{ authModeLabel }}</dd>
      </div>
      <div class="pda-home__meta-item">
        <dt>{{ copy.dataSource }}</dt>
        <dd data-testid="data-source">{{ copy.demoData }}</dd>
      </div>
    </dl>

    <TerminalFeatureMenu terminal="pda" />
    <AppEmptyState v-if="!hasFeature" :title="copy.pdaEmptyTitle" :description="copy.pdaEmptyDescription" />
  </AppPage>
</template>

<style scoped>
.pda-home__meta {
  display: flex;
  flex-wrap: wrap;
  gap: var(--ip-space-6);
  margin: 0;
  padding: var(--ip-space-4);
  background: var(--ip-color-bg-container);
  border: 1px solid var(--ip-color-border);
  border-radius: var(--ip-radius-lg);
}

.pda-home__meta-item {
  display: flex;
  flex-direction: column;
  gap: var(--ip-space-1);
}

.pda-home__meta-item dt {
  font-size: var(--ip-font-size-xs);
  color: var(--ip-color-text-secondary);
}

.pda-home__meta-item dd {
  margin: 0;
  font-size: var(--ip-font-size-md);
  font-weight: 500;
  color: var(--ip-color-text-primary);
}
</style>
