<script setup lang="ts">
/**
 * PcWorkspaceTabs(PF-01 §7.9/§10.1):36px PC 业务标签栏。
 * 固定工作台恒在第 0 位且不可关闭;业务标签可关闭/关闭其他/关闭右侧/重新加载。
 * 溢出单行横向滚动;所有控件为原生按钮 + ElDropdown,键盘可达。
 * 组件为展示层:数据来自 WorkspaceTabsStore,动作 emit 给 PcLayout 处理导航。
 */

import { computed, ref } from 'vue'
import { Search } from '@element-plus/icons-vue'
import { ElDropdown, ElDropdownItem, ElDropdownMenu } from 'element-plus'

import { pcNavigationGroups } from '@/components/navigation/navigation'
import { useAuthStore } from '@/stores/authStore'
import { useWorkspaceTabsStore } from '@/stores/workspaceTabsStore'
import type { WorkspaceTab } from '@/workspace'

const emit = defineEmits<{
  activate: [tabId: string]
  close: [tabId: string]
  'close-others': [tabId: string]
  'close-right': [tabId: string]
  reload: [tabId: string]
  'menu-select': [routeName: string]
}>()

const tabsStore = useWorkspaceTabsStore()
const authStore = useAuthStore()
const selectedMenu = ref('')

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

function close(tabId: string): void {
  emit('close', tabId)
}

function onMenuCommand(tab: WorkspaceTab, command: unknown): void {
  if (command === 'close-others') emit('close-others', tab.id)
  else if (command === 'close-right') emit('close-right', tab.id)
  else if (command === 'reload') emit('reload', tab.id)
}
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
      >
        <button
          type="button"
          class="ip-pc-tabs__tab"
          role="tab"
          :aria-selected="tab.id === tabsStore.activeTabId"
          :aria-label="tab.title"
          :title="tab.title"
          @click="activate(tab)"
        >
          {{ tab.title }}
        </button>

        <template v-if="tab.kind === 'business'">
          <button
            type="button"
            class="ip-pc-tabs__close"
            :aria-label="`关闭 ${tab.title}`"
            title="关闭"
            @click="close(tab.id)"
          >
            ×
          </button>
          <ElDropdown trigger="click" @command="(cmd) => onMenuCommand(tab, cmd)">
            <button
              type="button"
              class="ip-pc-tabs__more"
              :aria-label="`${tab.title} 更多操作`"
              title="更多操作"
            >
              ⋯
            </button>
            <template #dropdown>
              <ElDropdownMenu>
                <ElDropdownItem command="close-others">关闭其他</ElDropdownItem>
                <ElDropdownItem command="close-right">关闭右侧</ElDropdownItem>
                <ElDropdownItem command="reload">重新加载</ElDropdownItem>
              </ElDropdownMenu>
            </template>
          </ElDropdown>
        </template>
      </div>
    </template>
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

.ip-pc-tabs__close,
.ip-pc-tabs__more {
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

.ip-pc-tabs__close:hover,
.ip-pc-tabs__more:hover {
  color: var(--ip-color-text-primary);
  background: var(--ip-color-bg-muted);
}

.ip-pc-tabs__tab:focus-visible,
.ip-pc-tabs__close:focus-visible,
.ip-pc-tabs__more:focus-visible {
  outline: 2px solid var(--ip-focus-ring-color);
  outline-offset: 1px;
}
</style>
