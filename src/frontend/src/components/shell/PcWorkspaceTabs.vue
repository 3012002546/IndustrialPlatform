<script setup lang="ts">
/**
 * PcWorkspaceTabs(PF-01 §7.9/§10.1):36px PC 业务标签栏。
 * 固定工作台恒在第 0 位且不可关闭;业务标签可关闭/关闭其他/关闭右侧/重新加载。
 * 溢出单行横向滚动;所有控件为原生按钮 + ElDropdown,键盘可达。
 * 组件为展示层:数据来自 WorkspaceTabsStore,动作 emit 给 PcLayout 处理导航。
 */

import { computed, nextTick, onBeforeUnmount, onMounted, ref } from 'vue'
import { Search } from '@element-plus/icons-vue'

import { pcNavigationGroups } from '@/components/navigation/navigation'
import { resolveLocaleMessage } from '@/localization/i18n'
import { useLocalizationStore } from '@/stores/localizationStore'
import { useAuthStore } from '@/stores/authStore'
import { useWorkspaceTabsStore } from '@/stores/workspaceTabsStore'
import type { WorkspaceTab } from '@/workspace'

const emit = defineEmits<{
  activate: [tabId: string]
  close: [tabId: string]
  'close-others': [tabId: string]
  'close-right': [tabId: string]
  'close-left': [tabId: string]
  'close-all': []
  reload: [tabId: string]
  focus: [tabId: string]
  'focus-exit': [tabId: string]
  'menu-select': [routeName: string]
  'toggle-pin': [tabId: string]
}>()

const props = withDefaults(defineProps<{ focusMode?: boolean }>(), { focusMode: false })

const tabsStore = useWorkspaceTabsStore()
const authStore = useAuthStore()
const localization = useLocalizationStore()
const selectedMenu = ref('')
const contextTabId = ref<string | null>(null)
const contextMenuStyle = ref<Record<string, string>>({})
const contextMenuRef = ref<HTMLElement | null>(null)
const contextTab = computed(() =>
  tabsStore.tabs.find((item) => item.id === contextTabId.value) ?? null,
)

const searchableMenus = computed(() =>
  pcNavigationGroups.flatMap((group) =>
    group.items
      .filter((item) => item.permission === undefined || authStore.hasPermission(item.permission))
      .map((item) => ({
        label: item.label,
        groupLabel: group.label,
        routeName: String(item.routeName),
      })),
  ),
)

function selectMenu(routeName: string): void {
  if (routeName === '') return
  emit('menu-select', routeName)
  selectedMenu.value = ''
}

function activate(tab: WorkspaceTab): void {
  emit('activate', tab.id)
}

function titleFor(tab: WorkspaceTab): string {
  return resolveLocaleMessage(localization.locale, tab.titleKey, tab.fallbackTitle ?? tab.title)
}

function close(tabId: string): void {
  emit('close', tabId)
}

function openContextMenu(event: MouseEvent, tab: WorkspaceTab): void {
  contextTabId.value = tab.id
  contextMenuStyle.value = { left: `${event.clientX}px`, top: `${event.clientY}px` }
  void nextTick(() => {
    const menu = contextMenuRef.value
    if (menu === null) return
    const rect = menu.getBoundingClientRect()
    const menuWidth = rect.width || 180
    const menuHeight = rect.height || 320
    contextMenuStyle.value = {
      left: `${Math.max(0, Math.min(event.clientX, window.innerWidth - menuWidth))}px`,
      top: `${Math.max(0, Math.min(event.clientY, window.innerHeight - menuHeight))}px`,
    }
  })
}

function closeContextMenu(): void {
  contextTabId.value = null
}

function selectContextCommand(command: string): void {
  const tabId = contextTabId.value
  if (tabId === null) return
  if (command === 'close') emit('close', tabId)
  else if (command === 'close-left') emit('close-left', tabId)
  else if (command === 'close-right') emit('close-right', tabId)
  else if (command === 'close-others') emit('close-others', tabId)
  else if (command === 'close-all') emit('close-all')
  else if (command === 'reload') emit('reload', tabId)
  else if (command === 'focus') emit('focus', tabId)
  else if (command === 'focus-exit') emit('focus-exit', tabId)
  else if (command === 'toggle-pin') emit('toggle-pin', tabId)
  closeContextMenu()
}

