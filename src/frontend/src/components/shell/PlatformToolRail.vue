<script setup lang="ts">
/**
 * PlatformToolRail(PF-01 §7.8/§6.2):PC 固定工具轨,宽 52px,始终保留。
 * 只显示一级平台分组(PlatformToolRail 管理当前分组),不承载任意深度菜单树。
 * 选中状态同时使用背景、侧边标记与字重变化;图标按钮具有可读名称、Tooltip 与键盘焦点。
 */

import type { NavigationGroup } from '@/components/navigation/types'

const props = defineProps<{
  groups: readonly NavigationGroup[]
  /** 当前分组 id(受控状态,由父级 PcLayout 持有)。 */
  activeGroupId: string
}>()

const emit = defineEmits<{
  'update:activeGroupId': [id: string]
}>()

function select(id: string): void {
  if (id === props.activeGroupId) return
  emit('update:activeGroupId', id)
}
</script>

<template>
  <nav class="ip-toolrail" aria-label="平台分组">
    <ul class="ip-toolrail__list">
      <li v-for="group in groups" :key="group.id" class="ip-toolrail__item">
        <button
          type="button"
          class="ip-toolrail__button"
          :class="{ 'ip-toolrail__button--active': group.id === activeGroupId }"
          :aria-current="group.id === activeGroupId ? 'page' : undefined"
          :aria-pressed="group.id === activeGroupId"
          :title="group.label"
          :aria-label="group.label"
          @click="select(group.id)"
        >
          <component :is="group.icon" class="ip-toolrail__icon" aria-hidden="true" />
        </button>
      </li>
    </ul>
  </nav>
</template>

<style scoped>
.ip-toolrail {
  display: flex;
  flex: 0 0 auto;
  width: var(--ip-shell-toolrail-width);
  background: var(--ip-shell-toolrail-bg);
  border-right: 1px solid var(--ip-color-border);
}

.ip-toolrail__list {
  display: flex;
  flex-direction: column;
  width: 100%;
  margin: 0;
  padding: var(--ip-space-2) 0;
  list-style: none;
}

.ip-toolrail__item {
  padding: var(--ip-space-1) var(--ip-space-2);
}

.ip-toolrail__button {
  position: relative;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 100%;
  height: 40px;
  padding: 0;
  color: var(--ip-color-text-secondary);
  background: transparent;
  border: 0;
  border-radius: var(--ip-radius-md);
  cursor: pointer;
}

.ip-toolrail__button::before {
  /* 侧边标记:选中时显示,强化选中语义(不只依赖图标颜色)。 */
  content: '';
  position: absolute;
  left: -8px;
  top: 50%;
  width: 3px;
  height: 0;
  border-radius: var(--ip-radius-full);
  background: var(--ip-color-primary);
  transform: translateY(-50%);
  transition: height 150ms ease;
}

.ip-toolrail__button:hover {
  color: var(--ip-color-text-primary);
  background: var(--ip-color-bg-muted);
}

.ip-toolrail__button--active,
.ip-toolrail__button--active:hover {
  color: var(--ip-color-primary);
  background: var(--ip-color-primary-bg);
  font-weight: 600;
}

.ip-toolrail__button--active::before {
  height: 24px;
}

.ip-toolrail__icon {
  width: 20px;
  height: 20px;
}

.ip-toolrail__button:focus-visible {
  outline: 2px solid var(--ip-focus-ring-color);
  outline-offset: 1px;
}

@media (prefers-reduced-motion: reduce) {
  .ip-toolrail__button::before {
    transition: none;
  }
}
</style>
