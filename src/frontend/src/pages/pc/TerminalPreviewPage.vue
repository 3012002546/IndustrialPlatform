<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref } from 'vue'
import { useRouter } from 'vue-router'

import { PERMISSIONS } from '@/permissions'
import { ROUTE_NAMES } from '@/router/routes'
import { useAuthStore } from '@/stores/authStore'

type PreviewTerminal = 'pda' | 'mobile'

interface PreviewSize {
  id: string
  label: string
  width: number
  height: number
}

const PDA_SIZES: readonly PreviewSize[] = [
  { id: '480x800', label: '480 × 800', width: 480, height: 800 },
  { id: '600x960', label: '600 × 960', width: 600, height: 960 },
]

const MOBILE_SIZES: readonly PreviewSize[] = [
  { id: '390x844', label: '390 × 844', width: 390, height: 844 },
  { id: '430x932', label: '430 × 932', width: 430, height: 932 },
]

const router = useRouter()
const authStore = useAuthStore()
const canPreviewPda = computed(() => authStore.hasPermission(PERMISSIONS.platformPdaView))
const canPreviewMobile = computed(() => authStore.hasPermission(PERMISSIONS.platformMobileView))
const terminal = ref<PreviewTerminal>(canPreviewPda.value ? 'pda' : 'mobile')
const selectedSizeId = ref(terminal.value === 'pda' ? '480x800' : '390x844')
const frameVersion = ref(0)
const frame = ref<HTMLIFrameElement | null>(null)
const stage = ref<HTMLElement | null>(null)
const availableWidth = ref(1280)
const availableHeight = ref(640)
let resizeObserver: ResizeObserver | null = null

const sizes = computed(() => (terminal.value === 'pda' ? PDA_SIZES : MOBILE_SIZES))
const selectedSize = computed(
  () => sizes.value.find((size) => size.id === selectedSizeId.value) ?? sizes.value[0]!,
)
const frameKey = computed(() => `${terminal.value}-${selectedSize.value.id}-${frameVersion.value}`)
const previewUrl = computed(
  () =>
    router.resolve({
      name: terminal.value === 'pda' ? ROUTE_NAMES.pdaHome : ROUTE_NAMES.mobileHome,
      query: { preview: 'iframe' },
    }).href,
)
const deviceOuterSize = computed(() => ({
  width: selectedSize.value.width + 16,
  height: selectedSize.value.height + 34,
}))
const previewScale = computed(() => {
  const heightLimit = Math.max(160, Math.min(640, availableHeight.value))
  const widthLimit = Math.max(200, availableWidth.value)
  return Math.min(
    1,
    heightLimit / deviceOuterSize.value.height,
    widthLimit / deviceOuterSize.value.width,
  )
})
const deviceSlotStyle = computed(() => ({
  '--preview-slot-width': `${deviceOuterSize.value.width * previewScale.value}px`,
  '--preview-slot-height': `${deviceOuterSize.value.height * previewScale.value}px`,
}))
const deviceStyle = computed(() => ({
  '--preview-width': `${selectedSize.value.width}px`,
  '--preview-height': `${selectedSize.value.height}px`,
  '--preview-scale': String(previewScale.value),
}))

function updateAvailableSpace(): void {
  if (typeof window === 'undefined') return
  const container = stage.value
  availableWidth.value = Math.max(
    200,
    (container?.clientWidth || window.innerWidth) - 48,
  )
  availableHeight.value = Math.max(
    160,
    (container?.clientHeight || window.innerHeight - 260) - 32,
  )
}

onMounted(() => {
  updateAvailableSpace()
  window.addEventListener('resize', updateAvailableSpace)
  if (typeof ResizeObserver !== 'undefined' && stage.value !== null) {
    resizeObserver = new ResizeObserver(updateAvailableSpace)
    resizeObserver.observe(stage.value)
  }
})

onBeforeUnmount(() => {
  window.removeEventListener('resize', updateAvailableSpace)
  resizeObserver?.disconnect()
})

function selectTerminal(next: PreviewTerminal): void {
  if (next === 'pda' && !canPreviewPda.value) return
  if (next === 'mobile' && !canPreviewMobile.value) return
  terminal.value = next
  selectedSizeId.value = next === 'pda' ? PDA_SIZES[0]!.id : MOBILE_SIZES[0]!.id
}

function refreshPreview(): void {
  frameVersion.value += 1
}

function goTerminalHome(): void {
  frameVersion.value += 1
}

function returnToPc(): void {
  void router.push({ name: ROUTE_NAMES.pcHome })
}

function toggleFullscreen(): void {
  void frame.value?.requestFullscreen?.()
}
</script>