function onDocumentPointerDown(event: PointerEvent): void {
  if (contextMenuRef.value?.contains(event.target as Node)) return
  closeContextMenu()
}

function onDocumentKeyDown(event: KeyboardEvent): void {
  if (event.key === 'Escape') closeContextMenu()
}

onMounted(() => document.addEventListener('pointerdown', onDocumentPointerDown))
onMounted(() => document.addEventListener('keydown', onDocumentKeyDown))
onBeforeUnmount(() => {
  document.removeEventListener('pointerdown', onDocumentPointerDown)
  document.removeEventListener('keydown', onDocumentKeyDown)
})
</script>

<template>
  <nav class="ip-pc-tabs" aria-label="工作台标签" role="tablist">
    <template v-for="(tab, index) in tabsStore.tabs" :key="tab.id">
      <el-select
        v-if="index === 0"
        v-model="selectedMenu"
        class="ip-pc-tabs__menu-search"
        filterable
        clearable
        placeholder="搜索菜单"
        aria-label="搜索菜单"
        @change="selectMenu"
      >
        <template #prefix
          ><el-icon><Search /></el-icon
        ></template>
        <el-option
          v-for="menu in searchableMenus"
          :key="menu.routeName"
          :label="`${menu.groupLabel} / ${menu.label}`"
          :value="menu.routeName"
        />
      </el-select>

      <div
        class="ip-pc-tabs__item"
        :class="{ 'ip-pc-tabs__item--active': tab.id === tabsStore.activeTabId }"
        @contextmenu.prevent="openContextMenu($event, tab)"
      >
        <button
          type="button"
          class="ip-pc-tabs__tab"
          role="tab"
          :aria-selected="tab.id === tabsStore.activeTabId"
          :aria-label="titleFor(tab)"
          :title="titleFor(tab)"
          @click="activate(tab)"
        >
          {{ titleFor(tab) }}
        </button>

        <template v-if="tab.kind === 'business' && tab.pinned !== true">
          <button
            type="button"
            class="ip-pc-tabs__close"
            :aria-label="`关闭 ${titleFor(tab)}`"
            title="关闭"
            @click="close(tab.id)"
          >
            ×
          </button>
          </template>
      </div>
    </template>
    <div
      v-if="contextTabId !== null"
      ref="contextMenuRef"
      class="ip-pc-tabs__context-menu"
      data-testid="workspace-tab-context-menu"
      role="menu"
      :style="contextMenuStyle"
    >
      <button type="button" data-testid="workspace-tab-menu-reload" role="menuitem" @click="selectContextCommand('reload')">
        刷新
      </button>
      <button
        type="button"
        data-testid="workspace-tab-menu-close"
        role="menuitem"
        :disabled="contextTab?.kind === 'fixed' || contextTab?.pinned === true"
        @click="selectContextCommand('close')"
      >
        关闭
      </button>
      <button type="button" data-testid="workspace-tab-menu-close-left" role="menuitem" @click="selectContextCommand('close-left')">
        关闭左侧
      </button>
      <button type="button" data-testid="workspace-tab-menu-close-right" role="menuitem" @click="selectContextCommand('close-right')">
        关闭右侧
      </button>
      <button type="button" data-testid="workspace-tab-menu-close-others" role="menuitem" @click="selectContextCommand('close-others')">
        关闭其他
      </button>
      <button type="button" data-testid="workspace-tab-menu-close-all" role="menuitem" @click="selectContextCommand('close-all')">
        关闭全部
      </button>
      <button
        v-if="contextTab?.kind === 'business'"
        type="button"
        data-testid="workspace-tab-menu-toggle-pin"
        role="menuitem"
        @click="selectContextCommand('toggle-pin')"
      >
        {{ contextTab?.pinned === true ? '取消固定' : '固定标签' }}
      </button>
      <button
        v-if="!props.focusMode"
        type="button"
        data-testid="workspace-tab-menu-focus"
        role="menuitem"
        @click="selectContextCommand('focus')"
      >
        当前页专注
      </button>
      <button
        v-else
        type="button"
        data-testid="workspace-tab-menu-focus-exit"
        role="menuitem"
        @click="selectContextCommand('focus-exit')"
      >
        退出专注
      </button>
    </div>
  </nav>
