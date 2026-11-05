<script setup lang="ts">
import {
  computed,
  nextTick,
  onActivated,
  onBeforeUnmount,
  onDeactivated,
  onMounted,
  shallowRef,
  watch,
} from 'vue'
import { Graph, Selection, Transform, type Edge } from '@antv/x6'
import { useI18n } from 'vue-i18n'
import GraphInteractionHint from './GraphInteractionHint.vue'
import GraphViewportControls from './GraphViewportControls.vue'
import { cellId, cellTarget, projectionCells } from './projection'
import type {
  ConnectionIntent,
  Diagnostic,
  GraphIntent,
  GraphView,
  LayoutView,
  Mode,
  Target,
  ViewportState,
} from './types'

const props = withDefaults(
  defineProps<{
    view: GraphView
    mode?: Mode
    readOnly?: boolean
    showInteractionTools?: boolean
    showInteractionHint?: boolean
    showViewportTools?: boolean
    selection?: readonly Target[]
    checkConnection?: (intent: ConnectionIntent) => readonly Diagnostic[]
  }>(),
  {
    mode: 'design',
    readOnly: false,
    showInteractionTools: true,
    showInteractionHint: true,
    showViewportTools: true,
    selection: () => [],
  },
)
const emit = defineEmits<{
  intent: [value: GraphIntent]
  selectionChange: [value: Target[]]
  viewportChange: [value: ViewportState]
  undo: []
  redo: []
  error: [message: string]
}>()
const { locale } = useI18n()
const zh = computed(() => locale.value.startsWith('zh'))
const container = shallowRef<HTMLElement | null>(null),
  zoom = shallowRef(1),
  error = shallowRef('')
const surface = shallowRef<HTMLElement | null>(null)
const tool = defineModel<'select' | 'pan' | 'connect'>('tool', { default: 'select' })
const editable = computed(
  () => props.mode === 'design' && !props.readOnly && props.view.scope.definition.kind === 'draft',
)
let graph: Graph | undefined,
  observer: ResizeObserver | undefined,
  selectionPlugin: Selection | undefined
let replaying = false,
  contextKey = '',
  frame = 0,
  disposed = false,
  gesture = false
