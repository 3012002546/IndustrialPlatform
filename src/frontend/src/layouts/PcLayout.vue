<script setup lang="ts">
/**
 * PC 平台外壳(PF-01 §7.8):组合四层——
 * PlatformTopBar 52px → body(PlatformToolRail 52px + 功能树与内容工作区
 * [PlatformFunctionTree 216px/0px + RouterView 内容画布])。
 * 功能树收起状态由 ThemeStore 持久化,不再直接读写旧侧栏键。
 */

import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { RouterView, useRoute, useRouter } from 'vue-router'
import { ElDropdown, ElDropdownItem, ElDropdownMenu } from 'element-plus'
import { FullScreen, Lock } from '@element-plus/icons-vue'

import MockModeBanner from '@/components/base/MockModeBanner.vue'
import PlatformBrand from '@/components/brand/PlatformBrand.vue'
import { pcNavigationGroups } from '@/components/navigation/navigation'
import PcWorkspaceTabs from '@/components/shell/PcWorkspaceTabs.vue'
import PlatformFunctionTree from '@/components/shell/PlatformFunctionTree.vue'
import PlatformToolRail from '@/components/shell/PlatformToolRail.vue'
import PlatformTopBar from '@/components/shell/PlatformTopBar.vue'
import ThemeControl from '@/components/theme/ThemeControl.vue'
import WorkspaceTabLimitDialog from '@/components/shell/WorkspaceTabLimitDialog.vue'
import AppLockOverlay from '@/components/shell/AppLockOverlay.vue'
import type { TerminalType } from '@/device/types'
import { useAuthStore } from '@/stores/authStore'
import { useDeviceStore } from '@/stores/deviceStore'
import { useWorkspaceTabsStore } from '@/stores/workspaceTabsStore'
import { useLockStore } from '@/stores/lockStore'
import { buildTabId } from '@/workspace'
import type { TabLimitResolution, WorkspaceTab } from '@/workspace'

const TERMINAL_LABELS: Record<TerminalType, string> = {
  pc: 'PC',
  pda: 'PDA',
  mobile: 'Mobile',
}

const router = useRouter()
const route = useRoute()
const authStore = useAuthStore()
const deviceStore = useDeviceStore()
const tabsStore = useWorkspaceTabsStore()
const lockStore = useLockStore()
const focusMode = ref(false)
const browserFullscreen = ref(false)

/** 当前平台分组:默认第一个;路由变化时跟随所属分组。 */
const activeGroupId = ref(pcNavigationGroups[0]?.id ?? '')

watch(
  () => route.name,
  () => {
    const group = pcNavigationGroups.find((g) =>
      g.items.some((item) => item.routeName === route.name),
    )
    if (group !== undefined) activeGroupId.value = group.id
  },
  { immediate: true },
)

const activeGroup = computed(
  () => pcNavigationGroups.find((g) => g.id === activeGroupId.value) ?? null,
)

/** 当前组的全部 items(功能树内部再做权限过滤)。 */
const activeGroupItems = computed(() => activeGroup.value?.items ?? [])

const displayName = computed(() => authStore.user?.displayName ?? '')
const terminalLabel = computed(() => TERMINAL_LABELS[deviceStore.terminal] ?? deviceStore.terminal)

function onFullscreenChange(): void {
  browserFullscreen.value = document.fullscreenElement !== null
}

async function toggleBrowserFullscreen(): Promise<void> {
  try {
    if (document.fullscreenElement !== null) await document.exitFullscreen()
    else await document.documentElement.requestFullscreen()
  } catch {
    browserFullscreen.value = false
  }
}

function exitFocusMode(): void {
  focusMode.value = false
}

function onDocumentKeydown(event: KeyboardEvent): void {
  if (event.key === 'Escape' && focusMode.value) exitFocusMode()
}

onMounted(() => {
  document.addEventListener('fullscreenchange', onFullscreenChange)
  document.addEventListener('keydown', onDocumentKeydown)
  onFullscreenChange()
})

onBeforeUnmount(() => {
  document.removeEventListener('fullscreenchange', onFullscreenChange)
  document.removeEventListener('keydown', onDocumentKeydown)
})

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

