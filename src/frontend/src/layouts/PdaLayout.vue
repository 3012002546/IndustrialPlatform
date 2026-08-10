<script setup lang="ts">
import { computed } from 'vue'
import { RouterView, useRouter } from 'vue-router'

import MockModeBanner from '@/components/base/MockModeBanner.vue'
import type { TerminalType } from '@/device/types'
import { ROUTE_NAMES } from '@/router/routes'
import { useAuthStore } from '@/stores/authStore'
import { useDeviceStore } from '@/stores/deviceStore'

const TERMINAL_LABELS: Record<TerminalType, string> = {
  pc: 'PC',
  pda: 'PDA',
  mobile: 'Mobile',
}

const router = useRouter()
const authStore = useAuthStore()
const deviceStore = useDeviceStore()

const displayName = computed(() => authStore.user?.displayName ?? '')
const terminalLabel = computed(() => TERMINAL_LABELS[deviceStore.terminal] ?? deviceStore.terminal)

function goHome(): void {
  void router.push({ name: ROUTE_NAMES.pdaHome })
}

/**
 * 是否有可返回的上一条历史(与 NotFoundPage 同一模式):
 * 生产(web history)在首个 entry 时 back 为 null;memory history 的 state 不携带
 * back 字段,取值恒为 undefined,一并按“无历史”回落首页,避免调用失效的 back()。
 */
function canGoBack(): boolean {
  return router.options.history.state.back != null
}

function goBack(): void {
  if (canGoBack()) {
    void router.back()
  } else {
    goHome()
  }
}

async function handleLogout(): Promise<void> {
  try {
    await authStore.logout()
  } catch {
    // logout 内部已吞掉网关失败;此处兜底确保仍能回登录页
  } finally {
    await router.push({ name: ROUTE_NAMES.login })
  }
}
</script>

<template>
  <div class="ip-pda-layout">
    <a class="ip-pda-skip-link" href="#main-content">跳到主内容</a>

    <header class="ip-pda-header">
      <div class="ip-pda-header__left">
        <button
          type="button"
          class="ip-pda-icon-button"
          data-testid="back-button"
          aria-label="返回"
          @click="goBack"
        >
          <svg
            width="22"
            height="22"
            viewBox="0 0 24 24"
            fill="none"
            aria-hidden="true"
            focusable="false"
          >
            <path
              d="M15 5l-7 7 7 7"
              stroke="currentColor"
              stroke-width="2"
              stroke-linecap="round"
              stroke-linejoin="round"
            />
          </svg>
        </button>
        <button
          type="button"
          class="ip-pda-icon-button"
          data-testid="home-button"
          aria-label="首页"
          @click="goHome"
        >
          <svg
            width="22"
            height="22"
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
        </button>
      </div>

      <div class="ip-pda-header__center">
        <span class="ip-pda-user" data-testid="pda-user">{{ displayName || '未登录' }}</span>
      </div>

      <div class="ip-pda-header__right">
        <span class="ip-pda-terminal" data-testid="terminal-info">
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

        <MockModeBanner class="ip-pda-mock" label="Mock" />

        <button
          type="button"
          class="ip-pda-icon-button"
          data-testid="logout-button"
          aria-label="退出登录"
          @click="handleLogout"
        >
          <svg
            width="22"
            height="22"
            viewBox="0 0 24 24"
            fill="none"
            aria-hidden="true"
            focusable="false"
          >
            <path
              d="M15 12H4m0 0l3-3m-3 3l3 3M10 4h8a1 1 0 0 1 1 1v14a1 1 0 0 1-1 1h-8"
              stroke="currentColor"
              stroke-width="2"
              stroke-linecap="round"
              stroke-linejoin="round"
            />
          </svg>
        </button>
      </div>
    </header>

    <main id="main-content" class="ip-pda-main" tabindex="-1">
      <RouterView />
    </main>
  </div>
</template>

<style scoped>
.ip-pda-layout {
  display: flex;
  flex-direction: column;
  height: 100vh;
}

.ip-pda-header {
  display: flex;
  flex: 0 0 auto;
  align-items: center;
  justify-content: space-between;
  gap: var(--ip-space-2);
  height: var(--ip-pda-header-height);
  padding: 0 var(--ip-space-2);
  background: var(--ip-color-bg-container);
  border-bottom: 1px solid var(--ip-color-border);
}

.ip-pda-header__left,
.ip-pda-header__right {
  display: flex;
  flex: 0 0 auto;
  align-items: center;
  gap: var(--ip-space-1);
}

.ip-pda-header__center {
  flex: 1 1 auto;
  min-width: 0;
  text-align: center;
}

/* 48px 触控目标(§16:最小 48×48),顶栏高度即触控目标高度。 */
.ip-pda-icon-button {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: var(--ip-touch-min-size);
  height: var(--ip-touch-min-size);
  padding: 0;
  color: var(--ip-color-text-secondary);
  background: transparent;
  border: 0;
  border-radius: var(--ip-radius-md);
  cursor: pointer;
}

.ip-pda-icon-button:hover {
  background: var(--ip-color-bg-muted);
  color: var(--ip-color-text-primary);
}

.ip-pda-icon-button:active {
  background: var(--ip-color-bg-muted);
}

.ip-pda-user {
  display: inline-block;
  max-width: 100%;
  overflow: hidden;
  color: var(--ip-color-text-primary);
  font-size: var(--ip-font-size-md);
  font-weight: 500;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.ip-pda-terminal {
  display: inline-flex;
  align-items: center;
  gap: var(--ip-space-1);
  color: var(--ip-color-text-secondary);
  font-size: var(--ip-font-size-xs);
  white-space: nowrap;
}

.ip-pda-mock {
  font-size: var(--ip-font-size-xs);
}

.ip-pda-main {
  flex: 1 1 auto;
  min-height: 0;
  overflow: auto;
  background: var(--ip-color-bg-page);
}

/* 跳到主内容入口(§13.2):视觉隐藏,聚焦时可见且置于最前。 */
.ip-pda-skip-link {
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

.ip-pda-skip-link:focus {
  top: var(--ip-space-2);
}

@media (prefers-reduced-motion: reduce) {
  .ip-pda-skip-link {
    transition: none;
  }
}
</style>
