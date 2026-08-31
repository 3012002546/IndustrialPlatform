<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, onMounted, ref } from 'vue'
import { Check, Connection } from '@element-plus/icons-vue'

import { localeMessages } from '@/localization/i18n'
import { usePlatformLocale } from '@/localization/localeContext'
import { useLocalizationStore } from '@/stores/localizationStore'
import type { SupportedLocale } from '@/localization/types'

const localization = useLocalizationStore()
const locale = usePlatformLocale()
const copy = computed(() => localeMessages[locale.value].common.locale)
const open = ref(false)
const trigger = ref<HTMLButtonElement | null>(null)
const menu = ref<HTMLElement | null>(null)
const menuStyle = ref<Record<string, string>>({})
const LOCALE_MENU_WIDTH = 144
const LOCALE_MENU_GUTTER = 8

function positionMenu(): void {
  const rect = trigger.value?.getBoundingClientRect()
  if (rect === undefined) return
  menuStyle.value = {
    top: `${Math.min(window.innerHeight - 12, rect.bottom + 6)}px`,
    left: `${Math.max(LOCALE_MENU_GUTTER, Math.min(rect.right - LOCALE_MENU_WIDTH, window.innerWidth - LOCALE_MENU_WIDTH - LOCALE_MENU_GUTTER))}px`,
  }
}

function toggle(): void {
  open.value = !open.value
  if (open.value) void nextTick(() => {
    positionMenu()
    menu.value?.querySelector<HTMLButtonElement>('[role="option"]')?.focus()
  })
}

function choose(next: SupportedLocale): void {
  localization.setLocale(next)
  open.value = false
  void nextTick(() => trigger.value?.focus())
}

function onPointerDown(event: PointerEvent): void {
  if (trigger.value?.contains(event.target as Node) || menu.value?.contains(event.target as Node)) return
  open.value = false
}

function onKeyDown(event: KeyboardEvent): void {
  if (event.key === 'Escape' && open.value) {
    event.preventDefault()
    open.value = false
    trigger.value?.focus()
  }
}

onMounted(() => {
  document.addEventListener('pointerdown', onPointerDown)
  document.addEventListener('keydown', onKeyDown)
  window.addEventListener('resize', positionMenu)
  window.addEventListener('scroll', positionMenu, true)
})
onBeforeUnmount(() => {
  document.removeEventListener('pointerdown', onPointerDown)
  document.removeEventListener('keydown', onKeyDown)
  window.removeEventListener('resize', positionMenu)
  window.removeEventListener('scroll', positionMenu, true)
})
</script>

<template>
  <button
    ref="trigger"
    type="button"
    class="ip-locale-control"
    :aria-label="copy.label"
    :aria-expanded="open"
    aria-haspopup="listbox"
    :title="copy.label"
    @click="toggle"
  >
    <Connection aria-hidden="true" />
    <span class="ip-sr-only">{{ copy.label }}</span>
  </button>
  <Teleport to="body">
    <div v-if="open" ref="menu" class="ip-locale-control__menu" role="listbox" :style="menuStyle" :aria-label="copy.label">
      <button type="button" role="option" :aria-selected="localization.locale === 'zh-CN'" @click="choose('zh-CN')">
        <span>{{ copy.zhCN }}</span><Check v-if="localization.locale === 'zh-CN'" aria-hidden="true" />
      </button>
      <button type="button" role="option" :aria-selected="localization.locale === 'en-US'" @click="choose('en-US')">
        <span>{{ copy.enUS }}</span><Check v-if="localization.locale === 'en-US'" aria-hidden="true" />
      </button>
    </div>
  </Teleport>
</template>

<style scoped>
.ip-locale-control {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  flex: 0 0 32px;
  width: 32px;
  height: 32px;
  padding: 0;
  color: inherit;
  background: transparent;
  border: 0;
  border-radius: var(--ip-radius-md);
  cursor: pointer;
}
.ip-locale-control:hover,
.ip-locale-control:focus-visible { background: rgb(255 255 255 / 0.12); }
.ip-locale-control :deep(svg) { width: 18px; height: 18px; }
.ip-locale-control__menu {
  position: fixed;
  z-index: 2200;
  box-sizing: border-box;
  width: 144px;
  padding: var(--ip-space-1);
  color: var(--ip-color-text-primary);
  background: var(--ip-color-bg-container);
  border: 1px solid var(--ip-color-border);
  border-radius: var(--ip-radius-md);
  box-shadow: var(--ip-shadow-md);
}
.ip-locale-control__menu button {
  display: flex;
  align-items: center;
  justify-content: space-between;
  width: 100%;
  min-height: 36px;
  padding: 0 var(--ip-space-3);
  color: inherit;
  background: transparent;
  border: 0;
  border-radius: var(--ip-radius-sm);
  cursor: pointer;
  text-align: left;
}
.ip-locale-control__menu button:hover,
.ip-locale-control__menu button:focus-visible,
.ip-locale-control__menu button[aria-selected='true'] { background: var(--ip-color-primary-bg); }
.ip-locale-control__menu button :deep(svg) { width: 16px; height: 16px; flex: 0 0 16px; }
.ip-sr-only {
  position: absolute;
  width: 1px;
  height: 1px;
  padding: 0;
  overflow: hidden;
  clip: rect(0 0 0 0);
  white-space: nowrap;
  border: 0;
}
</style>