<template>
  <section class="terminal-preview-page" data-testid="terminal-preview-workspace">
    <header class="terminal-preview-page__header">
      <div>
        <p class="terminal-preview-page__eyebrow">PC 工作台 / 设备能力验证</p>
        <h1 class="terminal-preview-page__title">终端预览</h1>
        <p class="terminal-preview-page__description">
          在当前登录会话中运行真实 PDA / Mobile 页面与权限路由。
        </p>
      </div>

      <div class="terminal-preview-page__actions" aria-label="预览操作">
        <button
          type="button"
          class="terminal-preview-page__action"
          data-testid="terminal-preview-back"
          title="返回 PC 工作台"
          @click="returnToPc"
        >
          <svg width="16" height="16" viewBox="0 0 24 24" fill="none" aria-hidden="true">
            <path
              d="M19 12H5m6-6-6 6 6 6"
              stroke="currentColor"
              stroke-width="1.8"
              stroke-linecap="round"
              stroke-linejoin="round"
            />
          </svg>
          返回工作台
        </button>
        <button
          type="button"
          class="terminal-preview-page__icon-action"
          data-testid="terminal-preview-refresh"
          aria-label="刷新终端预览"
          title="刷新终端预览"
          @click="refreshPreview"
        >
          <svg width="17" height="17" viewBox="0 0 24 24" fill="none" aria-hidden="true">
            <path
              d="M20 11a8 8 0 0 0-14-4L4 9m0 0V4m0 5h5M4 13a8 8 0 0 0 14 4l2-2m0 0v5m0-5h-5"
              stroke="currentColor"
              stroke-width="1.7"
              stroke-linecap="round"
              stroke-linejoin="round"
            />
          </svg>
        </button>
        <button
          type="button"
          class="terminal-preview-page__icon-action"
          data-testid="terminal-preview-fullscreen"
          aria-label="全屏查看终端预览"
          title="全屏查看终端预览"
          @click="toggleFullscreen"
        >
          <svg width="17" height="17" viewBox="0 0 24 24" fill="none" aria-hidden="true">
            <path
              d="M8 4H4v4m12-4h4v4M8 20H4v-4m12 4h4v-4"
              stroke="currentColor"
              stroke-width="1.7"
              stroke-linecap="round"
              stroke-linejoin="round"
            />
          </svg>
        </button>
        <button
          type="button"
          class="terminal-preview-page__action"
          data-testid="terminal-preview-home"
          title="回到当前终端首页"
          @click="goTerminalHome"
        >
          终端首页
        </button>
        <a
          class="terminal-preview-page__action"
          data-testid="terminal-preview-open"
          :href="previewUrl"
          target="_blank"
          rel="noopener noreferrer"
          title="在新标签页查看真实终端页面"
        >
          独立查看
        </a>
      </div>
    </header>

    <div class="terminal-preview-page__controls">
      <div class="terminal-preview-page__control" role="group" aria-label="终端类型">
        <span class="terminal-preview-page__control-label">终端</span>
        <button
          v-if="canPreviewPda"
          type="button"
          class="terminal-preview-page__choice"
          data-testid="terminal-preview-pda"
          :class="{ 'terminal-preview-page__choice--active': terminal === 'pda' }"
          :aria-pressed="terminal === 'pda'"
          @click="selectTerminal('pda')"
        >
          PDA
        </button>
        <button
          v-if="canPreviewMobile"
          type="button"
          class="terminal-preview-page__choice"
          data-testid="terminal-preview-mobile"
          :class="{ 'terminal-preview-page__choice--active': terminal === 'mobile' }"
          :aria-pressed="terminal === 'mobile'"
          @click="selectTerminal('mobile')"
        >
          Mobile
        </button>
      </div>

      <div class="terminal-preview-page__control" role="group" aria-label="预览尺寸">
        <span class="terminal-preview-page__control-label">尺寸</span>
        <button
          v-for="size in sizes"
          :key="size.id"
          type="button"
          class="terminal-preview-page__choice"
          :class="{ 'terminal-preview-page__choice--active': selectedSize.id === size.id }"
          :data-testid="`terminal-preview-size-${size.id}`"
          :aria-pressed="selectedSize.id === size.id"
          @click="selectedSizeId = size.id"
        >
          {{ size.label }}
        </button>
        <span class="terminal-preview-page__size-readout" data-testid="terminal-preview-size">
          {{ selectedSize.label }}
        </span>
      </div>
    </div>

    <div ref="stage" class="terminal-preview-page__stage">
      <div
        class="terminal-preview-page__device-slot"
        data-testid="terminal-preview-device-slot"
        :style="deviceSlotStyle"
      >
        <div
          class="terminal-preview-page__device"
          data-testid="terminal-preview-device"
          :style="deviceStyle"
        >
          <div class="terminal-preview-page__device-top" aria-hidden="true">
            <span></span>
            <span></span>
            <span></span>
          </div>
          <iframe
            ref="frame"
            :key="frameKey"
            class="terminal-preview-page__frame"
            data-testid="terminal-preview-frame"
            :data-frame-key="frameKey"
            :style="{
              width: `${selectedSize.width}px`,
              height: `${selectedSize.height}px`,
            }"
            :src="previewUrl"
            :title="`${terminal === 'pda' ? 'PDA' : 'Mobile'}真实页面预览`"
          ></iframe>
        </div>
      </div>
    </div>
  </section>
