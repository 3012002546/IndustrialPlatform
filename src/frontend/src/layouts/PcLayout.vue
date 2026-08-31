<script setup lang="ts">
/**
 * PC 平台外壳(PF-01 §7.8):组合四层——
 * PlatformTopBar 56px → body(PlatformToolRail 72px + 功能树与内容工作区
 * [PlatformFunctionTree 208px/52px + tabs/RouterView 内容画布])。
 * 功能树收起状态由 ThemeStore 持久化,不再直接读写旧侧栏键。
 */

import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { RouterView, useRoute, useRouter } from 'vue-router'
import { ElDropdown, ElDropdownItem, ElDropdownMenu, ElMessage, ElMessageBox } from 'element-plus'
import { Delete, FullScreen, Lock, SwitchButton, UserFilled } from '@element-plus/icons-vue'

import MockModeBanner from '@/components/base/MockModeBanner.vue'
import PlatformBrand from '@/components/brand/PlatformBrand.vue'
import LocaleControl from '@/components/localization/LocaleControl.vue'
import { pcNavigationGroups } from '@/components/navigation/navigation'
import PlatformCommandSearch, {
  type PlatformCommandItem,
} from '@/components/shell/PlatformCommandSearch.vue'
import PlatformContextSwitcher from '@/components/shell/PlatformContextSwitcher.vue'
import PlatformEnvironmentBadge from '@/components/shell/PlatformEnvironmentBadge.vue'
import PcExperienceModeControl from '@/components/shell/PcExperienceModeControl.vue'
import PcWorkspaceTabs from '@/components/shell/PcWorkspaceTabs.vue'
import PlatformFunctionTree from '@/components/shell/PlatformFunctionTree.vue'
import PlatformServiceStatus from '@/components/shell/PlatformServiceStatus.vue'
import PlatformToolRail from '@/components/shell/PlatformToolRail.vue'
import PlatformTopBar from '@/components/shell/PlatformTopBar.vue'
import PlatformSessionControls from '@/components/shell/PlatformSessionControls.vue'
import ThemeControl from '@/components/theme/ThemeControl.vue'
import WorkspaceTabLimitDialog from '@/components/shell/WorkspaceTabLimitDialog.vue'
import AppLockOverlay from '@/components/shell/AppLockOverlay.vue'
import type { TerminalType } from '@/device/types'
import { loadRuntimeConfig } from '@/config/runtimeConfig'
import { useAuthStore } from '@/stores/authStore'
import { useDeviceStore } from '@/stores/deviceStore'
import { useWorkspaceTabsStore } from '@/stores/workspaceTabsStore'
import { useLockStore } from '@/stores/lockStore'
import { useThemeStore } from '@/stores/themeStore'
import { useSystemDataRuntimeStore } from '@/stores/systemData/runtimeStore'
import { clearCurrentUserUiCache } from '@/stores/uiCacheStore'
import { localeMessages, resolveLocaleMessage } from '@/localization/i18n'
import { usePlatformLocale } from '@/localization/localeContext'
import { applyPermissionPolicy } from '@/systemData/runtime/navigation'
import { buildTabId } from '@/workspace'
import type { NavigationItem } from '@/components/navigation/types'
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
const themeStore = useThemeStore()
const systemDataRuntime = useSystemDataRuntimeStore()
const runtimeConfig = loadRuntimeConfig()
const focusMode = ref(false)
const browserFullscreen = ref(false)
const locale = usePlatformLocale()

const authorizedNavigationGroups = computed(() =>
  applyPermissionPolicy(pcNavigationGroups, authStore.user?.permissions ?? []),
)

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
const activeGroupSections = computed(() => activeGroup.value?.sections ?? [])
const navigationMode = computed(() => themeStore.navigationMode)
const functionTreeCollapsed = computed(
  () => navigationMode.value !== 'expanded' || themeStore.preferences.pcFunctionTreeCollapsed,
)

const displayName = computed(() => authStore.user?.displayName ?? '')
const terminalLabel = computed(() => TERMINAL_LABELS[deviceStore.terminal] ?? deviceStore.terminal)
const tenant = computed(() => {
  const user = authStore.user
  return user === null ? null : { id: user.tenantId, name: user.tenantId }
})

