<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { useStudioCopy } from './copy'
import {
  logicalDelta,
  type LayoutNode,
  nodes,
  createHistory,
  clone,
  syncGridPlacement,
} from './model'
import type { Json } from '@/api/studio'

const props = defineProps<{
  modelValue: Json
  canvasWidth?: number
  canvasHeight?: number
  propertiesOpen?: boolean
  leftTitle?: string
  sidebarWidths?: [number, number]
  centerCanvas?: boolean
  interactionActive?: boolean
}>()
const emit = defineEmits<{ 'update:modelValue': [value: Json]; select: [id: string] }>()
const t = useStudioCopy(),
  selected = ref(''),
  leftOpen = ref(true),
  rightOpen = ref(false),
  zoom = ref(1),
  width = ref(
    Number(
      props.canvasWidth ?? (props.modelValue.layout?.grid as Json | undefined)?.canvasWidth ?? 1200,
    ),
  ),
  height = ref(
    Number(
      props.canvasHeight ??
        (props.modelValue.layout?.grid as Json | undefined)?.canvasHeight ??
        800,
    ),
  )
watch(
  () => [props.canvasWidth, props.canvasHeight],
  ([w, h]) => {
    if (w) width.value = w
    if (h) height.value = h
    if (!props.interactionActive) void nextTick(fit)
  },
)
const leftWidth = ref(props.sidebarWidths?.[0] ?? 240),
  rightWidth = ref(props.sidebarWidths?.[1] ?? 320),
  viewport = ref<HTMLElement | null>(null)
const revision = ref(0),
  history = createHistory({ document: props.modelValue, selected: '' })
const root = computed(() => props.modelValue.layout as unknown as LayoutNode)
const flat = computed(() => (root.value ? nodes(root.value) : []))
const focused = computed(() => flat.value.find((n) => n.id === selected.value))
const canUndo = computed(() => {
    void revision.value
    return history.canUndo
  }),
  canRedo = computed(() => {
    void revision.value
    return history.canRedo
  })
