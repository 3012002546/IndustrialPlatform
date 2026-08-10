<script setup lang="ts">
import { computed, ref } from 'vue'
import { RouterView, useRouter } from 'vue-router'
import { ElDropdown, ElDropdownItem, ElDropdownMenu } from 'element-plus'

import MockModeBanner from '@/components/base/MockModeBanner.vue'
import { pcNavigationItems } from '@/components/navigation/navigation'
import PcNavMenu from '@/components/navigation/PcNavMenu.vue'
import type { TerminalType } from '@/device/types'
import { useAuthStore } from '@/stores/authStore'
import { useDeviceStore } from '@/stores/deviceStore'

/** 侧栏折叠状态持久化键(§14:刷新保持)。 */
const PC_SIDEBAR_COLLAPSED_KEY = 'industrial-platform.pc.sidebar.collapsed.v1'

const TERMINAL_LABELS: Record<TerminalType, string> = {
  pc: 'PC',
  pda: 'PDA',
  mobile: 'Mobile',
}

const router = useRouter()
const authStore = useAuthStore()
const deviceStore = useDeviceStore()

function readCollapsed(): boolean {
  try {
    return globalThis.localStorage.getItem(PC_SIDEBAR_COLLAPSED_KEY) === '1'
  } catch {
    return false
  }
}

const collapsed = ref(readCollapsed())

function toggleCollapsed(): void {
  collapsed.value = !collapsed.value
  try {
    globalThis.localStorage.setItem(PC_SIDEBAR_COLLAPSED_KEY, collapsed.value ? '1' : '0')
  } catch {
    // 存储不可用(如隐私模式)不阻塞布局交互
  }
}

const displayName = computed(() => authStore.user?.displayName ?? '')
const terminalLabel = computed(() => TERMINAL_LABELS[deviceStore.terminal] ?? deviceStore.terminal)

/** 用户菜单命令:目前仅「退出登录」。退出后总是回登录页(§14.1)。 */
function onUserCommand(command: unknown): void {
  if (command !== 'logout') return
  void handleLogout()
}

async function handleLogout(): Promise<void> {
  try {
    await authStore.logout()
  } catch {
    // logout 内部已吞掉网关失败;此处兜底确保仍能回登录页
  } finally {
    await router.push({ name: 'login' })
  }
}
</script>

<template>
  <div class="ip-pc-layout">
    <a class="ip-pc-skip-link" href="#main-content">跳到主内容</a>

    <header class="ip-pc-header">
      <div class="ip-pc-header__left">
        <button
          type="button"
          class="ip-pc-collapse"
          data-testid="sidebar-toggle"
          :aria-expanded="!collapsed"
          aria-controls="ip-pc-sidebar"
          @click="toggleCollapsed"
        >
          <svg
            width="16"
            height="16"
            viewBox="0 0 16 16"
            fill="none"
            aria-hidden="true"
            focusable="false"
          >
            <rect x="2" y="3" width="12" height="1.5" rx="0.75" fill="currentColor" />
            <rect x="2" y="7.25" width="12" height="1.5" rx="0.75" fill="currentColor" />
            <rect x="2" y="11.5" width="8" height="1.5" rx="0.75" fill="currentColor" />
          </svg>
          <span class="ip-pc-visually-hidden">{{ collapsed ? '展开侧栏' : '折叠侧栏' }}</span>
        </button>
        <span class="ip-pc-brand">Industrial Platform</span>
      </div>

      <div class="ip-pc-header__right">
        <span class="ip-pc-terminal" data-testid="terminal-info">
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

        <MockModeBanner />

        <ElDropdown trigger="click" @command="onUserCommand">
          <button type="button" class="ip-pc-user" data-testid="user-menu" aria-label="用户菜单">
            <svg
              width="14"
              height="14"
              viewBox="0 0 16 16"
              fill="none"
              aria-hidden="true"
              focusable="false"
            >
              <circle cx="8" cy="5" r="2.5" stroke="currentColor" />
              <path d="M2.5 13.5a5.5 5.5 0 0 1 11 0" stroke="currentColor" stroke-linecap="round" />
            </svg>
            <span class="ip-pc-user__name">{{ displayName || '未登录' }}</span>
            <svg
              class="ip-pc-user__caret"
              width="12"
              height="12"
              viewBox="0 0 16 16"
              fill="none"
              aria-hidden="true"
              focusable="false"
            >
              <path
                d="M4 6l4 4 4-4"
                stroke="currentColor"
                stroke-linecap="round"
                stroke-linejoin="round"
              />
            </svg>
          </button>
          <template #dropdown>
            <ElDropdownMenu>
              <ElDropdownItem command="logout">退出登录</ElDropdownItem>
            </ElDropdownMenu>
          </template>
        </ElDropdown>
      </div>
    </header>

    <div class="ip-pc-body">
      <aside
        id="ip-pc-sidebar"
        class="ip-pc-sidebar"
        :class="{ 'ip-pc-sidebar--collapsed': collapsed }"
        aria-label="侧边导航"
      >
        <PcNavMenu :items="pcNavigationItems" :collapsed="collapsed" />
      </aside>
      <main id="main-content" class="ip-pc-main" tabindex="-1">
        <RouterView />
      </main>
    </div>
  </div>