/** RouterView 内容 key:路由位置 + 当前激活标签的 reloadVersion,重挂载实现「重新加载」。 */
const contentKey = computed(() => `${route.fullPath}:${tabsStore.activeTab?.reloadVersion ?? 0}`)

/** 当前路由是否已展示该标签(按稳定标签 id 比较,容忍 vue-router 的 null 查询值)。 */
function isCurrentTab(tab: WorkspaceTab): boolean {
  return buildTabId(String(route.name ?? ''), route.params, route.query) === tab.id
}

/** 确定性导航到当前激活标签;已展示时不重复导航。 */
function navigateToActive(): void {
  const tab = tabsStore.activeTab
  if (tab === null || isCurrentTab(tab)) return
  void router.push({ name: tab.route.name, params: tab.route.params, query: tab.route.query })
}

function onActivate(tabId: string): void {
  const tab = tabsStore.activateTab(tabId)
  if (tab === null || isCurrentTab(tab)) return
  void router.push({ name: tab.route.name, params: tab.route.params, query: tab.route.query })
}

function onMenuSelect(routeName: string): void {
  void router.push({ name: routeName })
}

function onClose(tabId: string): void {
  tabsStore.closeTab(tabId)
  navigateToActive()
}

function onCloseOthers(tabId: string): void {
  tabsStore.closeOthers(tabId)
  navigateToActive()
}

function onCloseRight(tabId: string): void {
  tabsStore.closeRight(tabId)
  navigateToActive()
}

function onCloseLeft(tabId: string): void {
  tabsStore.closeLeft(tabId)
  navigateToActive()
}

function onCloseAll(): void {
  tabsStore.closeAll()
  navigateToActive()
}

function onFocusTab(tabId: string): void {
  const tab = tabsStore.activateTab(tabId)
  if (tab === null) return
  navigateToActive()
  focusMode.value = true
}

function onFocusExit(): void {
  focusMode.value = false
}

/** 刷新右键目标:先经 Store 激活并导航,再递增目标 reloadVersion。 */
function onReload(tabId: string): void {
  const tab = tabsStore.activateTab(tabId)
  if (tab === null || tab.kind !== 'business') return
  navigateToActive()
  tabsStore.reloadTab(tabId)
}

function onLimitResolve(resolution: TabLimitResolution): void {
  const target = tabsStore.resolvePending(resolution)
  if (target === null) return
  void router.push({ name: target.name, params: target.params, query: target.query })
}
</script>

<template>
  <div class="ip-pc-layout" :class="{ 'ip-pc-layout--focus': focusMode }">
    <a class="ip-pc-skip-link" href="#main-content">跳到主内容</a>

    <PlatformTopBar class="ip-pc-chrome">
      <template #brand>
        <PlatformBrand class="ip-pc-brand" variant="dark" />
        <span class="ip-pc-terminal" data-testid="terminal-info"> 终端 {{ terminalLabel }} </span>
      </template>

      <template #global-actions>
        <MockModeBanner />
        <button
          type="button"
          class="ip-pc-shell-action"
          data-testid="browser-fullscreen"
          :aria-label="browserFullscreen ? '退出浏览器全屏' : '浏览器全屏'"
          :title="browserFullscreen ? '退出浏览器全屏' : '浏览器全屏'"
          @click="toggleBrowserFullscreen"
        >
          <FullScreen aria-hidden="true" />
        </button>
        <button
          type="button"
          class="ip-pc-shell-action"
          data-testid="lock-workspace"
          aria-label="锁定工作区"
          title="锁定工作区"
          @click="lockStore.lock"
        >
          <Lock aria-hidden="true" />
        </button>
        <ThemeControl terminal="pc" />
      </template>

      <template #user>
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
      </template>
    </PlatformTopBar>

    <PcWorkspaceTabs
      class="ip-pc-chrome"
      @activate="onActivate"
      @close="onClose"
      @close-others="onCloseOthers"
      @close-right="onCloseRight"
      @close-left="onCloseLeft"
      @close-all="onCloseAll"
      @reload="onReload"
      :focus-mode="focusMode"
      @focus="onFocusTab"
      @focus-exit="onFocusExit"
      @menu-select="onMenuSelect"
    />

    <div class="ip-pc-body">
      <PlatformToolRail
        class="ip-pc-chrome"
        v-model:active-group-id="activeGroupId"
        :groups="pcNavigationGroups"
      />

      <div class="ip-pc-function-and-workspace">
        <PlatformFunctionTree
          class="ip-pc-chrome"
          v-if="activeGroup !== null"
          :label="activeGroup.label"
          :items="activeGroupItems"
        />
        <main id="main-content" class="ip-pc-main" style="padding: 10px" tabindex="-1">
          <RouterView :key="contentKey" />
        </main>
      </div>
    </div>

    <WorkspaceTabLimitDialog @resolve="onLimitResolve" />

    <button
      v-if="focusMode"
      type="button"
      class="ip-focus-exit"
      data-testid="focus-mode-exit"
      aria-label="退出页面专注全屏"
      @click="exitFocusMode"
    >
      退出专注全屏
    </button>

    <AppLockOverlay />
  </div>
