<script setup lang="ts">
import { computed, ref, onMounted, onBeforeUnmount, watch, nextTick } from 'vue'
import type { Json } from '@/api/studio'
import { isolatedPage } from './previewPhysicalPage'

const props = defineProps<{
  pages: Json[]
  page: number
  content: string
  searchLocation: string
  continuous: boolean
  zoom: string
  title: string
  read: (item: Json) => Promise<string>
}>()
const emit = defineEmits<{ scrollPage: [number]; scale: [number] }>()
const viewport = ref<HTMLElement>()
const width = ref(1000),
  height = ref(700),
  top = ref(0)
let observer: ResizeObserver | undefined
onMounted(() => {
  const el = viewport.value!
  const resize = () => {
    width.value = el.clientWidth || width.value
    height.value = el.clientHeight || height.value
  }
  resize()
  if (typeof ResizeObserver !== 'undefined') {
    observer = new ResizeObserver(resize)
    observer.observe(el)
  }
})
onBeforeUnmount(() => observer?.disconnect())
const fetched = ref<Record<number, string>>({})
const failures = ref<Record<number, string>>({})
const sizes = computed(() =>
  props.pages.map((item) => {
    const html = String(
      item.html ?? fetched.value[item.pageNo] ?? (item.pageNo === props.page ? props.content : ''),
    )
    // Use only the renderer's frozen paper element, never a root variable from another part.
    const start = html.indexOf('class="pagedjs_page ')
    const tag = start < 0 ? '' : html.slice(start, html.indexOf('>', start))
    const mm = (key: string, fallback: number) =>
      ((Number(new RegExp('--pagedjs-' + key + ':\\s*([\\d.]+)mm').exec(tag)?.[1]) || fallback) *
        96) /
      25.4
    return { no: Number(item.pageNo), w: mm('width', 210), h: mm('height', 297) }
  }),
)
const layout = computed(() => {
  let y = 12
  return sizes.value
    .filter((p) => props.continuous || p.no === props.page)
    .map((p) => {
      const fit = Math.max(0.01, (width.value - 32) / p.w)
      const scale =
        props.zoom === 'width'
          ? fit
          : props.zoom === 'page'
            ? Math.min(fit, Math.max(0.01, (height.value - 24) / p.h))
            : Number(props.zoom)
      // A fragment may scroll its own sandbox, but cannot scroll a cross-origin parent.
      // Give the active search document a viewport at the selected zoom.
      const searching = props.searchLocation && p.no === props.page
      const sheet = {
        ...p,
        y,
        scale,
        width: searching ? Math.min(p.w * scale, Math.max(1, width.value - 32)) : p.w * scale,
        height: searching ? Math.min(p.h * scale, Math.max(1, height.value - 24)) : p.h * scale,
      }
      y += sheet.height + 16
      return sheet
    })
})
const visible = computed(() =>
  layout.value.filter(
    (p) =>
      !props.continuous ||
      (p.y + p.height >= top.value - 24 && p.y <= top.value + height.value + 24),
  ),
)
const extent = computed(() => layout.value.at(-1))
const maxWidth = computed(() => Math.max(width.value, ...layout.value.map((s) => s.width + 32)))
// Only compatibility previews use file URLs. Keep at most the neighbouring visible pages.
watch(
  [() => visible.value.map((p) => p.no).join(','), () => props.pages, () => props.page],
  async (_, __, onCleanup) => {
    let active = true
    onCleanup(() => {
      active = false
    })
    const keep: Record<number, string> = {}
    for (const p of visible.value) if (fetched.value[p.no]) keep[p.no] = fetched.value[p.no]!
    fetched.value = keep
    failures.value = {}
    for (const p of visible.value) {
      const item = props.pages.find((i) => i.pageNo === p.no)
      if (!item || p.no === props.page || typeof item.html === 'string' || keep[p.no]) continue
      try {
        const html = await props.read(item)
        if (!active) return
        fetched.value[p.no] = html
      } catch (e) {
        if (active) failures.value[p.no] = String(e)
      }
    }
  },
  { immediate: true },
)
let positionedTop = -1
let scrolledPage = 0
function scroll() {
  const el = viewport.value
  if (!el) return
  top.value = el.scrollTop
  if (!props.continuous || Math.abs(top.value - positionedTop) < 0.5) return
  positionedTop = -1
  const sheet =
    visible.value.find((p) => p.y + p.height > el.scrollTop + Math.min(height.value / 2, 100)) ??
    visible.value.at(-1)
  if (sheet && sheet.no !== props.page) {
    scrolledPage = sheet.no
    emit('scrollPage', sheet.no)
  }
}
function go(no: number) {
  const el = viewport.value
  if (el) {
    el.scrollTop = Math.max(0, (layout.value.find((p) => p.no === no)?.y ?? 12) - 12)
    positionedTop = el.scrollTop
    top.value = el.scrollTop
  }
}
watch(
  () => [props.page, props.continuous, props.zoom] as const,
  async ([no], old) => {
    const fromScroll = no === scrolledPage && old[1] === props.continuous && old[2] === props.zoom
    scrolledPage = 0
    if (!fromScroll) {
      await nextTick()
      go(no)
    }
  },
)
watch(
  () => layout.value.find((s) => s.no === props.page)?.scale,
  (scale) => {
    if (scale) emit('scale', scale)
  },
  { immediate: true },
)
watch(
  () => props.searchLocation,
  async (location) => {
    if (!location) return
    await nextTick()
    go(props.page)
    if (viewport.value) viewport.value.scrollLeft = (maxWidth.value - width.value) / 2
  },
)
function source(no: number) {
  const html =
    no === props.page
      ? props.content
      : (props.pages.find((p) => p.pageNo === no)?.html ?? fetched.value[no])
  return html ? isolatedPage(html, no, props.pages.length) : undefined
}
</script>
<template>
  <div ref="viewport" class="paper-scroll" @scroll.passive="scroll">
    <div
      class="paper-stack"
      :style="{
        height: (extent ? extent.y + extent.height + 12 : 0) + 'px',
        width: maxWidth + 'px',
      }"
    >
      <div
        v-for="sheet in visible"
        :key="sheet.no"
        class="physical-sheet"
        :data-physical-page="sheet.no"
        :style="{
          top: sheet.y + 'px',
          left: (maxWidth - sheet.width) / 2 + 'px',
          width: sheet.width + 'px',
          height: sheet.height + 'px',
        }"
      >
        <iframe
          v-if="source(sheet.no)"
          :title="title + ' ' + sheet.no"
          sandbox=""
          referrerpolicy="no-referrer"
          :scrolling="sheet.no === page && searchLocation ? 'auto' : 'no'"
          :style="{
            width: sheet.width / sheet.scale + 'px',
            height: sheet.height / sheet.scale + 'px',
            transform: 'scale(' + sheet.scale + ')',
          }"
          :srcdoc="sheet.no === page && searchLocation ? undefined : source(sheet.no)"
          :src="sheet.no === page && searchLocation ? searchLocation : undefined"
        />
        <span v-else-if="failures[sheet.no]" role="alert">{{ failures[sheet.no] }}</span>
      </div>
    </div>
  </div>
</template>
<style scoped>
.paper-scroll {
  flex: 1;
  min-height: 0;
  overflow: auto;
  background: #f5f5f5;
  overflow-anchor: none;
}
.paper-stack {
  position: relative;
}
.physical-sheet {
  position: absolute;
  overflow: hidden;
  background: white;
  box-shadow:
    0 0 0 1px #b9b9b9,
    0 2px 7px #0001;
}
iframe {
  display: block;
  border: 0;
  transform-origin: top left;
  background: white;
}
</style>