function flattenNavigationItems(
  groups: readonly { items: readonly NavigationItem[] }[],
): readonly NavigationItem[] {
  const result: NavigationItem[] = []
  const visit = (items: readonly NavigationItem[]): void => {
    for (const item of items) {
      result.push(item)
      if (item.children !== undefined) visit(item.children)
    }
  }
  for (const group of groups) visit(group.items)
  return result
}

const commandItems = computed<readonly PlatformCommandItem[]>(() => [
  ...flattenNavigationItems(authorizedNavigationGroups.value).map((item) => ({
    id: item.id,
    label: resolveLocaleMessage(locale.value, item.labelKey, item.fallbackLabel ?? item.label),
    kind: 'navigation' as const,
  })),
  ...tabsStore.tabs
    .filter((tab) => tab.kind === 'business')
    .map((tab) => ({
      id: tab.id,
      label: resolveLocaleMessage(locale.value, tab.titleKey, tab.fallbackTitle ?? tab.title),
      kind: 'recent' as const,
    })),
])
const shellCopy = computed(() => localeMessages[locale.value].shell.top)

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

/** 用户菜单命令:仅处理真实的个人中心、白名单 UI 缓存、锁定与退出。 */
function onUserCommand(command: unknown): void {
  if (command === 'profile') void router.push({ name: 'profile' })
  else if (command === 'clear-cache') void clearCache()
  else if (command === 'lock') lockStore.lock()
  else if (command === 'logout') void handleLogout()
}

