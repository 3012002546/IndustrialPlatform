<script setup lang="ts">
import { computed } from 'vue'
import { RouterLink, RouterView, useRoute } from 'vue-router'

import MockModeBanner from '@/components/base/MockModeBanner.vue'
import ThemeControl from '@/components/theme/ThemeControl.vue'
import { resolveActiveTerminal, type TerminalType } from '@/device'
import { ROUTE_NAMES } from '@/router/routes'
import { useDeviceStore } from '@/stores/deviceStore'

const TERMINAL_LABELS: Record<TerminalType, string> = {
  pc: 'PC',
  pda: 'PDA',
  mobile: 'Mobile',
}

interface MobileTab {
  label: string
  routeName: string
  icon: 'home' | 'user'
}

/** 第一批底部导航只有「首页」「我的」,不出现任务/消息/审批等假入口(§17)。 */
const tabs: readonly MobileTab[] = [
  { label: '首页', routeName: ROUTE_NAMES.mobileHome, icon: 'home' },
  { label: '我的', routeName: ROUTE_NAMES.mobileMy, icon: 'user' },
]

const route = useRoute()
const deviceStore = useDeviceStore()

// 终端文案单事实源:显式路由 meta.terminal 优先,无显式路由回退设备建议(§7.11)。
const terminalLabel = computed(() => {
  const active = resolveActiveTerminal(route.meta.terminal, deviceStore.terminal)
  return TERMINAL_LABELS[active] ?? active
})

/** 当前 Tab 高亮:按路由名精确匹配。 */
function isActive(tab: MobileTab): boolean {
  return route.name === tab.routeName
}
</script>

<template>
  <div class="ip-mobile-layout">
    <a class="ip-mobile-skip-link" href="#main-content">跳到主内容</a>

    <header class="ip-mobile-header">
      <div class="ip-mobile-header__brand">Industrial Platform</div>
      <div class="ip-mobile-header__right">
        <span class="ip-mobile-terminal" data-testid="terminal-info">
          <svg
            width="14"
            height="14"
            viewBox="0 0 16 16"
            fill="none"
            aria-hidden="true"
            focusable="false"
          >
            <rect x="1.5" y="3" width="13" height="10" rx="1.5" stroke="currentColor" />
            <path
              d="M5 6l2 2-2 2"
              stroke="currentColor"
              stroke-linecap="round"
              stroke-linejoin="round"
            />
          </svg>
          终端 {{ terminalLabel }}
        </span>
        <MockModeBanner class="ip-mobile-mock" label="Mock" />

        <ThemeControl terminal="mobile" />
      </div>
    </header>

    <main id="main-content" class="ip-mobile-main" tabindex="-1">
      <RouterView />
    </main>

    <nav class="ip-mobile-nav" aria-label="底部导航">
      <template v-for="tab in tabs" :key="tab.routeName">
        <RouterLink
          :to="{ name: tab.routeName }"
          class="ip-mobile-nav-item"
          :class="{ 'ip-mobile-nav-item--active': isActive(tab) }"
          data-testid="nav-item"
        >
          <svg
            v-if="tab.icon === 'home'"
            width="20"
            height="20"
            viewBox="0 0 24 24"
            fill="none"
            aria-hidden="true"
            focusable="false"
          >
            <path
              d="M3.5 10.5L12 3l8.5 7.5V20a1 1 0 0 1-1 1h-5v-6h-5v6h-5a1 1 0 0 1-1-1v-9.5z"
              stroke="currentColor"
              stroke-width="2"
              stroke-linecap="round"
              stroke-linejoin="round"
            />
          </svg>
          <svg
            v-else
            width="20"
            height="20"
            viewBox="0 0 24 24"
            fill="none"
            aria-hidden="true"
            focusable="false"
          >
            <circle cx="12" cy="8" r="3.5" stroke="currentColor" stroke-width="2" />
            <path
              d="M4.5 20a7.5 7.5 0 0 1 15 0"
              stroke="currentColor"
              stroke-width="2"
              stroke-linecap="round"
            />
          </svg>
          <span>{{ tab.label }}</span>
        </RouterLink>
      </template>
    </nav>
  </div>
</template>

<style scoped>
.ip-mobile-layout {
  display: flex;
  flex-direction: column;
  height: 100vh;
}

.ip-mobile-header {
  display: flex;
  flex: 0 0 auto;
  align-items: center;
  justify-content: space-between;
  gap: var(--ip-space-2);
  height: var(--ip-mobile-header-height);
  padding: 0 var(--ip-space-3);
  background: var(--ip-color-bg-container);
  border-bottom: 1px solid var(--ip-color-border);
}

.ip-mobile-header__brand {
  overflow: hidden;
  color: var(--ip-color-text-primary);
  font-size: var(--ip-font-size-md);
  font-weight: 600;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.ip-mobile-header__right {
  display: flex;
  flex: 0 0 auto;
  align-items: center;
  gap: var(--ip-space-2);
}

.ip-mobile-terminal {
  display: inline-flex;
  align-items: center;
  gap: var(--ip-space-1);
  color: var(--ip-color-text-secondary);
  font-size: var(--ip-font-size-xs);
  white-space: nowrap;
}

.ip-mobile-mock {
  font-size: var(--ip-font-size-xs);
}

.ip-mobile-main {
  flex: 1 1 auto;
  min-height: 0;
  overflow: auto;
  background: var(--ip-color-bg-page);
}

/* 底部导航 + 安全区域适配(§17:必须适配 env(safe-area-inset-bottom)) */
.ip-mobile-nav {
  display: flex;
  flex: 0 0 auto;
  padding-bottom: var(--ip-safe-area-bottom);
  background: var(--ip-color-bg-container);
  border-top: 1px solid var(--ip-color-border);
}

/* 44px 触控目标(§17:最小 44×44),safe-area 高度叠加在触控目标之上。 */
.ip-mobile-nav-item {
  display: flex;
  flex: 1 1 0;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  gap: 2px;
  min-height: var(--ip-touch-min-size-mobile);
  padding: 0 var(--ip-space-1);
  color: var(--ip-color-text-secondary);
  font-size: var(--ip-font-size-xs);
  text-decoration: none;
}

.ip-mobile-nav-item--active {
  color: var(--ip-color-primary);
  font-weight: 500;
}

.ip-mobile-nav-item:hover {
  color: var(--ip-color-text-primary);
}

.ip-mobile-nav-item--active:hover {
  color: var(--ip-color-primary);
}

/* 跳到主内容入口(§13.2):视觉隐藏,聚焦时可见且置于最前。 */
.ip-mobile-skip-link {
  position: absolute;
  z-index: 100;
  top: -100px;
  left: var(--ip-space-4);
  padding: var(--ip-space-2) var(--ip-space-4);
  background: var(--ip-color-bg-container);
  border: 1px solid var(--ip-color-primary);
  border-radius: var(--ip-radius-md);
  color: var(--ip-color-primary);
  font-size: var(--ip-font-size-md);
  text-decoration: none;
  transition: top 150ms ease;
}

.ip-mobile-skip-link:focus {
  top: var(--ip-space-2);
}

@media (prefers-reduced-motion: reduce) {
  .ip-mobile-skip-link {
    transition: none;
  }
}
</style>
