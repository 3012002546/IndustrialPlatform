<script setup lang="ts">
/**
 * PlatformFunctionTree(PF-01 §7.8/§6.3):PC 功能树,展开宽 216px,可完全收起。
 * 只渲染当前组的授权 items;收起状态由 ThemeStore.pcFunctionTreeCollapsed 持久化,
 * 本组件不直接读写 localStorage(旧侧栏键迁移由 TASK-PF01-001 负责)。
 */

import { computed, ref, watch } from 'vue'
import { RouterLink, useRoute } from 'vue-router'

import type { NavigationItem, NavigationSection } from '@/components/navigation/types'
import { useAuthStore } from '@/stores/authStore'
import { useThemeStore } from '@/stores/themeStore'
import { localeMessages, resolveLocaleMessage } from '@/localization/i18n'
import { usePlatformLocale } from '@/localization/localeContext'

const props = defineProps<{
  /** 功能树 aria 标签(区分所在分组)。 */
  label: string
  labelKey?: string | undefined
  /** 当前分组的授权 items(由父级过滤后传入)。 */
  items: readonly NavigationItem[]
  /** 当前分组内由真实导航数据声明的二级分组。 */
  sections?: readonly NavigationSection[]
}>()

const authStore = useAuthStore()
const themeStore = useThemeStore()
const locale = usePlatformLocale()
const copy = computed(() => localeMessages[locale.value].shell.copy)
const route = useRoute()
const openMenuId = ref<string | null>(null)
const menuQuery = ref('')

const treeLabel = computed(() => resolveLocaleMessage(locale.value, props.labelKey, props.label))

function itemLabel(item: NavigationItem): string {
  return resolveLocaleMessage(locale.value, item.labelKey, item.fallbackLabel ?? item.label)
}

function sectionLabel(section: NavigationSection): string {
  return resolveLocaleMessage(locale.value, section.labelKey, section.fallbackLabel ?? section.label)
}

/** 功能树收起状态来自 ThemeStore(§7.8),不直接读写 localStorage。 */
const collapsed = computed(
  () => themeStore.navigationMode !== 'expanded' || themeStore.preferences.pcFunctionTreeCollapsed,
)

/** 权限过滤后的可见菜单(§13.2):未声明权限视为公开,声明但未持有则隐藏。 */
function hasAccess(item: NavigationItem): boolean {
  return (
    (item.permission === undefined || authStore.hasPermission(item.permission)) &&
    (item.anyPermissions === undefined ||
      item.anyPermissions.some((permission) => authStore.hasPermission(permission)))
  )
}

const visibleItems = computed(() =>
  props.items.filter((item) => {
    if (!hasAccess(item)) return false
    // 收起后搜索框不可见;恢复完整图标入口,避免无结果筛选把导航变成死路。
    const query = collapsed.value ? '' : menuQuery.value.trim().toLocaleLowerCase()
    if (query === '') return true
    return itemLabel(item).toLocaleLowerCase().includes(query)
  }),
)

const visibleSections = computed(() => {
  const sections = props.sections ?? []
  if (sections.length === 0) {
    return [{ key: 'default', definition: undefined, items: visibleItems.value }]
  }

  const itemsBySection = new Map<string, NavigationItem[]>()
  const ungrouped: NavigationItem[] = []
  for (const item of visibleItems.value) {
    const sectionId = item.sectionId
    if (sectionId === undefined || !sections.some((section) => section.id === sectionId)) {
      ungrouped.push(item)
      continue
    }
    const sectionItems = itemsBySection.get(sectionId) ?? []
    sectionItems.push(item)
    itemsBySection.set(sectionId, sectionItems)
  }

  return [
    ...sections
      .map((section) => ({
        key: section.id,
        definition: section,
        items: itemsBySection.get(section.id) ?? [],
      }))
      .filter((section) => section.items.length > 0),
    ...(ungrouped.length > 0
      ? [{ key: 'ungrouped', definition: undefined, items: ungrouped }]
      : []),
  ]
})

function toggle(): void {
  themeStore.setPcFunctionTreeCollapsed(!collapsed.value)
}

/** 路由高亮(§14.2):当前路由名与菜单项 routeName 一致即为激活态。 */
function isActive(routeName: string): boolean {
  return route.name === routeName
}

function isItemActive(item: NavigationItem): boolean {
  return (
    isActive(item.routeName) || (item.children?.some((child) => isActive(child.routeName)) ?? false)
  )
}

function visibleChildren(item: NavigationItem): readonly NavigationItem[] {
  return (item.children ?? []).filter((child) => hasAccess(child))
}

function toggleSubmenu(itemId: string): void {
  openMenuId.value = openMenuId.value === itemId ? null : itemId
}

function closeSubmenu(): void {
  openMenuId.value = null
}

watch(
  () => route.name,
  () => closeSubmenu(),
)
</script>

