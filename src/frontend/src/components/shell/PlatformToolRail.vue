<script setup lang="ts">
/**
 * PlatformToolRail(PF-01 §7.8/§6.2):PC 固定工具轨,宽 52px,始终保留。
 * 只显示一级平台分组(PlatformToolRail 管理当前分组),不承载任意深度菜单树。
 * 选中状态同时使用背景、侧边标记与字重变化;图标按钮具有可读名称、Tooltip 与键盘焦点。
 */

import type { NavigationGroup } from '@/components/navigation/types'
import { computed, nextTick, onBeforeUnmount, onMounted, ref } from 'vue'
import { MoreFilled } from '@element-plus/icons-vue'
import { localeMessages, resolveLocaleMessage } from '@/localization/i18n'
import { usePlatformLocale } from '@/localization/localeContext'

const props = withDefaults(defineProps<{
  groups: readonly NavigationGroup[]
  /** 当前分组 id(受控状态,由父级 PcLayout 持有)。 */
  activeGroupId: string
  mode?: 'expanded' | 'secondary-collapsed' | 'compact'
}>(), { mode: 'expanded' })

const emit = defineEmits<{
  'update:activeGroupId': [id: string]
}>()
const locale = usePlatformLocale()
const copy = computed(() => localeMessages[locale.value].shell.copy)
const topCopy = computed(() => localeMessages[locale.value].shell.top)
const railRef = ref<HTMLElement | null>(null)
const moreOpen = ref(false)
const moreButtonRef = ref<HTMLButtonElement | null>(null)
const availableHeight = ref(0)
let resizeObserver: ResizeObserver | undefined

const ITEM_HEIGHT = 62
const MORE_HEIGHT = 50

function groupLabel(group: NavigationGroup): string {
  return resolveLocaleMessage(locale.value, group.labelKey, group.fallbackLabel ?? group.label)
}

const visibleCount = computed(() => {
  if (props.groups.length === 0 || availableHeight.value <= 0) return props.groups.length
  const padding = 16
  const capacity = Math.max(1, Math.floor((availableHeight.value - padding - MORE_HEIGHT) / ITEM_HEIGHT))
  return Math.min(props.groups.length, capacity)
})

const visibleGroups = computed(() => props.groups.slice(0, visibleCount.value))
const moreGroups = computed(() => props.groups.slice(visibleCount.value))
const menuGroups = computed(() => (moreGroups.value.length > 0 ? moreGroups.value : props.groups))
const activeInMore = computed(() => menuGroups.value.some((group) => group.id === props.activeGroupId))

function select(id: string): void {
  moreOpen.value = false
  if (id === props.activeGroupId) return
  emit('update:activeGroupId', id)
}

function toggleMore(): void {
  moreOpen.value = !moreOpen.value
  if (moreOpen.value) void nextTick(() => moreButtonRef.value?.focus())
}

function onDocumentKeydown(event: KeyboardEvent): void {
  if (event.key !== 'Escape' || !moreOpen.value) return
  moreOpen.value = false
  moreButtonRef.value?.focus()
}

function onDocumentPointerdown(event: PointerEvent): void {
  if (!railRef.value?.contains(event.target as Node)) moreOpen.value = false
}

onMounted(() => {
  const element = railRef.value
  if (element !== null && typeof ResizeObserver !== 'undefined') {
    resizeObserver = new ResizeObserver(([entry]) => {
      availableHeight.value = entry?.contentRect.height ?? 0
    })
    resizeObserver.observe(element)
    availableHeight.value = element.getBoundingClientRect().height
  }
  document.addEventListener('keydown', onDocumentKeydown)
  document.addEventListener('pointerdown', onDocumentPointerdown)
})

onBeforeUnmount(() => {
  resizeObserver?.disconnect()
  document.removeEventListener('keydown', onDocumentKeydown)
  document.removeEventListener('pointerdown', onDocumentPointerdown)
})
</script>

