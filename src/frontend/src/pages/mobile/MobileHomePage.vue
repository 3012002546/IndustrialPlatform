<script setup lang="ts">
import { computed } from 'vue'
import { useRoute } from 'vue-router'

import AppEmptyState from '@/components/base/AppEmptyState.vue'
import AppPage from '@/components/base/AppPage.vue'
import { loadRuntimeConfig } from '@/config/runtimeConfig'
import { resolveActiveTerminal, type TerminalType } from '@/device'
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

const displayName = computed(() => authStore.user?.displayName ?? '')
// 终端文案单事实源:显式路由 meta.terminal 优先,无显式路由回退设备建议(§7.11)。
const terminalLabel = computed(() => {
  const active = resolveActiveTerminal(route.meta.terminal, deviceStore.terminal)
  return TERMINAL_LABELS[active] ?? active
})
const authModeLabel = computed(() => (authMode === 'mock' ? 'Mock(演示数据)' : 'HTTP(真实服务)'))
</script>

<template>
  <AppPage title="首页" description="Mobile 工作台">
    <p class="mobile-home__welcome" data-testid="welcome">欢迎,{{ displayName }}</p>

    <dl class="mobile-home__meta">
      <div class="mobile-home__meta-item">
        <dt>当前终端</dt>
        <dd data-testid="terminal">{{ terminalLabel }}</dd>
      </div>
      <div class="mobile-home__meta-item">
        <dt>认证模式</dt>
        <dd data-testid="auth-mode">{{ authModeLabel }}</dd>
      </div>
      <div class="mobile-home__meta-item">
        <dt>数据来源</dt>
        <dd data-testid="data-source">Mock 演示数据</dd>
      </div>
    </dl>

    <AppEmptyState
      title="业务功能将在后续阶段接入"
      description="任务、消息、审批等业务功能将在后续阶段接入;当前不提供不可用的功能入口。"
    />
  </AppPage>
</template>

<style scoped>
.mobile-home__welcome {
  margin: 0;
  font-size: var(--ip-font-size-lg);
  font-weight: 500;
  color: var(--ip-color-text-primary);
}

.mobile-home__meta {
  display: flex;
  flex-wrap: wrap;
  gap: var(--ip-space-6);
  margin: 0;
  padding: var(--ip-space-4);
  background: var(--ip-color-bg-container);
  border: 1px solid var(--ip-color-border);
  border-radius: var(--ip-radius-lg);
}

.mobile-home__meta-item {
  display: flex;
  flex-direction: column;
  gap: var(--ip-space-1);
}

.mobile-home__meta-item dt {
  font-size: var(--ip-font-size-xs);
  color: var(--ip-color-text-secondary);
}

.mobile-home__meta-item dd {
  margin: 0;
  font-size: var(--ip-font-size-md);
  font-weight: 500;
  color: var(--ip-color-text-primary);
}
</style>