<template>
  <nav
    class="ip-function-tree"
    :class="{ 'ip-function-tree--collapsed': collapsed }"
    :aria-label="treeLabel"
  >
    <div class="ip-function-tree__header">
      <span class="ip-function-tree__title">{{ treeLabel }}</span>
      <button
        type="button"
        class="ip-function-tree__toggle"
        :aria-expanded="!collapsed"
        aria-controls="ip-function-tree-list"
        data-testid="function-tree-toggle"
        :title="collapsed ? copy.expandFunctionTree : copy.collapseFunctionTree"
        @click="toggle"
      >
        <span class="ip-function-tree__toggle-icon" aria-hidden="true">{{
          collapsed ? '»' : '«'
        }}</span>
        <span class="ip-function-tree__visually-hidden">{{
          collapsed ? copy.expandFunctionTree : copy.collapseFunctionTree
        }}</span>
      </button>
    </div>
    <div
      class="ip-function-tree__surface"
      :class="{ 'ip-function-tree__surface--collapsed': collapsed }"
    >
      <input
        v-if="!collapsed"
        v-model="menuQuery"
        class="ip-function-tree__search"
        type="search"
        :placeholder="copy.menuSearchHint"
        :aria-label="copy.menuSearchHint"
        @keydown.esc="menuQuery = ''"
      />

      <ul id="ip-function-tree-list" class="ip-function-tree__list">
      <template v-for="section in visibleSections" :key="section.key">
        <li v-if="section.definition !== undefined" class="ip-function-tree__section">
          {{ sectionLabel(section.definition) }}
        </li>
      <li v-for="item in section.items" :key="item.id" class="ip-function-tree__item">
        <template v-if="collapsed && visibleChildren(item).length > 0">
          <button
            type="button"
            class="ip-function-tree__link ip-function-tree__link--collapsed-parent"
            :class="{ 'ip-function-tree__link--active': isItemActive(item) }"
            :aria-label="itemLabel(item)"
            :title="itemLabel(item)"
            aria-haspopup="menu"
            :aria-expanded="openMenuId === item.id"
            :data-testid="`function-tree-parent-${item.id}`"
            @click="toggleSubmenu(item.id)"
            @keydown.esc="closeSubmenu"
          >
            <span v-if="item.icon" class="ip-function-tree__icon" aria-hidden="true">
              <component :is="item.icon" />
            </span>
            <span v-if="!collapsed" class="ip-function-tree__label">{{ itemLabel(item) }}</span>
          </button>
          <div
            v-if="openMenuId === item.id"
            class="ip-function-tree__popover"
            role="menu"
            :data-testid="`function-tree-popover-${item.id}`"
          >
            <RouterLink
              v-for="child in visibleChildren(item)"
              :key="child.id"
              :to="{
                name: child.routeName,
                ...(child.routeQuery === undefined ? {} : { query: child.routeQuery }),
              }"
              class="ip-function-tree__popover-link"
              role="menuitem"
              :aria-current="isActive(child.routeName) ? 'page' : undefined"
              @click="closeSubmenu"
            >
              <span v-if="child.icon" class="ip-function-tree__icon" aria-hidden="true">
                <component :is="child.icon" />
              </span>
              <span>{{ itemLabel(child) }}</span>
            </RouterLink>
          </div>
        </template>
        <template v-else-if="visibleChildren(item).length === 0">
          <RouterLink
            :to="{
              name: item.routeName,
              ...(item.routeQuery === undefined ? {} : { query: item.routeQuery }),
            }"
            class="ip-function-tree__link"
            :class="{ 'ip-function-tree__link--active': isActive(item.routeName) }"
            :aria-current="isActive(item.routeName) ? 'page' : undefined"
            :aria-label="collapsed ? itemLabel(item) : undefined"
            :title="collapsed ? itemLabel(item) : undefined"
          >
            <span v-if="item.icon" class="ip-function-tree__icon" aria-hidden="true">
              <component :is="item.icon" />
            </span>
            <span v-if="!collapsed" class="ip-function-tree__label">{{ itemLabel(item) }}</span>
          </RouterLink>
        </template>
        <template v-else>
          <div class="ip-function-tree__parent">
            <span class="ip-function-tree__link ip-function-tree__link--parent">
              <span v-if="item.icon" class="ip-function-tree__icon" aria-hidden="true">
                <component :is="item.icon" />
              </span>
              <span class="ip-function-tree__label">{{ itemLabel(item) }}</span>
            </span>
            <ul class="ip-function-tree__children">
              <li v-for="child in visibleChildren(item)" :key="child.id">
                <RouterLink
                  :to="{
                    name: child.routeName,
                    ...(child.routeQuery === undefined ? {} : { query: child.routeQuery }),
                  }"
                  class="ip-function-tree__link ip-function-tree__link--child"
                  :class="{ 'ip-function-tree__link--active': isActive(child.routeName) }"
                  :aria-current="isActive(child.routeName) ? 'page' : undefined"
                >
                  <span v-if="child.icon" class="ip-function-tree__icon" aria-hidden="true">
                    <component :is="child.icon" />
                  </span>
                  <span class="ip-function-tree__label">{{ itemLabel(child) }}</span>
                </RouterLink>
              </li>
            </ul>
          </div>
        </template>
      </li>
      </template>
      </ul>
    </div>
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
  width: var(--ip-shell-toolrail-width-compact);
  overflow: visible;
}