<template>
  <nav ref="railRef" class="ip-toolrail" :class="{ 'ip-toolrail--compact': props.mode === 'compact' }" :aria-label="copy.platformGroups">
    <ul class="ip-toolrail__list">
      <li v-for="group in visibleGroups" :key="group.id" class="ip-toolrail__item">
        <button
          type="button"
          class="ip-toolrail__button"
          :class="{ 'ip-toolrail__button--active': group.id === activeGroupId }"
          :aria-current="group.id === activeGroupId ? 'page' : undefined"
          :aria-pressed="group.id === activeGroupId"
          :title="groupLabel(group)"
          :aria-label="groupLabel(group)"
          @click="select(group.id)"
        >
          <component :is="group.icon" class="ip-toolrail__icon" aria-hidden="true" />
          <span v-if="props.mode !== 'compact'" class="ip-toolrail__label">{{ groupLabel(group) }}</span>
        </button>
      </li>
      <li class="ip-toolrail__item ip-toolrail__more-item">
        <button
          ref="moreButtonRef"
          type="button"
          class="ip-toolrail__button ip-toolrail__more-button"
          :class="{ 'ip-toolrail__button--active': activeInMore }"
          :aria-label="topCopy.moreNavigation"
          :title="topCopy.moreNavigation"
          :aria-expanded="moreOpen"
          aria-haspopup="menu"
          data-testid="toolrail-more"
          @click="toggleMore"
        >
          <MoreFilled class="ip-toolrail__more-icon" aria-hidden="true" />
          <span v-if="props.mode !== 'compact'" class="ip-toolrail__label">{{ topCopy.more }}</span>
        </button>
        <div v-if="moreOpen" class="ip-toolrail__more-menu" role="menu" data-testid="toolrail-more-menu">
          <button
            v-for="group in menuGroups"
            :key="group.id"
            type="button"
            role="menuitem"
            class="ip-toolrail__more-menu-item"
            :class="{ 'ip-toolrail__more-menu-item--active': group.id === activeGroupId }"
            :aria-current="group.id === activeGroupId ? 'page' : undefined"
            @click="select(group.id)"
          >
            <component :is="group.icon" aria-hidden="true" />
            <span>{{ groupLabel(group) }}</span>
          </button>
        </div>
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

.ip-toolrail--compact {
  width: var(--ip-shell-toolrail-width-compact);
}

.ip-toolrail__list {
  display: flex;
  flex-direction: column;
  min-height: 0;
  width: 100%;
  margin: 0;
  padding: 14px 7px 10px;
  list-style: none;
}

.ip-toolrail__item {
  padding: 0;
}

.ip-toolrail__more-item {
  position: relative;
  margin-top: auto;
}

.ip-toolrail__button {
  position: relative;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 100%;
  min-height: 62px;
  padding: 0;
  flex-direction: column;
  gap: 6px;
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
  width: 21px;
  height: 21px;
}

.ip-toolrail__label {
  display: -webkit-box;
  overflow: hidden;
  max-width: 48px;
  font-size: 11px;
  line-height: 1.2;
  text-align: center;
  -webkit-box-orient: vertical;
  -webkit-line-clamp: 2;
}

.ip-toolrail__more-icon {
  width: 21px;
  height: 21px;
}

.ip-toolrail__more-button {
  min-height: 50px;
}

.ip-toolrail__more-menu {
  position: absolute;
  z-index: 30;
  bottom: var(--ip-space-1);
  left: calc(100% + var(--ip-space-2));
  display: grid;
  min-width: 190px;
  gap: 2px;
  padding: var(--ip-space-1);
  background: var(--ip-color-bg-container);
  border: 1px solid var(--ip-color-border);
  border-radius: var(--ip-radius-md);
  box-shadow: var(--ip-shadow-md);
}

.ip-toolrail__more-menu-item {
  display: flex;
  align-items: center;
  gap: var(--ip-space-2);
  min-height: 36px;
  padding: 0 var(--ip-space-3);
  color: var(--ip-color-text-secondary);
  text-align: left;
  background: transparent;
  border: 0;
  border-radius: var(--ip-radius-sm);
  cursor: pointer;
}

.ip-toolrail__more-menu-item svg {
  width: 18px;
  height: 18px;
  flex: 0 0 18px;
}

.ip-toolrail__more-menu-item:hover,
.ip-toolrail__more-menu-item:focus-visible,
.ip-toolrail__more-menu-item--active {
  color: var(--ip-color-primary);
  background: var(--ip-color-primary-bg);
}

.ip-toolrail__more-menu-item:focus-visible {
  outline: 2px solid var(--ip-focus-ring-color);
  outline-offset: -1px;
}

.ip-toolrail--compact .ip-toolrail__label {
  display: none;
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