</template>

<style scoped>
.terminal-preview-page {
  display: flex;
  flex-direction: column;
  min-height: 100%;
  padding: var(--ip-space-5);
  color: var(--ip-color-text-primary);
  background: var(--ip-color-bg-page);
}

.terminal-preview-page__header,
.terminal-preview-page__controls {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: var(--ip-space-4);
}

.terminal-preview-page__header {
  padding-bottom: var(--ip-space-4);
  border-bottom: 1px solid var(--ip-color-border);
}

.terminal-preview-page__eyebrow {
  margin: 0 0 var(--ip-space-1);
  color: var(--ip-color-text-secondary);
  font-size: var(--ip-font-size-sm);
}

.terminal-preview-page__title {
  margin: 0;
  font-size: var(--ip-font-size-xl);
}

.terminal-preview-page__description {
  margin: var(--ip-space-2) 0 0;
  color: var(--ip-color-text-secondary);
  font-size: var(--ip-font-size-sm);
}

.terminal-preview-page__actions,
.terminal-preview-page__control {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: var(--ip-space-1);
}

.terminal-preview-page__action,
.terminal-preview-page__icon-action,
.terminal-preview-page__choice {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  min-height: 36px;
  padding: 0 var(--ip-space-3);
  color: var(--ip-color-text-secondary);
  font: inherit;
  background: var(--ip-color-bg-container);
  border: 1px solid var(--ip-color-border);
  border-radius: var(--ip-radius-md);
  cursor: pointer;
  text-decoration: none;
}

.terminal-preview-page__icon-action {
  width: 36px;
  padding: 0;
  font-size: var(--ip-font-size-lg);
}

.terminal-preview-page__action:hover,
.terminal-preview-page__icon-action:hover,
.terminal-preview-page__choice:hover,
.terminal-preview-page__choice--active {
  color: var(--ip-color-primary);
  border-color: var(--ip-color-primary);
}

.terminal-preview-page__control-label {
  margin-right: var(--ip-space-1);
  color: var(--ip-color-text-secondary);
  font-size: var(--ip-font-size-sm);
}

.terminal-preview-page__controls {
  justify-content: flex-start;
  padding: var(--ip-space-3) 0;
}

.terminal-preview-page__size-readout {
  color: var(--ip-color-text-tertiary);
  font-size: var(--ip-font-size-xs);
}

.terminal-preview-page__stage {
  display: flex;
  flex: 1;
  align-items: flex-start;
  justify-content: center;
  min-height: 0;
  padding: var(--ip-space-5);
  overflow: hidden;
  background: var(--ip-color-bg-muted);
  border: 1px solid var(--ip-color-border);
  border-radius: var(--ip-radius-lg);
}

.terminal-preview-page__device-slot {
  flex: 0 0 auto;
  width: var(--preview-slot-width);
  height: var(--preview-slot-height);
}

.terminal-preview-page__device {
  box-sizing: border-box;
  display: flex;
  flex-direction: column;
  width: calc(var(--preview-width) + 16px);
  height: calc(var(--preview-height) + 34px);
  overflow: hidden;
  background: var(--ip-color-bg-container);
  border: 8px solid var(--ip-color-text-primary);
  border-radius: 24px;
  box-shadow: 0 18px 42px rgb(15 23 42 / 18%);
  transform: scale(var(--preview-scale));
  transform-origin: top left;
}

.terminal-preview-page__device-top {
  display: flex;
  flex: 0 0 18px;
  align-items: center;
  justify-content: center;
  gap: 5px;
  background: var(--ip-color-text-primary);
}

.terminal-preview-page__device-top span {
  width: 5px;
  height: 5px;
  background: var(--ip-color-text-tertiary);
  border-radius: 50%;
}

.terminal-preview-page__frame {
  display: block;
  flex: 0 0 auto;
  max-width: none;
  max-height: none;
  border: 0;
  background: var(--ip-color-bg-page);
}

.terminal-preview-page__frame:fullscreen {
  width: var(--preview-width);
  height: var(--preview-height);
}

@media (max-width: 860px) {
  .terminal-preview-page {
    padding: var(--ip-space-3);
  }

  .terminal-preview-page__header {
    align-items: flex-start;
    flex-direction: column;
  }

  .terminal-preview-page__actions {
    width: 100%;
  }

  .terminal-preview-page__controls {
    align-items: flex-start;
    flex-direction: column;
  }

  .terminal-preview-page__stage {
    padding: var(--ip-space-3);
  }
}

@media (prefers-reduced-motion: reduce) {
  .terminal-preview-page__action,
  .terminal-preview-page__icon-action,
  .terminal-preview-page__choice {
    transition: none;
  }
}
</style>