async function clearCache(): Promise<void> {
  const user = authStore.user
  if (user === null) return
  try {
    await ElMessageBox.confirm(shellCopy.value.clearCacheConfirm, shellCopy.value.clearCache, {
      confirmButtonText: shellCopy.value.clearCache,
      cancelButtonText: localeMessages[locale.value].common.action.cancel,
      type: 'warning',
    })
  } catch {
    return
  }
  clearCurrentUserUiCache({ tenantId: user.tenantId, userId: user.userId })
  tabsStore.clearUiCache()
  navigateToActive()
  ElMessage.success(shellCopy.value.cacheCleared)
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

function onCommandSearchSelect(id: string): void {
  const navigation = flattenNavigationItems(authorizedNavigationGroups.value).find(
    (item) => item.id === id,
  )
  if (navigation !== undefined) {
    onMenuSelect(navigation.routeName)
    return
  }
  const tab = tabsStore.tabs.find((candidate) => candidate.id === id)
  if (tab !== undefined) void router.push(tab.route)
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

function onTogglePin(tabId: string): void {
  tabsStore.setTabPinned(tabId, !(tabsStore.tabs.find((tab) => tab.id === tabId)?.pinned === true))
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
    <a class="ip-pc-skip-link" href="#main-content">{{ localeMessages[locale].common.action.skipToContent }}</a>

    <PlatformTopBar class="ip-pc-chrome">
      <template #brand>
        <PlatformBrand class="ip-pc-brand" variant="dark" />
        <span class="ip-pc-terminal" data-testid="terminal-info">{{ shellCopy.terminal }} {{ terminalLabel }}</span>
        <PlatformEnvironmentBadge :environment="runtimeConfig.deploymentEnvironment" />
      </template>

      <template #context>
        <PlatformContextSwitcher :tenant="tenant" />
      </template>

      <template #global-search>
        <div class="ip-pc-context-search">
          <PlatformCommandSearch :items="commandItems" @select="onCommandSearchSelect" />
        </div>
      </template>

      <template #global-actions>
        <MockModeBanner />
        <PcExperienceModeControl mode="management" />
        <PlatformServiceStatus
          :degraded="systemDataRuntime.degraded"
          :unavailable="systemDataRuntime.unavailable"
          @retry="systemDataRuntime.refresh('Pc')"
        />
        <PlatformSessionControls />
        <LocaleControl />
        <button
          type="button"
          class="ip-pc-shell-action"
          data-testid="browser-fullscreen"
          :aria-label="browserFullscreen ? localeMessages[locale].common.action.exitFullscreen : localeMessages[locale].common.action.fullscreen"
          :title="browserFullscreen ? localeMessages[locale].common.action.exitFullscreen : localeMessages[locale].common.action.fullscreen"
          @click="toggleBrowserFullscreen"
        >
          <FullScreen aria-hidden="true" />
        </button>
        <ThemeControl terminal="pc" />
      </template>

      <template #user>
        <ElDropdown trigger="click" popper-class="ip-pc-user-popper" @command="onUserCommand">
          <button type="button" class="ip-pc-user" data-testid="user-menu" :aria-label="shellCopy.userMenu">
            <span class="ip-pc-user__avatar"><UserFilled aria-hidden="true" /></span>
            <span class="ip-pc-user__copy">
              <strong class="ip-pc-user__name">{{ displayName || localeMessages[locale].common.state.unauthenticated }}</strong>
              <small>{{ authStore.user?.username ?? '' }}</small>
            </span>
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
              <li class="ip-pc-user-menu__summary" role="presentation">
                <strong>{{ displayName || localeMessages[locale].common.state.unauthenticated }}</strong>
                <span>{{ authStore.user?.username ?? '' }} · {{ tenant?.name ?? shellCopy.noTenant }}</span>
              </li>
              <ElDropdownItem command="profile"><UserFilled aria-hidden="true" />{{ shellCopy.profile }}</ElDropdownItem>
              <ElDropdownItem command="clear-cache"><Delete aria-hidden="true" />{{ shellCopy.clearCache }}</ElDropdownItem>
              <ElDropdownItem command="lock"><Lock aria-hidden="true" />{{ shellCopy.lock }}</ElDropdownItem>
              <ElDropdownItem command="logout" divided class="ip-pc-user-menu__logout"><SwitchButton aria-hidden="true" />{{ localeMessages[locale].common.action.logout }}</ElDropdownItem>
            </ElDropdownMenu>
          </template>
        </ElDropdown>
      </template>
    </PlatformTopBar>

    <div class="ip-pc-body">
      <PlatformToolRail
        class="ip-pc-chrome"
        v-model:active-group-id="activeGroupId"
        :groups="pcNavigationGroups"
        :mode="navigationMode"
      />

      <div
        class="ip-pc-function-and-workspace"
        :class="{
          'ip-pc-function-and-workspace--secondary-collapsed': functionTreeCollapsed,
          'ip-pc-function-and-workspace--no-secondary': activeGroup === null,
        }"
      >
        <PlatformFunctionTree
          class="ip-pc-chrome"
          v-if="activeGroup !== null"
          :label="activeGroup.label"
          :label-key="activeGroup.labelKey"
          :items="activeGroupItems"
          :sections="activeGroupSections"
        />
        <div class="ip-pc-content">
          <PcWorkspaceTabs
            class="ip-pc-chrome"
            @activate="onActivate"
            @close="onClose"
            @close-others="onCloseOthers"
            @close-right="onCloseRight"
            @close-left="onCloseLeft"
            @close-all="onCloseAll"
            @toggle-pin="onTogglePin"
            @reload="onReload"
            :focus-mode="focusMode"
            @focus="onFocusTab"
            @focus-exit="onFocusExit"
            @menu-select="onMenuSelect"
          />
          <main id="main-content" class="ip-pc-main" style="padding: 16px" tabindex="-1">
            <RouterView :key="contentKey" />
          </main>
        </div>
      </div>
    </div>

    <WorkspaceTabLimitDialog @resolve="onLimitResolve" />

    <button
      v-if="focusMode"
      type="button"
      class="ip-focus-exit"
      data-testid="focus-mode-exit"
      :aria-label="localeMessages[locale].common.action.exitFocusMode"
      @click="exitFocusMode"
    >
      {{ localeMessages[locale].common.action.exitFocusMode }}
    </button>

    <AppLockOverlay />
  </div>
</template>

<style scoped>
.ip-pc-layout {
  display: flex;
  flex-direction: column;
  width: 100%;
  min-width: 0;
  height: 100vh;
}

.ip-pc-body {
  display: flex;
  flex: 1 1 auto;
  min-width: 0;
  min-height: 0;
}

.ip-pc-function-and-workspace {
  display: grid;
  grid-template-columns: var(--ip-shell-functiontree-width) minmax(0, 1fr);
  flex: 1 1 auto;
  min-width: 0;
  min-height: 0;
}

.ip-pc-function-and-workspace--secondary-collapsed {
  grid-template-columns: var(--ip-shell-toolrail-width-compact) minmax(0, 1fr);
}

.ip-pc-function-and-workspace--no-secondary {
  grid-template-columns: minmax(0, 1fr);
}

.ip-pc-content {
  display: flex;
  min-width: 0;
  min-height: 0;
  flex-direction: column;
}

.ip-pc-main {
  display: flex;
  flex-direction: column;
  flex: 1 1 auto;
  min-width: 0;
  min-height: 0;
  box-sizing: border-box;
  overflow: auto;
  background: var(--ip-color-bg-page);
}

.ip-pc-main > :deep(*) {
  flex: 1 1 auto;
  min-width: 0;
  max-width: 100%;
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

.ip-pc-layout--focus .ip-pc-content {
  display: block;
  width: 100%;
  height: 100%;
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

.ip-pc-brand {
  flex: 0 0 184px;
  width: 184px;
  margin-right: 8px;
}

.ip-pc-brand :deep(img) {
  width: 184px;
  height: 30px;
  max-width: none;
  max-height: none;
  object-fit: contain;
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

.ip-pc-context-search {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: var(--ip-space-4);
  width: min(100%, 760px);
  min-width: 0;
}

.ip-pc-user {
  display: inline-flex;
  box-sizing: border-box;
  align-items: center;
  gap: 9px;
  width: 100%;
  min-width: 0;
  min-height: 32px;
  padding: 0 var(--ip-space-2);
  color: var(--ip-shell-topbar-text);
  background: transparent;
  border: 0;
  border-radius: var(--ip-radius-md);
  cursor: pointer;
  font-size: var(--ip-font-size-md);
  white-space: nowrap;
}

.ip-pc-user__name {
  display: block;
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.ip-pc-user__avatar {
  display: inline-flex;
  flex: 0 0 28px;
  align-items: center;
  justify-content: center;
  width: 28px;
  height: 28px;
  color: #35728e;
  background: #d9e9f2;
  border-radius: 50%;
}

.ip-pc-user__avatar :deep(svg) {
  width: 17px;
  height: 17px;
}

.ip-pc-user__copy {
  display: flex;
  min-width: 0;
  flex: 1 1 auto;
  flex-direction: column;
  gap: 1px;
  line-height: 1.2;
}

.ip-pc-user__copy small {
  overflow: hidden;
  color: var(--ip-shell-topbar-text-secondary);
  font-size: 11px;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.ip-pc-user:hover {
  background: rgb(255 255 255 / 0.12);
}

.ip-pc-user__caret {
  color: var(--ip-shell-topbar-text-secondary);
}

:global(.ip-pc-user-popper) {
  box-sizing: border-box;
  width: 192px;
  padding: 4px;
}

:global(.ip-pc-user-popper .ip-pc-user-menu__summary) {
  display: flex;
  box-sizing: border-box;
  flex-direction: column;
  gap: 2px;
  min-width: 0;
  margin: 0 0 4px;
  padding: 7px 12px 9px;
  border-bottom: 1px solid var(--ip-color-border);
  list-style: none;
}

:global(.ip-pc-user-popper .ip-pc-user-menu__summary strong) {
  overflow: hidden;
  color: var(--ip-color-text-primary);
  font-size: 13px;
  font-weight: 650;
  line-height: 16px;
  text-overflow: ellipsis;
  white-space: nowrap;
}

:global(.ip-pc-user-popper .ip-pc-user-menu__summary span) {
  overflow: hidden;
  color: var(--ip-color-text-secondary);
  font-size: 11px;
  line-height: 14px;
  text-overflow: ellipsis;
  white-space: nowrap;
}

:global(.ip-pc-user-popper .el-dropdown-menu__item) {
  display: flex;
  box-sizing: border-box;
  align-items: center;
  gap: 10px;
  height: 36px;
  min-height: 36px;
  padding: 0 12px;
  font-size: 13px;
  line-height: 36px;
  white-space: nowrap;
}

:global(.ip-pc-user-popper .el-dropdown-menu__item svg) {
  width: 16px;
  height: 16px;
  flex: 0 0 16px;
  margin: 0;
}

:global(.ip-pc-user-popper .el-dropdown-menu__item.ip-pc-user-menu__logout) {
  color: var(--ip-color-danger);
}

@media (max-width: 720px) {
  /* At the narrow PC fallback width, keep the primary actions and user menu;
     terminal metadata is secondary and may yield space to the content shell. */
  .ip-pc-terminal,
  .ip-pc-brand :deep(.ip-environment-badge) {
    display: none;
  }
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