const panMode = ref(false)
const fitMode = ref(true)
function changeZoom(delta: number) {
  fitMode.value = false
  zoom.value = Math.max(0.001, Math.min(3, zoom.value + delta))
}
function setZoom(value: number) {
  if (!Number.isFinite(value)) return
  fitMode.value = false
  zoom.value = Math.max(0.1, Math.min(3, value))
}
const narrow = () => window.innerWidth <= 1024
function sizeChanged() {
  if (!Number.isFinite(width.value) || !Number.isFinite(height.value)) return
  width.value = Math.max(320, Math.min(16384, width.value))
  height.value = Math.max(320, Math.min(16384, height.value))
  apply((doc) => {
    const grid = (doc.layout as LayoutNode).grid as unknown as Json
    grid.canvasWidth = width.value
    grid.canvasHeight = height.value
  })
  fit()
}
function toggleLeft() {
  leftOpen.value = !leftOpen.value
  if (leftOpen.value && narrow()) rightOpen.value = false
}
function toggleRight() {
  rightOpen.value = !rightOpen.value
  if (rightOpen.value && narrow()) leftOpen.value = false
}
function showProperties() {
  rightOpen.value = true
  if (narrow()) leftOpen.value = false
}
let observer: ResizeObserver | undefined
let ownChange = false
function update(value: Json, commit = true) {
  ownChange = true
  emit('update:modelValue', value)
  if (commit) {
    history.commit({ document: value, selected: selected.value })
    revision.value++
  }
}
function apply(fn: (document: Json) => void) {
  const document = clone(props.modelValue)
  fn(document)
  update(document)
}
function select(id: string) {
  selected.value = id
  emit('select', id)
}
function undo() {
  const value = history.undo()
  selected.value = value.selected
  ownChange = true
  emit('update:modelValue', value.document)
  emit('select', selected.value)
  revision.value++
}
function redo() {
  const value = history.redo()
  selected.value = value.selected
  ownChange = true
  emit('update:modelValue', value.document)
  emit('select', selected.value)
  revision.value++
}
function fit() {
  const view = viewport.value
  if (!view) return
  fitMode.value = true
  // A newly mounted/hidden panel has no usable layout yet; keep the last valid zoom.
  if (view.clientWidth <= 32 || view.clientHeight <= 32) return
  zoom.value = Math.max(
    0.001,
    Math.min((view.clientWidth - 32) / width.value, (view.clientHeight - 32) / height.value, 1),
  )
  view.scrollTo(0, 0)
}
function keydown(event: KeyboardEvent) {
  const target = event.target as HTMLElement
  if (
    event.isComposing ||
    target.closest('input,textarea,[contenteditable="true"],.el-input-number') ||
    !(event.ctrlKey || event.metaKey)
  )
    return
  if (event.key.toLowerCase() === 'z') {
    event.preventDefault()
    if (event.shiftKey) redo()
    else undo()
  }
  if (event.key.toLowerCase() === 'y') {
    event.preventDefault()
    redo()
  }
}
function geometry(node: LayoutNode) {
  const grid = node.grid as unknown as Json
  const index = flat.value.filter((x) => x !== root.value).indexOf(node)
  return {
    x: Number(grid.x ?? 24 + (index % 2) * 430),
    y: Number(grid.y ?? 24 + Math.floor(index / 2) * 150),
    width: Number(grid.width ?? 400),
    height: Number(grid.height ?? 120),
  }
}
function pointer(event: PointerEvent, node?: LayoutNode, resizing = false) {
  if (event.button !== 0) return
  const element = event.currentTarget as HTMLElement
  element.setPointerCapture(event.pointerId)
  const startX = event.clientX,
    startY = event.clientY,
    view = viewport.value,
    scrollX = view?.scrollLeft ?? 0,
    scrollY = view?.scrollTop ?? 0
  const draft = clone(props.modelValue),
    item = node ? nodes(draft.layout as unknown as LayoutNode).find((n) => n.id === node.id) : null
  const start = node ? geometry(node) : null
  if (node) select(node.id)
  const move = (e: PointerEvent) => {
    if (!item || !start || panMode.value) {
      if (view) {
        view.scrollLeft = scrollX - (e.clientX - startX)
        view.scrollTop = scrollY - (e.clientY - startY)
      }
      return
    }
    const grid = item.grid as unknown as Json,
      dx = logicalDelta(e.clientX - startX, zoom.value),
      dy = logicalDelta(e.clientY - startY, zoom.value)
    if (resizing) {
      grid.width = Math.max(60, Math.round(start.width + dx))
      grid.height = Math.max(40, Math.round(start.height + dy))
    } else {
      grid.x = Math.max(0, Math.round(start.x + dx))
      grid.y = Math.max(0, Math.round(start.y + dy))
    }
    update(clone(draft), false)
  }
  const end = () => {
    element.removeEventListener('pointermove', move)
    element.removeEventListener('pointerup', end)
    element.removeEventListener('pointercancel', end)
    if (element.hasPointerCapture(event.pointerId)) element.releasePointerCapture(event.pointerId)
    if (item) {
      syncGridPlacement(draft.layout as LayoutNode, item.id, width.value)
      update(clone(draft), false)
      history.commit({ document: draft, selected: selected.value })
      revision.value++
    }
  }
  element.addEventListener('pointermove', move)
  element.addEventListener('pointerup', end)
  element.addEventListener('pointercancel', end)
}
function resizeSidebar(event: PointerEvent, side: 'left' | 'right') {
  const target = event.currentTarget as HTMLElement
  target.setPointerCapture(event.pointerId)
  const start = event.clientX,
    original = side === 'left' ? leftWidth.value : rightWidth.value
  const move = (e: PointerEvent) => {
    if (side === 'left')
      leftWidth.value = Math.max(180, Math.min(420, original + e.clientX - start))
    else rightWidth.value = Math.max(240, Math.min(480, original + start - e.clientX))
  }
  const end = () => {
    target.removeEventListener('pointermove', move)
    target.removeEventListener('pointerup', end)
    target.removeEventListener('pointercancel', end)
  }
  target.addEventListener('pointermove', move)
  target.addEventListener('pointerup', end)
  target.addEventListener('pointercancel', end)
}
watch(
  () => props.modelValue,
  (value) => {
    if (ownChange) {
      ownChange = false
      return
    }
    history.commit({ document: value, selected: selected.value })
    revision.value++
  },
  { deep: true },
)
watch([leftOpen, rightOpen], () => void nextTick(fit))
watch(
  () => [root.value?.grid],
  () => {
    const grid = root.value?.grid as unknown as Json
    if (grid?.canvasWidth) width.value = Number(grid.canvasWidth)
    if (grid?.canvasHeight) height.value = Number(grid.canvasHeight)
  },
  { deep: true },
)
onMounted(() => {
  leftOpen.value = true
  rightOpen.value = window.innerWidth > (props.propertiesOpen ? 1024 : 1280)
  observer = new ResizeObserver(() => {
    if (fitMode.value && !props.interactionActive) fit()
  })
  if (viewport.value) observer.observe(viewport.value)
  fit()
})
onBeforeUnmount(() => observer?.disconnect())
watch(
  () => props.interactionActive,
  (active) => {
    if (!active && fitMode.value) void nextTick(fit)
  },
)
defineExpose({
  select,
  apply,
  update,
  fit,
  showProperties,
  selected,
  focused,
  undo,
  redo,
  canUndo,
  canRedo,
})
</script>
<template>
  <div tabindex="0" :aria-label="t('design')" @keydown="keydown">
    <slot
      name="toolbar"
      :zoom="zoom"
      :set-zoom="setZoom"
      :fit="fit"
      :left-open="leftOpen"
      :right-open="rightOpen"
      :toggle-left="toggleLeft"
      :toggle-right="toggleRight"
    >
      <div class="studio-tools">
        <el-button :disabled="!canUndo" @click="undo">{{ t('undo') }}</el-button
        ><el-button :disabled="!canRedo" @click="redo">{{ t('redo') }}</el-button>
        <el-button @click="toggleLeft">{{ leftTitle ?? t('left') }}</el-button
        ><el-button @click="toggleRight">{{ t('right') }}</el-button>
        <el-checkbox v-model="panMode">{{ t('pan') }}</el-checkbox
        ><el-button @click="changeZoom(-0.1)">−</el-button><span>{{ Math.round(zoom * 100) }}%</span
        ><el-button @click="changeZoom(0.1)">+</el-button
        ><el-button @click="fit">{{ t('fit') }}</el-button>
        <label v-if="canvasWidth === undefined"
          >{{ t('width') }}
          <input
            v-model.number="width"
            type="number"
            min="320"
            max="16384"
            style="width: 70px"
            @change="sizeChanged" /></label
        ><label v-if="canvasHeight === undefined"
          >{{ t('height') }}
          <input
            v-model.number="height"
            type="number"
            min="320"
            max="16384"
            style="width: 70px"
            @change="sizeChanged"
        /></label>
      </div>
    </slot>
    <div
      class="studio-editor"
      :class="{ 'left-hidden': !leftOpen, 'right-hidden': !rightOpen }"
      :style="{ '--studio-left': leftWidth + 'px', '--studio-right': rightWidth + 'px' }"
    >
      <aside class="studio-sidebar" style="grid-column: 1; grid-row: 1">
        <slot name="structure" :select="select" :selected="selected" :nodes="flat" :apply="apply" />
      </aside>
      <div
        v-if="leftOpen"
        class="studio-separator left"
        @pointerdown="resizeSidebar($event, 'left')"
      />
      <main
        ref="viewport"
        class="studio-viewport"
        :style="{ overflow: fitMode ? 'hidden' : 'auto', gridColumn: 2, gridRow: 1 }"
        @pointerdown.self="pointer($event)"
      >
        <div
          :style="{
            width: width * zoom + 'px',
            height: height * zoom + 'px',
            padding: '16px',
            boxSizing: 'content-box',
            marginInline: centerCanvas ? 'auto' : undefined,
          }"
        >
          <div
            class="studio-canvas"
            :style="{ width: width + 'px', height: height + 'px', transform: `scale(${zoom})` }"
            @pointerdown.self="pointer($event)"
          >
            <slot name="canvas" :zoom="zoom" :select="select" :selected="selected" :apply="apply">
              <button v-if="root" class="studio-canvas-root" @click="select(root.id)">
                {{ root.type }} · {{ root.id.slice(0, 8) }}
              </button>
              <div
                v-for="node in flat.filter((n) => n.id !== root?.id)"
                :key="node.id"
                class="studio-design-node"
                :class="{ active: selected === node.id }"
                :style="{
                  left: geometry(node).x + 'px',
                  top: geometry(node).y + 'px',
                  width: geometry(node).width + 'px',
                  height: geometry(node).height + 'px',
                }"
                @pointerdown.stop="pointer($event, node)"
                @click="select(node.id)"
              >
                <div class="studio-design-label">{{ node.type }} · {{ node.id.slice(0, 8) }}</div>
                <slot name="node" :node="node"
                  ><strong>{{ node.props.text ?? node.props.fieldId ?? node.type }}</strong></slot
                >
                <div
                  v-if="selected === node.id"
                  class="studio-resize"
                  @pointerdown.stop="pointer($event, node, true)"
                />
              </div>
            </slot>
          </div>
        </div>
      </main>
      <div
        v-if="rightOpen"
        class="studio-separator right"
        @pointerdown="resizeSidebar($event, 'right')"
      />
      <aside class="studio-sidebar" style="grid-column: 3; grid-row: 1">
        <slot name="properties" :node="focused" :apply="apply" :selected="selected" />
      </aside>
    </div>
  </div>
</template>