</template>

<style scoped>
.ip-pc-layout {
  display: flex;
  flex-direction: column;
  height: 100vh;
}

.ip-pc-header {
  display: flex;
  flex: 0 0 auto;
  align-items: center;
  justify-content: space-between;
  height: var(--ip-pc-header-height);
  padding: 0 var(--ip-space-4);
  background: var(--ip-color-bg-container);
  border-bottom: 1px solid var(--ip-color-border);
}

.ip-pc-header__left,
.ip-pc-header__right {
  display: flex;
  align-items: center;
  gap: var(--ip-space-4);
}

.ip-pc-collapse {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 32px;
  height: 32px;
  padding: 0;
  color: var(--ip-color-text-secondary);
  background: transparent;
  border: 0;
  border-radius: var(--ip-radius-md);
  cursor: pointer;
}

.ip-pc-collapse:hover {
  background: var(--ip-color-bg-muted);
  color: var(--ip-color-text-primary);
}

.ip-pc-brand {
  font-size: var(--ip-font-size-lg);
  font-weight: 600;
  color: var(--ip-color-text-primary);
}

.ip-pc-terminal {
  display: inline-flex;
  align-items: center;
  gap: var(--ip-space-2);
  color: var(--ip-color-text-secondary);
  font-size: var(--ip-font-size-sm);
  white-space: nowrap;
}

.ip-pc-user {
  display: inline-flex;
  align-items: center;
  gap: var(--ip-space-2);
  min-height: 32px;
  padding: 0 var(--ip-space-2);
  color: var(--ip-color-text-primary);
  background: transparent;
  border: 0;
  border-radius: var(--ip-radius-md);
  cursor: pointer;
  font-size: var(--ip-font-size-md);
}

.ip-pc-user:hover {
  background: var(--ip-color-bg-muted);
}

.ip-pc-user__caret {
  color: var(--ip-color-text-disabled);
}

.ip-pc-body {
  display: flex;
  flex: 1 1 auto;
  min-height: 0;
}

.ip-pc-sidebar {
  flex: 0 0 auto;
  width: var(--ip-pc-sidebar-width);
  overflow-y: auto;
  background: var(--ip-color-bg-container);
  border-right: 1px solid var(--ip-color-border);
  transition: width 200ms ease;
}

.ip-pc-sidebar--collapsed {
  width: var(--ip-pc-sidebar-width-collapsed);
}

.ip-pc-main {
  flex: 1 1 auto;
  min-width: 0;
  overflow: auto;
  background: var(--ip-color-bg-page);
}

/* 跳到主内容入口(§14.1):视觉隐藏,聚焦时可见且置于最前。 */
.ip-pc-skip-link {
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

.ip-pc-skip-link:focus {
  top: var(--ip-space-2);
}

.ip-pc-visually-hidden {
  position: absolute;
  width: 1px;
  height: 1px;
  padding: 0;
  margin: -1px;
  overflow: hidden;
  clip: rect(0 0 0 0);
  white-space: nowrap;
  border: 0;
}

@media (prefers-reduced-motion: reduce) {
  .ip-pc-sidebar,
  .ip-pc-skip-link {
    transition: none;
  }
}
</style>
