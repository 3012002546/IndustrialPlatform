<script setup lang="ts">
import { computed } from 'vue'
import { RouterLink, useRoute } from 'vue-router'

import { useAuthStore } from '@/stores/authStore'
import type { NavigationItem } from './types'

const props = defineProps<{
  items: NavigationItem[]
  /** 侧栏是否折叠:折叠时隐藏文字,仅保留图标并通过 title 提示。 */
  collapsed: boolean
}>()

const authStore = useAuthStore()
const route = useRoute()

/** 权限过滤后的可见菜单(§13.2):未声明权限视为公开,声明但未持有则隐藏。 */
const visibleItems = computed(() =>
  props.items.filter(
    (item) => item.permission === undefined || authStore.hasPermission(item.permission),
  ),
)

/** 路由高亮(§14.2):当前路由名与菜单项 routeName 一致即为激活态。 */
function isActive(routeName: string): boolean {
  return route.name === routeName
}
</script>

<template>
  <nav class="ip-pc-nav" :class="{ 'ip-pc-nav--collapsed': collapsed }" aria-label="主导航">
    <ul class="ip-pc-nav__list">
      <li v-for="item in visibleItems" :key="item.id" class="ip-pc-nav__item">
        <RouterLink
          :to="{ name: item.routeName }"
          class="ip-pc-nav__link"
          :class="{ 'ip-pc-nav__link--active': isActive(item.routeName) }"
          :aria-current="isActive(item.routeName) ? 'page' : undefined"
          :title="collapsed ? item.label : undefined"
        >
          <span class="ip-pc-nav__icon" aria-hidden="true">{{ item.icon ?? '•' }}</span>
          <span class="ip-pc-nav__label">{{ item.label }}</span>
        </RouterLink>
      </li>
    </ul>
  </nav>
</template>

<style scoped>
.ip-pc-nav__list {
  margin: 0;
  padding: var(--ip-space-2);
  list-style: none;
}

.ip-pc-nav__item + .ip-pc-nav__item {
  margin-top: var(--ip-space-1);
}

.ip-pc-nav__link {
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

.ip-pc-nav__link:hover {
  background: var(--ip-color-bg-muted);
  color: var(--ip-color-text-primary);
}

.ip-pc-nav__link--active,
.ip-pc-nav__link--active:hover {
  background: var(--ip-color-primary-bg);
  color: var(--ip-color-primary);
  font-weight: 600;
}

.ip-pc-nav__icon {
  display: inline-flex;
  flex: 0 0 auto;
  align-items: center;
  justify-content: center;
  width: 20px;
  font-size: var(--ip-font-size-lg);
}

/* 折叠态:隐藏文字,仅保留图标;hover 时通过 title 提示全名。 */
.ip-pc-nav--collapsed .ip-pc-nav__label {
  display: none;
}
</style>