let savedViewport: ViewportState | undefined
const transient = new Map<string, { x: number; y: number; width: number; height: number }>()
let renderedLayout: LayoutView = { nodes: [], edges: [] }
function viewportChanged() {
  if (!graph || replaying) return
  const translate = graph.translate()
  zoom.value = graph.zoom()
  savedViewport = { zoom: zoom.value, x: translate.tx, y: translate.ty }
  emit('viewportChange', savedViewport)
}
function fit() {
  if (graph && container.value?.clientWidth) {
    graph.zoomToFit({ padding: 40, minScale: 0.05, maxScale: 1 })
    viewportChanged()
  }
}
function setZoom(value: number) {
  if (Number.isFinite(value)) {
    graph?.zoomTo(Math.max(0.05, Math.min(3, value)))
    viewportChanged()
  }
}
function focus(target: Target) {
  const cell = graph?.getCellById(
    cellId(
      target.kind === 'port' ? 'node' : target.kind,
      target.kind === 'port' ? target.nodeId : target.id,
    ),
  )
  if (cell) graph?.centerCell(cell)
}
function syncTool() {
  selectionPlugin?.toggleRubberband(props.mode === 'design' && tool.value === 'select')
  selectionPlugin?.setRubberbandModifiers('shift')
  if (tool.value !== 'select') graph?.getPlugin<Transform>('transform')?.clearWidgets()
}
function readLayout(): LayoutView {
  if (!graph) return { nodes: [], edges: [] }
  return {
    nodes: graph.getNodes().flatMap((n) => {
      const target = cellTarget(n.id)
      return target?.kind === 'node' ? [{ id: target.id, ...n.position(), ...n.size() }] : []
    }),
    edges: graph.getEdges().flatMap((e) => {
      const target = cellTarget(e.id)
      return target?.kind === 'edge'
        ? [{ id: target.id, vertices: e.getVertices().map((p) => ({ x: p.x, y: p.y })) }]
        : []
    }),
  }
}
function scheduleLayout() {
  if (replaying || frame || disposed) return
  frame = requestAnimationFrame(() => {
    frame = 0
    if (!graph || replaying) return
    const layout = readLayout()
    const previousNodes = new Map(renderedLayout.nodes.map((n) => [n.id, n]))
    const previousEdges = new Map(renderedLayout.edges.map((e) => [e.id, e]))
    const nodes = layout.nodes.filter(
      (n) => JSON.stringify(n) !== JSON.stringify(previousNodes.get(n.id)),
    )
    const edges = layout.edges.filter(
      (e) => JSON.stringify(e) !== JSON.stringify(previousEdges.get(e.id)),
    )
    if (!nodes.length && !edges.length) return
    if (editable.value) emit('intent', { type: 'layout', nodes, edges })
    else if (props.mode !== 'design') {
      for (const n of nodes) transient.set(n.id, n)
      renderedLayout = layout
    } else render()
  })
}
function syncSelection() {
  if (!graph) return
  replaying = true
  try {
    graph.resetSelection(
      props.selection.flatMap((t) => {
        const c = graph!.getCellById(
          cellId(t.kind === 'port' ? 'node' : t.kind, t.kind === 'port' ? t.nodeId : t.id),
        )
        return c ? [c] : []
      }),
    )
    for (const e of graph.getEdges()) e.removeTools()
    if (editable.value && tool.value === 'select')
      for (const t of props.selection)
        if (t.kind === 'edge') {
          graph
            .getCellById(cellId('edge', t.id))
            ?.addTools(['vertices', 'source-arrowhead', 'target-arrowhead'])
        }
  } finally {
    replaying = false
  }
}
function render() {
  if (!graph) return
  const changed = contextKey !== props.view.scope.contextKey
  if (changed) {
    transient.clear()
    savedViewport = undefined
    contextKey = props.view.scope.contextKey
  }
  try {
    const view =
      props.mode === 'design'
        ? props.view
        : {
            ...props.view,
            layout: {
              ...props.view.layout,
              nodes: props.view.layout.nodes.map((n) => ({ ...n, ...transient.get(n.id) })),
            },
          }
    const cells = projectionCells(view, editable.value)
    replaying = true
    graph.getPlugin<Transform>('transform')?.clearWidgets()
    graph.fromJSON(cells)
    renderedLayout = readLayout()
    error.value = ''
    selectionPlugin?.toggleMultipleSelection(props.mode === 'design')
    syncTool()
    replaying = false
    syncSelection()
    if (changed) void nextTick(fit)
  } catch (cause) {
    replaying = false
    error.value = cause instanceof Error ? cause.message : String(cause)
    if (changed) graph.clearCells()
    emit('error', error.value)
  }
}
function connection(edge: Edge, isNew: boolean): ConnectionIntent {
  const source = cellTarget(edge.getSourceCellId() ?? ''),
    target = cellTarget(edge.getTargetCellId() ?? '')
  const current = cellTarget(edge.id)
  return {
    ...(!isNew && current?.kind === 'edge' ? { edgeId: current.id } : {}),
    source:
      source?.kind === 'node' ? { nodeId: source.id, portId: edge.getSourcePortId() ?? '' } : null,
    target:
      target?.kind === 'node' ? { nodeId: target.id, portId: edge.getTargetPortId() ?? '' } : null,
    edgeKind:
      current?.kind === 'edge'
        ? (props.view.edges.find((e) => e.id === current.id)?.kind ?? 'relation')
        : 'relation',
  }
}
function initialize() {
  if (graph || !container.value || !surface.value || disposed) return
  graph = new Graph({
    container: container.value,
    autoResize: false,
    width: surface.value.clientWidth || 800,
    height: surface.value.clientHeight || 500,
    grid: { size: 16, visible: true },
    background: { color: 'transparent' },
    scaling: { min: 0.05, max: 3 },
    panning: { enabled: true, eventTypes: ['leftMouseDown', 'rightMouseDown'] },
    mousewheel: { enabled: true, modifiers: ['ctrl', 'meta'], minScale: 0.05, maxScale: 3 },
    interacting: () => ({
      nodeMovable: tool.value === 'select' && (editable.value || props.mode !== 'design'),
      magnetConnectable: tool.value !== 'pan' && editable.value,
      edgeMovable: false,
      arrowheadMovable: editable.value && tool.value === 'select',
      vertexMovable: editable.value && tool.value === 'select',
      vertexAddable: editable.value && tool.value === 'select',
      vertexDeletable: editable.value && tool.value === 'select',
    }),
    connecting: {
      allowBlank: true,
      allowNode: false,
      allowEdge: false,
      allowLoop: true,
      allowMulti: true,
      snap: { radius: 24 },
      validateMagnet: ({ magnet }) => editable.value && magnet.getAttribute('magnet') !== 'passive',
      validateConnection: ({ sourceCell, targetCell, sourcePort, targetPort, edge }) => {
        if (!editable.value) return false
        const s = sourceCell && cellTarget(sourceCell.id),
          t = targetCell && cellTarget(targetCell.id)
        if (!s || s.kind !== 'node' || !sourcePort) return false
        if (!t || t.kind !== 'node' || !targetPort) return true
        const source = props.view.nodes
          .find((n) => n.id === s.id)
          ?.ports.find((p) => p.id === sourcePort)
        const target = props.view.nodes
          .find((n) => n.id === t.id)
          ?.ports.find((p) => p.id === targetPort)
        if (!source || source.role === 'in' || !target || target.role === 'out') return false
        const existing = edge && cellTarget(edge.id)
        const candidate: ConnectionIntent = {
          source: { nodeId: s.id, portId: sourcePort },
          target: { nodeId: t.id, portId: targetPort },
          ...(existing?.kind === 'edge' ? { edgeId: existing.id } : {}),
          edgeKind:
            existing?.kind === 'edge'
              ? (props.view.edges.find((e) => e.id === existing.id)?.kind ?? 'relation')
              : 'relation',
        }
        return !(props.checkConnection?.(candidate) ?? []).some((d) => d.severity === 'error')
      },
    },
  })
  selectionPlugin = new Selection({
    enabled: true,
    multiple: props.mode === 'design',
    rubberband: props.mode === 'design',
    modifiers: 'shift',
    movable: false,
    // Keep the selection outline visual; node gestures own dragging and layout commits.
    pointerEvents: 'none',
    showNodeSelectionBox: true,
    showEdgeSelectionBox: true,
  })
  graph.use(selectionPlugin)
  graph.use(
    new Transform({
      resizing: {
        enabled: () => editable.value && tool.value === 'select',
        minWidth: 60,
        minHeight: 40,
        maxWidth: 10000,
        maxHeight: 10000,
      },
      rotating: false,
    }),
  )
  graph.on('selection:changed', ({ selected }) => {
    if (!replaying)
      emit(
        'selectionChange',
        selected.flatMap((c) => {
          const t = cellTarget(c.id)
          return t ? [t] : []
        }),
      )
  })
  graph.on('node:moved', scheduleLayout)
  graph.on('node:resized', scheduleLayout)
  graph.on('edge:mouseup', scheduleLayout)
  graph.on('edge:connected', ({ edge, isNew }) => {
    if (replaying || !editable.value) return
    const intent = connection(edge, isNew)
    if (isNew && (!intent.source || !intent.target)) {
      render()
      return
    }
    const failures = props.checkConnection?.(intent) ?? []
    if (!failures.some((d) => d.severity === 'error') || !intent.source || !intent.target)
      emit('intent', { type: 'connect', connection: intent })
    else emit('error', failures.map((d) => d.message).join('\n'))
    void nextTick(render)
  })
  graph.on('scale', viewportChanged)
  graph.on('translate', viewportChanged)
  observer = new ResizeObserver(() => {
    if (graph && surface.value?.clientWidth && surface.value.clientHeight) {
      graph.resize(surface.value.clientWidth, surface.value.clientHeight)
      if (!savedViewport) fit()
    }
  })
  observer.observe(surface.value)
  render()
  if (savedViewport) {
    graph.zoomTo(savedViewport.zoom)
    graph.translate(savedViewport.x, savedViewport.y)
  }
}
function release() {
  if (frame) cancelAnimationFrame(frame)
  frame = 0
  observer?.disconnect()
  observer = undefined
  // X6 3.1.7 clears its model before plugins; Transform then skips detached widgets.
  // Dispose the owned plugins while their nodes still exist so DOM listeners are removed.
  // X6 clears selection during disposal. That is teardown, not a user selection
  // change: emitting it would erase the retained execution page's node details.
  const wasReplaying = replaying
  replaying = true
  try {
    graph?.disposePlugins(['transform', 'selection'])
    graph?.dispose()
    // GraphView.dispose() restores the host but omits View.remove()'s registry cleanup.
    graph?.view.remove()
  } finally {
    replaying = wasReplaying
  }
  graph = undefined
  selectionPlugin = undefined
  gesture = false
}
function cancelGesture() {
  if (gesture) {
    gesture = false
    if (frame) cancelAnimationFrame(frame)
    frame = 0
    render()
  }
}
function beginGesture() {
  gesture = true
}
function endGesture() {
  gesture = false
}
function keydown(event: KeyboardEvent) {
  if (
    event.isComposing ||
    (event.target as HTMLElement).closest('input,textarea,[contenteditable="true"]')
  )
    return
  if (event.key === 'Escape') {
    cancelGesture()
    tool.value = 'select'
    return
  }
  if (!editable.value) return
  const key = event.key.toLowerCase()
  if ((event.ctrlKey || event.metaKey) && (key === 'z' || key === 'y')) {
    event.preventDefault()
    if (key === 'y' || event.shiftKey) emit('redo')
    else emit('undo')
  }
  if ((event.key === 'Delete' || event.key === 'Backspace') && props.selection.length) {
    event.preventDefault()
    emit('intent', { type: 'remove', targets: props.selection })
  }
}
function drop(event: DragEvent) {
  if (!editable.value || !graph) return
  const key = event.dataTransfer?.getData('application/x-studio-graph-node')
  if (!key) return
  event.preventDefault()
  const p = graph.clientToLocal(event.clientX, event.clientY)
  emit('intent', { type: 'addNode', catalogKey: key, at: { x: p.x, y: p.y } })
}
watch(() => props.view, render)
watch(() => props.selection, syncSelection)
watch(tool, () => {
  syncTool()
  syncSelection()
})
watch([() => props.mode, () => props.readOnly], () => {
  cancelGesture()
  render()
})
onMounted(initialize)
onActivated(() => {
  void nextTick(initialize)
})
onDeactivated(release)
onBeforeUnmount(() => {
  disposed = true
  release()
  transient.clear()
})
defineExpose({ fit, focus, setZoom })
</script>

