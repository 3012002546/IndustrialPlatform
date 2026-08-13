<script setup lang="ts">
/**
 * PlatformFunctionTree(PF-01 §7.8/§6.3):PC 功能树,展开宽 216px,可完全收起。
 * 只渲染当前组的授权 items;收起状态由 ThemeStore.pcFunctionTreeCollapsed 持久化,
 * 本组件不直接读写 localStorage(旧侧栏键迁移由 TASK-PF01-001 负责)。
 */

import { computed } from 'vue'
import { RouterLink, useRoute } from 'vue-router'

import type { NavigationItem } from '@/components/navigation/types'
import { useAuthStore } from '@/stores/authStore'
import { useThemeStore } from '@/stores/themeStore'

const props = defineProps<{
  /** 功能树 aria 标签(区分所在分组)。 */
  label: string
  /** 当前分组的授权 items(由父级过滤后传入)。 */
  items: readonly NavigationItem[]
}>()

const authStore = useAuthStore()
const themeStore = useThemeStore()
const route = useRoute()

/** 权限过滤后的可见菜单(§13.2):未声明权限视为公开,声明但未持有则隐藏。 */
const visibleItems = computed(() =>
  props.items.filter(
    (item) => item.permission === undefined || authStore.hasPermission(item.permission),
  ),
)

/** 功能树收起状态来自 ThemeStore(§7.8),不直接读写 localStorage。 */
const collapsed = computed(() => themeStore.preferences.pcFunctionTreeCollapsed)

function toggle(): void {
  themeStore.setPcFunctionTreeCollapsed(!collapsed.value)
}

/** 路由高亮(§14.2):当前路由名与菜单项 routeName 一致即为激活态。 */
function isActive(routeName: string): boolean {
  return route.name === routeName
}
</script>

<template>
  <nav
    class="ip-function-tree"
    :class="{ 'ip-function-tree--collapsed': collapsed }"
    :aria-label="label"
  >
    <div class="ip-function-tree__header">
      <span class="ip-function-tree__title">{{ label }}</span>
      <button
        type="button"
        class="ip-function-tree__toggle"
        :aria-expanded="!collapsed"
        aria-controls="ip-function-tree-list"
        data-testid="function-tree-toggle"
        :title="collapsed ? '展开功能树' : '收起功能树'"
        @click="toggle"
      >
        <span class="ip-function-tree__toggle-icon" aria-hidden="true">{{
          collapsed ? '»' : '«'
        }}</span>
        <span class="ip-function-tree__visually-hidden">{{
          collapsed ? '展开功能树' : '收起功能树'
        }}</span>
      </button>
    </div>

    <ul v-if="!collapsed" id="ip-function-tree-list" class="ip-function-tree__list">
      <li v-for="item in visibleItems" :key="item.id" class="ip-function-tree__item">
        <RouterLink
          :to="{ name: item.routeName }"
          class="ip-function-tree__link"
          :class="{ 'ip-function-tree__link--active': isActive(item.routeName) }"
          :aria-current="isActive(item.routeName) ? 'page' : undefined"
        >
          <span v-if="item.icon" class="ip-function-tree__icon" aria-hidden="true">
            <component :is="item.icon" />
          </span>
          <span class="ip-function-tree__label">{{ item.label }}</span>
        </RouterLink>
      </li>
    </ul>
  </nav>
</template>

<style scoped>
.ip-function-tree {
  display: flex;
  flex-direction: column;
  flex: 0 0 auto;
  width: var(--ip-shell-functiontree-width);
  background: var(--ip-shell-functiontree-bg);
  border-right: 1px solid var(--ip-color-border);
  transition: width 150ms ease;
}

.ip-function-tree--collapsed {
  width: 0;
  overflow: hidden;
  border-right-width: 0;
}

.ip-function-tree__header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: var(--ip-space-2);
  min-height: var(--ip-shell-topbar-height);
  padding: 0 var(--ip-space-3);
  border-bottom: 1px solid var(--ip-color-border);
}

.ip-function-tree__title {
  overflow: hidden;
  color: var(--ip-color-text-secondary);
  font-size: var(--ip-font-size-sm);
  font-weight: 600;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.ip-function-tree__toggle {
  display: inline-flex;
  flex: 0 0 auto;
  align-items: center;
  justify-content: center;
  width: 28px;
  height: 28px;
  padding: 0;
  color: var(--ip-color-text-secondary);
  background: transparent;
  border: 0;
  border-radius: var(--ip-radius-md);
  cursor: pointer;
  font-size: var(--ip-font-size-md);
}

.ip-function-tree__toggle:hover {
  color: var(--ip-color-text-primary);
  background: var(--ip-color-bg-muted);
}

.ip-function-tree__toggle:focus-visible {
  outline: 2px solid var(--ip-focus-ring-color);
  outline-offset: 1px;
}

.ip-function-tree__toggle-icon {
  line-height: 1;
}

.ip-function-tree__list {
  flex: 1 1 auto;
  margin: 0;
  padding: var(--ip-space-2);
  overflow-y: auto;
  list-style: none;
}

.ip-function-tree__item + .ip-function-tree__item {
  margin-top: var(--ip-space-1);
}

.ip-function-tree__link {
  display: flex;
  align-items: center;
  gap: var(--ip-space-3);
  min-height: 40px;
  padding: 0 var(--ip-space-3);
  border-radius: var(--ip-radius-md);
  color: var(--ip-color-text-secondary);
  font-size: var(--ip-font-size-md);
  line-height: var(--ip-line-height-normal);
  text-decoration: none;
  white-space: nowrap;
}

.ip-function-tree__link:hover {
  background: var(--ip-color-bg-muted);
  color: var(--ip-color-text-primary);
}

.ip-function-tree__link--active,
.ip-function-tree__link--active:hover {
  background: var(--ip-color-primary-bg);
  color: var(--ip-color-primary);
  font-weight: 600;
}

.ip-function-tree__icon {
  display: inline-flex;
  flex: 0 0 auto;
  align-items: center;
  justify-content: center;
  width: 20px;
  color: currentColor;
}

.ip-function-tree__icon :deep(svg) {
  width: 18px;
  height: 18px;
}

.ip-function-tree__visually-hidden {
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
  .ip-function-tree {
    transition: none;
  }
}
</style>
