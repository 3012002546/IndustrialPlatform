<script setup lang="ts">
import { onBeforeUnmount, ref, watch } from 'vue'

const props = defineProps<{ continuous: boolean; pageSeconds?: number }>()
const viewport = ref<HTMLElement>()
let frame = 0
let previous = 0
let position = 0
let resumeAt = 0
const repeating = ref(false)
const hovered = ref(false)
const focused = ref(false)

function tick(time: number) {
  const element = viewport.value
  if (!element) return
  const elapsed = previous ? Math.min(100, time - previous) : 0
  previous = time
  const header = element.querySelector('thead')?.offsetHeight ?? 0
  const cycle = element.querySelector('tbody')?.offsetHeight ?? 0
  repeating.value = cycle + header > element.clientHeight + 1
  if (hovered.value || focused.value || time < resumeAt || !repeating.value) {
    position = element.scrollTop
  } else {
    // Preserve subpixel progress, including on browsers that round scrollTop.
    if (Math.abs(element.scrollTop - position) > 1) position = element.scrollTop
    const seconds = Number(props.pageSeconds)
    const duration = Number.isFinite(seconds) ? Math.max(3, seconds) : 8
    position =
      (position + (Math.max(1, element.clientHeight - header) * elapsed) / (duration * 1000)) %
      cycle
    element.scrollTop = position
  }
  frame = requestAnimationFrame(tick)
}

function stop() {
  cancelAnimationFrame(frame)
  frame = 0
  previous = 0
  repeating.value = false
}
function pauseManualScroll() {
  resumeAt = performance.now() + 1500
}
watch(
  () => props.continuous,
  (enabled) => {
    stop()
    if (enabled) frame = requestAnimationFrame(tick)
  },
  { immediate: true, flush: 'post' },
)
onBeforeUnmount(stop)
</script>

<template>
  <div
    ref="viewport"
    class="dashboard-table-wrap"
    @mouseenter="hovered = true"
    @mouseleave="hovered = false"
    @focusin="focused = true"
    @focusout="focused = false"
    @wheel.passive="pauseManualScroll"
    @touchmove.passive="pauseManualScroll"
  >
    <slot :repeat="repeating" />
  </div>
</template>