.ip-function-tree--collapsed .ip-function-tree__header {
  justify-content: center;
  padding: 0;
}

.ip-function-tree--collapsed .ip-function-tree__title {
  display: none;
}

.ip-function-tree__header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: var(--ip-space-2);
  min-height: 54px;
  padding: 0 18px;
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

.ip-function-tree__surface {
  display: flex;
  flex: 1 1 auto;
  min-width: 0;
  min-height: 0;
  flex-direction: column;
  margin: 0;
  overflow: hidden;
  background: var(--ip-color-bg-container);
  border: 0;
  border-radius: 0;
}

.ip-function-tree__surface--collapsed {
  margin: var(--ip-space-2) 0;
  overflow: visible;
  border: 0;
  background: transparent;
}

.ip-function-tree__search {
  flex: 0 0 auto;
  min-height: 32px;
  margin: 0 var(--ip-space-3) var(--ip-space-2);
  padding: 0 var(--ip-space-2);
  color: var(--ip-color-text-primary);
  background: var(--ip-color-bg-page);
  border: 1px solid var(--ip-color-border);
  border-radius: var(--ip-radius-sm);
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
  min-width: 0;
  margin: 0;
  padding: 0 var(--ip-space-2) var(--ip-space-2);
  overflow-x: hidden;
  overflow-y: auto;
  list-style: none;
}

.ip-function-tree__section {
  max-width: 100%;
  margin: 12px var(--ip-space-3) 4px;
  overflow: hidden;
  color: var(--ip-color-text-secondary);
  font-size: var(--ip-font-size-xs);
  font-weight: 500;
  line-height: 18px;
  white-space: nowrap;
}

.ip-function-tree__section:first-child {
  margin-top: 12px;
}

.ip-function-tree--collapsed .ip-function-tree__list {
  display: flex;
  flex-direction: column;
  padding: var(--ip-space-2) 0;
  overflow: visible;
}

.ip-function-tree--collapsed .ip-function-tree__section {
  display: none;
}

.ip-function-tree--collapsed .ip-function-tree__item {
  position: relative;
  margin-top: var(--ip-space-1);
}

.ip-function-tree--collapsed .ip-function-tree__link {
  justify-content: center;
  gap: 0;
  padding: 0;
}

.ip-function-tree--collapsed .ip-function-tree__label {
  display: none;
}

.ip-function-tree__item + .ip-function-tree__item {
  margin-top: 2px;
}

.ip-function-tree__link {
  display: flex;
  align-items: center;
  gap: 10px;
  min-width: 0;
  min-height: 36px;
  padding: 0 var(--ip-space-3);
  border-radius: var(--ip-radius-md);
  color: var(--ip-color-text-secondary);
  font-size: var(--ip-font-size-sm);
  line-height: var(--ip-line-height-normal);
  font-family: inherit;
  text-decoration: none;
  white-space: nowrap;
}

.ip-function-tree__label {
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

button.ip-function-tree__link {
  background: transparent;
  border: 0;
  cursor: pointer;
  font-size: inherit;
  text-align: left;
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

.ip-function-tree__parent {
  display: flex;
  min-width: 0;
  flex-direction: column;
}

.ip-function-tree__link--parent {
  color: var(--ip-color-text-primary);
  font-weight: 600;
}

.ip-function-tree__children {
  margin: 0;
  padding: 0 0 0 var(--ip-space-4);
  list-style: none;
}

.ip-function-tree__link--child {
  min-height: 36px;
  font-size: var(--ip-font-size-sm);
}

.ip-function-tree__popover {
  position: absolute;
  z-index: 20;
  top: 0;
  left: calc(100% + var(--ip-space-2));
  display: flex;
  min-width: 180px;
  flex-direction: column;
  gap: 2px;
  padding: var(--ip-space-2);
  background: var(--ip-color-bg-container);
  border: 1px solid var(--ip-color-border);
  border-radius: var(--ip-radius-md);
  box-shadow: var(--ip-shadow-md);
}

.ip-function-tree__popover-link {
  display: flex;
  align-items: center;
  gap: var(--ip-space-2);
  min-height: 40px;
  padding: 0 var(--ip-space-3);
  border-radius: var(--ip-radius-md);
  color: var(--ip-color-text-secondary);
  font-size: var(--ip-font-size-sm);
  text-decoration: none;
  white-space: nowrap;
}

.ip-function-tree__popover-link:hover,
.ip-function-tree__popover-link:focus-visible,
.ip-function-tree__popover-link[aria-current='page'] {
  background: var(--ip-color-primary-bg);
  color: var(--ip-color-primary);
}

.ip-function-tree__popover-link:focus-visible {
  outline: 2px solid var(--ip-focus-ring-color);
  outline-offset: -1px;
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