</template>

<style scoped>
.ip-pc-tabs {
  display: flex;
  flex: 0 0 auto;
  gap: var(--ip-space-1);
  height: var(--ip-shell-tabs-height);
  padding: var(--ip-space-1) var(--ip-space-2);
  overflow-x: auto;
  overflow-y: hidden;
  white-space: nowrap;
  background: var(--ip-shell-tabs-bg);
  border-bottom: 1px solid var(--ip-color-border);
}

.ip-pc-tabs__item {
  display: inline-flex;
  flex: 0 0 auto;
  align-items: center;
  height: 28px;
  max-width: 200px;
  padding: 0 var(--ip-space-1) 0 var(--ip-space-3);
  background: var(--ip-color-bg-muted);
  border: 1px solid var(--ip-color-border);
  border-radius: var(--ip-radius-md);
}

.ip-pc-tabs__item--active {
  background: var(--ip-color-primary-bg);
  border-color: var(--ip-color-primary);
  color: var(--ip-color-primary);
}

.ip-pc-tabs__menu-search {
  flex: 0 0
    calc(var(--ip-shell-toolrail-width) + var(--ip-shell-functiontree-width) - var(--ip-space-2));
  width: calc(
    var(--ip-shell-toolrail-width) + var(--ip-shell-functiontree-width) - var(--ip-space-2)
  );
}

.ip-pc-tabs__menu-search :deep(.el-select__wrapper) {
  min-height: 28px;
  border-radius: var(--ip-radius-md);
  box-shadow: 0 0 0 1px var(--ip-color-border) inset;
}

.ip-pc-tabs__tab {
  flex: 0 1 auto;
  min-width: 0;
  padding: 0;
  overflow: hidden;
  color: inherit;
  background: transparent;
  border: 0;
  cursor: pointer;
  font-size: var(--ip-font-size-md);
  text-overflow: ellipsis;
  white-space: nowrap;
}

.ip-pc-tabs__close {
  display: inline-flex;
  flex: 0 0 auto;
  align-items: center;
  justify-content: center;
  width: 22px;
  height: 22px;
  padding: 0;
  color: var(--ip-color-text-secondary);
  background: transparent;
  border: 0;
  border-radius: var(--ip-radius-sm);
  cursor: pointer;
}

.ip-pc-tabs__close:hover {
  color: var(--ip-color-text-primary);
  background: var(--ip-color-bg-muted);
}

.ip-pc-tabs__tab:focus-visible,
.ip-pc-tabs__close:focus-visible {
  outline: 2px solid var(--ip-focus-ring-color);
  outline-offset: 1px;
}

.ip-pc-tabs__close {
  visibility: hidden;
}

.ip-pc-tabs__item--active .ip-pc-tabs__close,
.ip-pc-tabs__item:hover .ip-pc-tabs__close,
.ip-pc-tabs__close:focus-visible {
  visibility: visible;
}

.ip-pc-tabs__context-menu {
  position: fixed;
  z-index: 1300;
  display: grid;
  min-width: 128px;
  padding: var(--ip-space-1);
  background: var(--ip-color-bg-container);
  border: 1px solid var(--ip-color-border);
  border-radius: var(--ip-radius-sm);
  box-shadow: var(--ip-shadow-md);
}

.ip-pc-tabs__context-menu button {
  padding: var(--ip-space-2) var(--ip-space-3);
  color: var(--ip-color-text-primary);
  text-align: left;
  background: transparent;
  border: 0;
  border-radius: var(--ip-radius-sm);
  cursor: pointer;
}

.ip-pc-tabs__context-menu button:hover,
.ip-pc-tabs__context-menu button:focus-visible {
  background: var(--ip-color-bg-muted);
}

.ip-pc-tabs__context-menu button:disabled {
  color: var(--ip-color-text-disabled);
  cursor: not-allowed;
}
</style>