<template>
  <section
    class="graph-workbench"
    tabindex="0"
    :aria-label="zh ? '图形工作台' : 'Graph workbench'"
    @keydown="keydown"
    @pointerdown.capture="beginGesture"
    @pointercancel.capture="cancelGesture"
    @pointerup="endGesture"
    @dragover.prevent
    @drop="drop"
  >
    <div
      v-if="showInteractionTools || showInteractionHint || showViewportTools"
      class="graph-tools"
    >
      <el-button-group v-if="showInteractionTools">
        <el-button
          size="small"
          :type="tool === 'select' ? 'primary' : 'default'"
          :aria-pressed="tool === 'select'"
          @click="tool = 'select'"
          >{{ zh ? '选择' : 'Select' }}</el-button
        >
        <el-button
          size="small"
          :type="tool === 'pan' ? 'primary' : 'default'"
          :aria-pressed="tool === 'pan'"
          @click="tool = 'pan'"
          >{{ zh ? '抓手' : 'Pan' }}</el-button
        >
      </el-button-group>
      <GraphViewportControls
        v-if="showViewportTools"
        :zoom="zoom"
        :locale="locale"
        @zoom="setZoom"
        @fit="fit"
      />
      <GraphInteractionHint
        v-if="showInteractionHint"
        class="graph-hint"
        :tool="tool"
        :locale="locale"
      />
    </div>
    <el-alert v-if="error" :title="error" type="error" :closable="false" />
    <div ref="surface" class="graph-surface"><div ref="container" /></div>
    <div v-if="!view.nodes.length && !error" class="graph-empty">
      {{ zh ? '暂无图形数据' : 'No graph data' }}
    </div>
  </section>
</template>

<style scoped>
.graph-workbench {
  position: relative;
  display: flex;
  flex-direction: column;
  height: 100%;
  width: 100%;
  min-width: 0;
  min-height: 480px;
  outline: none;
}
.graph-workbench:focus-visible {
  outline: 2px solid var(--el-color-primary);
  outline-offset: -2px;
}
.graph-tools {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 8px 12px;
  border-bottom: 1px solid var(--el-border-color-light);
  flex-wrap: wrap;
}
.graph-hint {
  flex: 1 1 160px;
  min-width: 0;
  overflow-wrap: anywhere;
  color: var(--el-text-color-secondary);
  font-size: 12px;
  margin-left: auto;
}
.graph-surface {
  flex: 1;
  min-width: 0;
  min-height: 400px;
  overflow: hidden;
}
.graph-empty {
  position: absolute;
  top: 50%;
  left: 50%;
  transform: translate(-50%, -50%);
  color: var(--el-text-color-secondary);
  pointer-events: none;
}
</style>
