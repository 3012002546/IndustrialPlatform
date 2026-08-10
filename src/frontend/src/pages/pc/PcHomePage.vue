<script setup lang="ts">
import { computed } from 'vue'

import AppEmptyState from '@/components/base/AppEmptyState.vue'
import AppPage from '@/components/base/AppPage.vue'
import { loadRuntimeConfig } from '@/config/runtimeConfig'
import type { TerminalType } from '@/device/types'
import { useAuthStore } from '@/stores/authStore'
import { useDeviceStore } from '@/stores/deviceStore'

const TERMINAL_LABELS: Record<TerminalType, string> = {
  pc: 'PC',
  pda: 'PDA',
  mobile: 'Mobile',
}

const authStore = useAuthStore()
const deviceStore = useDeviceStore()
const authMode = loadRuntimeConfig().authMode

const displayName = computed(() => authStore.user?.displayName ?? '')
const terminalLabel = computed(() => TERMINAL_LABELS[deviceStore.terminal] ?? deviceStore.terminal)
const authModeLabel = computed(() => (authMode === 'mock' ? 'Mock(演示数据)' : 'HTTP(真实服务)'))
</script>

<template>
  <AppPage title="首页" description="PC 工作台">
    <p class="pc-home__welcome" data-testid="welcome">欢迎,{{ displayName }}</p>

    <dl class="pc-home__meta">
      <div class="pc-home__meta-item">
        <dt>当前终端</dt>
        <dd data-testid="terminal">{{ terminalLabel }}</dd>
      </div>
      <div class="pc-home__meta-item">
        <dt>认证模式</dt>
        <dd data-testid="auth-mode">{{ authModeLabel }}</dd>
      </div>
      <div class="pc-home__meta-item">
        <dt>数据来源</dt>
        <dd data-testid="data-source">Mock 演示数据</dd>
      </div>
    </dl>

    <AppEmptyState
      title="业务指标将在后续阶段接入"
      description="当前不展示产量、OEE、告警等任何生产指标;业务阶段接入后由真实服务提供。"
    />
  </AppPage>
</template>

<style scoped>
.pc-home__welcome {
  margin: 0;
  font-size: var(--ip-font-size-lg);
  font-weight: 500;
  color: var(--ip-color-text-primary);
}

.pc-home__meta {
  display: flex;
  flex-wrap: wrap;
  gap: var(--ip-space-6);
  margin: 0;
  padding: var(--ip-space-4);
  background: var(--ip-color-bg-container);
  border: 1px solid var(--ip-color-border);
  border-radius: var(--ip-radius-lg);
}

.pc-home__meta-item {
  display: flex;
  flex-direction: column;
  gap: var(--ip-space-1);
}

.pc-home__meta-item dt {
  font-size: var(--ip-font-size-xs);
  color: var(--ip-color-text-secondary);
}

.pc-home__meta-item dd {
  margin: 0;
  font-size: var(--ip-font-size-md);
  font-weight: 500;
  color: var(--ip-color-text-primary);
}
</style>
