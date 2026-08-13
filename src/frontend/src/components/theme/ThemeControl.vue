<script setup lang="ts">
/**
 * ThemeControl(PF-01 §7.6):三端共享主题入口。
 * - PC 显示配色/明暗/密度;PDA/Mobile 隐藏密度并扩大触控目标。
 * - 所有选项为原生 radio,即时生效;不发 API,不显示虚假同步状态。
 */

import { computed, onBeforeUnmount, ref, watch } from 'vue'

import { useThemeStore } from '@/stores/themeStore'
import { PC_DENSITIES, THEME_MODES, THEME_PALETTES } from '@/theme'
import type { PcDensity, ThemeMode, ThemePalette } from '@/theme'

const props = defineProps<{
  /** 当前终端:决定密度可见性与触控下限。 */
  terminal: 'pc' | 'pda' | 'mobile'
}>()

const PALETTE_LABELS: Record<ThemePalette, string> = {
  'industrial-cyan': '工业青',
  'technology-blue': '科技蓝',
  'neutral-gray': '中性灰',
}

const MODE_LABELS: Record<ThemeMode, string> = {
  light: '明亮',
  dark: '暗色',
  system: '跟随系统',
}

const DENSITY_LABELS: Record<PcDensity, string> = {
  comfortable: '舒适',
  compact: '紧凑',
}

const store = useThemeStore()
const open = ref(false)
const triggerRef = ref<HTMLButtonElement | null>(null)
const panelRef = ref<HTMLElement | null>(null)

const isPc = computed(() => props.terminal === 'pc')
const currentPalette = computed(() => store.preferences.palette)
const currentMode = computed(() => store.preferences.mode)
const currentDensity = computed(() => store.preferences.density)

function toggle(): void {
  open.value = !open.value
}

function close(): void {
  open.value = false
}

function onPanelKeydown(event: KeyboardEvent): void {
  if (event.key === 'Escape') {
    close()
    triggerRef.value?.focus()
  }
}

/** 打开时点击面板外关闭;面板内点击不关闭。 */
function onDocumentPointerDown(event: Event): void {
  const target = event.target as Node | null
  if (panelRef.value?.contains(target)) return
  if (triggerRef.value?.contains(target)) return
  close()
}

watch(open, (value) => {
  if (value) document.addEventListener('pointerdown', onDocumentPointerDown)
  else document.removeEventListener('pointerdown', onDocumentPointerDown)
})

onBeforeUnmount(() => {
  document.removeEventListener('pointerdown', onDocumentPointerDown)
})
</script>

<template>
  <div class="theme-control" :class="`theme-control--${terminal}`">
    <button
      ref="triggerRef"
      type="button"
      class="theme-control__trigger"
      data-testid="theme-control-trigger"
      aria-haspopup="true"
      :aria-expanded="open"
      aria-label="主题设置"
      @click="toggle"
    >
      <span
        class="theme-control__swatch"
        :class="`theme-control__swatch--${currentPalette}`"
        aria-hidden="true"
      />
      <span class="theme-control__trigger-text">主题</span>
    </button>

    <div
      v-if="open"
      ref="panelRef"
      class="theme-control__panel"
      role="group"
      aria-label="主题设置"
      @keydown="onPanelKeydown"
    >
      <fieldset class="theme-control__fieldset">
        <legend>配色</legend>
        <label v-for="palette in THEME_PALETTES" :key="palette" class="theme-control__option">
          <input
            type="radio"
            name="ip-theme-palette"
            :value="palette"
            :checked="currentPalette === palette"
            :data-testid="`theme-palette-${palette}`"
            @change="store.setPalette(palette)"
          />
          <span
            class="theme-control__option-swatch"
            :class="`theme-control__swatch--${palette}`"
            aria-hidden="true"
          />
          <span>{{ PALETTE_LABELS[palette] }}</span>
        </label>
      </fieldset>

      <fieldset class="theme-control__fieldset">
        <legend>明暗模式</legend>
        <label v-for="mode in THEME_MODES" :key="mode" class="theme-control__option">
          <input
            type="radio"
            name="ip-theme-mode"
            :value="mode"
            :checked="currentMode === mode"
            :data-testid="`theme-mode-${mode}`"
            @change="store.setMode(mode)"
          />
          <span>{{ MODE_LABELS[mode] }}</span>
        </label>
      </fieldset>

      <fieldset v-if="isPc" class="theme-control__fieldset">
        <legend>密度</legend>
        <label v-for="density in PC_DENSITIES" :key="density" class="theme-control__option">
          <input
            type="radio"
            name="ip-theme-density"
            :value="density"
            :checked="currentDensity === density"
            :data-testid="`theme-density-${density}`"
            @change="store.setDensity(density)"
          />
          <span>{{ DENSITY_LABELS[density] }}</span>
        </label>
      </fieldset>
    </div>
  </div>
</template>

<style scoped>
.theme-control {
  position: relative;
  display: inline-flex;
}

.theme-control__trigger {
  display: inline-flex;
  align-items: center;
  gap: var(--ip-space-2);
  min-width: 32px;
  min-height: 32px;
  padding: 0 var(--ip-space-2);
  color: inherit;
  background: transparent;
  border: 0;
  border-radius: var(--ip-radius-md);
  cursor: pointer;
  font-size: var(--ip-font-size-md);
}

.theme-control--pda .theme-control__trigger {
  min-width: var(--ip-touch-min-size);
  min-height: var(--ip-touch-min-size);
}

.theme-control--mobile .theme-control__trigger {
  min-width: var(--ip-touch-min-size-mobile);
  min-height: var(--ip-touch-min-size-mobile);
}

.theme-control__swatch {
  display: inline-block;
  width: 14px;
  height: 14px;
  border-radius: var(--ip-radius-sm);
  border: 1px solid rgb(255 255 255 / 0.4);
}

.theme-control__swatch--industrial-cyan {
  background: #0077a1;
}

.theme-control__swatch--technology-blue {
  background: #2563eb;
}

.theme-control__swatch--neutral-gray {
  background: #4b5563;
}

.theme-control__panel {
  position: absolute;
  top: calc(100% + var(--ip-space-2));
  right: 0;
  z-index: var(--ip-z-dropdown);
  min-width: 240px;
  padding: var(--ip-space-3) var(--ip-space-4);
  background: var(--ip-color-bg-container);
  border: 1px solid var(--ip-color-border);
  border-radius: var(--ip-radius-lg);
  box-shadow: var(--ip-shadow-lg);
  color: var(--ip-color-text-primary);
}

.theme-control__fieldset {
  margin: 0 0 var(--ip-space-3);
  padding: 0;
  border: 0;
}

.theme-control__fieldset:last-child {
  margin-bottom: 0;
}

.theme-control__fieldset legend {
  padding: 0;
  margin-bottom: var(--ip-space-2);
  font-size: var(--ip-font-size-sm);
  color: var(--ip-color-text-secondary);
}

.theme-control__option {
  display: flex;
  align-items: center;
  gap: var(--ip-space-2);
  min-height: var(--ip-density-control-height);
  font-size: var(--ip-font-size-md);
}

.theme-control__option-swatch {
  width: 12px;
  height: 12px;
  margin-left: var(--ip-space-3);
  border-radius: var(--ip-radius-sm);
}

@media (prefers-reduced-motion: reduce) {
  .theme-control__panel {
    transition: none;
  }
}
</style>