</template>

<style scoped>
.ip-pc-layout {
  display: flex;
  flex-direction: column;
  height: 100vh;
}

.ip-pc-body {
  display: flex;
  flex: 1 1 auto;
  min-height: 0;
}

.ip-pc-function-and-workspace {
  display: flex;
  flex: 1 1 auto;
  min-width: 0;
  min-height: 0;
}

.ip-pc-main {
  flex: 1 1 auto;
  min-width: 0;
  min-height: 0;
  box-sizing: border-box;
  overflow: auto;
  background: var(--ip-color-bg-page);
}

.ip-pc-layout--focus .ip-pc-chrome {
  display: none;
}

.ip-pc-layout--focus .ip-pc-body,
.ip-pc-layout--focus .ip-pc-function-and-workspace,
.ip-pc-layout--focus .ip-pc-main {
  width: 100%;
  height: 100%;
}

.ip-pc-layout--focus .ip-pc-body,
.ip-pc-layout--focus .ip-pc-function-and-workspace {
  display: block;
}

.ip-pc-layout--focus .ip-pc-main {
  overflow: auto;
}

.ip-pc-layout--focus .ip-focus-exit {
  display: inline-flex;
}

.ip-pc-shell-action,
.ip-focus-exit {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  min-width: 32px;
  min-height: 32px;
  padding: 0 var(--ip-space-2);
  color: inherit;
  background: transparent;
  border: 0;
  border-radius: var(--ip-radius-md);
  cursor: pointer;
}

.ip-pc-shell-action:hover,
.ip-pc-shell-action:focus-visible {
  background: rgb(255 255 255 / 0.12);
}

.ip-focus-exit {
  position: fixed;
  z-index: 1500;
  top: var(--ip-space-4);
  right: var(--ip-space-4);
  display: none;
  color: var(--ip-color-text-primary);
  background: var(--ip-color-bg-container);
  border: 1px solid var(--ip-color-border);
  box-shadow: var(--ip-shadow-sm);
}

.ip-pc-terminal {
  display: inline-flex;
  align-items: center;
  gap: var(--ip-space-2);
  color: var(--ip-shell-topbar-text-secondary);
  font-size: var(--ip-font-size-sm);
  white-space: nowrap;
}

.ip-pc-user {
  display: inline-flex;
  align-items: center;
  gap: var(--ip-space-2);
  min-height: 32px;
  padding: 0 var(--ip-space-2);
  color: var(--ip-shell-topbar-text);
  background: transparent;
  border: 0;
  border-radius: var(--ip-radius-md);
  cursor: pointer;
  font-size: var(--ip-font-size-md);
}

.ip-pc-user:hover {
  background: rgb(255 255 255 / 0.12);
}

.ip-pc-user__caret {
  color: var(--ip-shell-topbar-text-secondary);
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

@media (prefers-reduced-motion: reduce) {
  .ip-pc-skip-link {
    transition: none;
  }
}
</style>
