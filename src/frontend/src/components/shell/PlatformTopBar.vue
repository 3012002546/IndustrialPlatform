<script setup lang="ts">
/**
 * PlatformTopBar(PF-01 §7.8/§6.1):PC 品牌顶栏,高度固定 56px,全宽覆盖工具轨、
 * 功能树与内容区。提供 brand / context / global-search / global-actions / user 具名槽,
 * 空槽不渲染占位按钮。顶栏文字与图标使用 --ip-shell-topbar-text(白),渐变由
 * --ip-shell-topbar-background 提供(三配色各自映射,§7.5)。
 */

import { nextTick, onBeforeUnmount, onMounted, ref } from 'vue'

defineSlots<{
  brand?: () => unknown
  context?: () => unknown
  'global-search'?: () => unknown
  'global-actions'?: () => unknown
  user?: () => unknown
}>()

const headerRef = ref<HTMLElement | null>(null)
const leftRef = ref<HTMLElement | null>(null)
const searchRef = ref<HTMLElement | null>(null)
const rightRef = ref<HTMLElement | null>(null)
const searchStyle = ref<Record<string, string>>({})
let resizeObserver: ResizeObserver | undefined

function preferredSearchWidth(viewportWidth: number): number {
  const minimum = viewportWidth >= 1600 ? 320 : 220
  const maximum = viewportWidth >= 1600 ? 480 : 320
  return Math.min(maximum, Math.max(minimum, viewportWidth * 0.22))
}

/** Keep the search visually centered while reserving the measured left/right content. */
function updateSearchLayout(): void {
  const header = headerRef.value
  const left = leftRef.value
  const search = searchRef.value
  const right = rightRef.value
  if (header === null || left === null || search === null || right === null) return

  const headerRect = header.getBoundingClientRect()
  const leftRect = left.getBoundingClientRect()
  const rightRect = right.getBoundingClientRect()
  const gutter = 8
  const availableStart = Math.max(headerRect.left + gutter, leftRect.right + gutter)
  const availableEnd = Math.min(headerRect.right - gutter, rightRect.left - gutter)
  const availableWidth = Math.max(0, availableEnd - availableStart)
  const center = headerRect.left + headerRect.width / 2
  const centeredWidth = Math.max(
    0,
    2 * Math.min(center - availableStart, availableEnd - center),
  )
  const width = Math.min(preferredSearchWidth(headerRect.width), availableWidth, centeredWidth)
  const leftPosition = Math.max(
    availableStart,
    Math.min(center - width / 2, availableEnd - width),
  )

  searchStyle.value = {
    left: `${Math.max(0, leftPosition - headerRect.left)}px`,
    width: `${Math.max(0, width)}px`,
    transform: 'none',
  }
}

function scheduleSearchLayout(): void {
  void nextTick(updateSearchLayout)
}

onMounted(() => {
  scheduleSearchLayout()
  window.addEventListener('resize', scheduleSearchLayout)
  if (typeof ResizeObserver !== 'undefined') {
    resizeObserver = new ResizeObserver(scheduleSearchLayout)
    for (const element of [headerRef.value, leftRef.value, rightRef.value]) {
      if (element !== null) resizeObserver.observe(element)
    }
  }
})

onBeforeUnmount(() => {
  resizeObserver?.disconnect()
  window.removeEventListener('resize', scheduleSearchLayout)
})
</script>

<template>
  <header ref="headerRef" class="ip-topbar">
    <div ref="leftRef" class="ip-topbar__left">
      <div class="ip-topbar__brand">
        <slot name="brand" />
      </div>

      <div v-if="$slots.context" class="ip-topbar__context">
        <slot name="context" />
      </div>
    </div>

    <div v-if="$slots['global-search']" ref="searchRef" class="ip-topbar__search" :style="searchStyle">
      <slot name="global-search" />
    </div>

    <div ref="rightRef" class="ip-topbar__right">
      <div class="ip-topbar__actions">
        <slot name="global-actions" />
      </div>

      <div class="ip-topbar__user">
        <slot name="user" />
      </div>
    </div>
  </header>
</template>

<style scoped>
.ip-topbar {
  position: relative;
  display: grid;
  flex: 0 0 auto;
  align-items: center;
  grid-template-columns: minmax(0, 1fr) max-content;
  gap: 12px;
  height: var(--ip-shell-topbar-height);
  padding: 0 4px;
  overflow: visible;
  background: var(--ip-shell-topbar-background);
  color: var(--ip-shell-topbar-text);
}

.ip-topbar__left {
  display: flex;
  width: max-content;
  min-width: 0;
  max-width: 100%;
  align-items: center;
  gap: 12px;
  overflow: hidden;
  padding-left: 10px;
}

.ip-topbar__brand {
  display: flex;
  flex: 0 1 auto;
  align-items: center;
  gap: 6px;
  min-width: 0;
  max-width: 36vw;
}

.ip-topbar__search {
  position: absolute;
  top: 50%;
  display: flex;
  align-items: center;
  box-sizing: border-box;
  min-width: 0;
  width: clamp(220px, 22vw, 320px);
  max-width: calc(100% - 16px);
  justify-content: center;
  transform: translate(-50%, -50%);
}

.ip-topbar__context {
  display: flex;
  flex: 1 1 auto;
  min-width: 0;
  max-width: 22vw;
  overflow: hidden;
  white-space: nowrap;
}

.ip-topbar__right {
  display: inline-flex;
  flex: 0 0 auto;
  align-items: center;
  gap: 4px;
  min-width: max-content;
  width: max-content;
  overflow: visible;
  justify-self: end;
}

.ip-topbar__actions {
  display: inline-flex;
  flex: 0 0 auto;
  align-items: center;
  gap: 4px;
  min-width: max-content;
  margin-left: auto;
  overflow: visible;
}

.ip-topbar__user {
  display: inline-flex;
  flex: 0 0 190px;
  align-items: center;
  width: 190px;
  max-width: 190px;
  min-width: 146px;
  overflow: hidden;
}

.ip-topbar__user > * {
  display: block;
  min-width: 0;
  width: 100%;
  max-width: 100%;
}

@media (max-width: 1440px) {
  .ip-topbar {
    grid-template-columns: minmax(0, 1fr) max-content;
    gap: 8px;
    padding: 0 4px;
  }

  .ip-topbar__right {
    gap: 4px;
  }

  .ip-topbar__actions {
    gap: 3px;
  }

  .ip-topbar__user {
    flex-basis: 168px;
    width: 168px;
    max-width: 168px;
  }

  /* Mock 仅是开发提示,保留 status 文本在无障碍树中,视觉上收为状态点。 */
  .ip-topbar__actions :deep(.mock-mode-banner) {
    box-sizing: border-box;
    width: 24px;
    min-width: 24px;
    max-width: 24px;
    padding: var(--ip-space-1);
    overflow: hidden;
  }

  .ip-topbar__actions :deep(.mock-mode-banner__dot) {
    flex: 0 0 auto;
  }

  .ip-topbar__actions :deep(.mock-mode-banner > span:last-child) {
    overflow: hidden;
    white-space: nowrap;
  }
}

@media (max-width: 1100px) {
  .ip-topbar__actions {
    gap: 1px;
  }

  .ip-topbar__user {
    flex-basis: 140px;
    width: 140px;
    max-width: 140px;
    min-width: 140px;
  }
}

@media (min-width: 1600px) {
  .ip-topbar {
    grid-template-columns: minmax(0, 1fr) max-content;
  }
}
</style>
