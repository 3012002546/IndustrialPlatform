<script setup lang="ts">
import { computed, ref } from 'vue'
import GraphWorkbench from '@/components/studio/graph/GraphWorkbench.vue'
import type { GraphIntent, GraphView, Target } from '@/components/studio/graph/types'
import { createHistory } from '@/components/studio/shared/model'
const ports = [
  { id: 'in', role: 'in' as const, side: 'left' as const, order: 0 },
  { id: 'out', role: 'out' as const, side: 'right' as const, order: 0 },
]
function fixture(kind = 'trace', large = false): GraphView {
  const ids = large
    ? Array.from({ length: 1000 }, (_, i) => String(i))
    : kind === 'trace'
      ? ['A', 'B', 'C', 'F2/A']
      : ['issue', 'weigh', 'split', 'process', 'inspect', 'join', 'release']
  const pairs = large
    ? Array.from({ length: 3000 }, (_, i) => [String(i % 1000), String((i + 1) % 1000)])
    : kind === 'trace'
      ? [
          ['A', 'C'],
          ['A', 'C'],
          ['B', 'C'],
          ['C', 'A'],
        ]
      : [
          ['issue', 'weigh'],
          ['weigh', 'split'],
          ['split', 'process'],
          ['split', 'inspect'],
          ['process', 'join'],
          ['inspect', 'join'],
          ['join', 'release'],
        ]
  return {
    graphContractVersion: 1,
    kind: 'definition',
    scope: {
      contextKey: kind + (large ? '-large' : ''),
      definition: { kind: 'draft', resourceNId: kind, documentStamp: '1' },
    },
    nodes: ids.map((id) => ({ id, kind, label: id, ports, definitionNodeId: id })),
    edges: pairs.map(([a, b], i) => ({
      id: 'e' + i,
      kind,
      label: 'e' + i,
      source: { nodeId: a!, portId: 'out' },
      target: { nodeId: b!, portId: 'in' },
    })),
    layout: {
      nodes: ids.map((id, i) => ({
        id,
        x: 60 + (i % (large ? 40 : 4)) * 220,
        y: 80 + Math.floor(i / (large ? 40 : 4)) * 160,
        width: 160,
        height: 80,
      })),
      edges: pairs.map((_, i) => ({ id: 'e' + i, vertices: [] })),
    },
    diagnostics: [],
  }
}
const view = ref(fixture()),
  selection = ref<Target[]>([]),
  mode = ref<'design' | 'run'>('design'),
  mounted = ref(true)
let history = createHistory(view.value),
  saved = JSON.stringify(view.value)
const commits = ref(0),
  error = ref(''),
  iteration = ref(1)
const instances = computed(() => [
  { id: 'inspect-1', iteration: 1, state: 'Failed' },
  { id: 'inspect-2', iteration: 2, state: iteration.value === 1 ? 'Running' : 'Completed' },
])
function change(kind: string, large = false) {
  view.value = fixture(kind, large)
  history = createHistory(view.value)
  saved = JSON.stringify(view.value)
  selection.value = []
  commits.value = 0
}
function edit(intent: GraphIntent) {
  const doc = structuredClone(JSON.parse(JSON.stringify(view.value))) as GraphView
  if (intent.type === 'layout') {
    doc.layout.nodes = doc.layout.nodes.map((n) => intent.nodes.find((x) => x.id === n.id) ?? n)
    doc.layout.edges = doc.layout.edges.map((e) => intent.edges.find((x) => x.id === e.id) ?? e)
  }
  if (intent.type === 'connect' && intent.connection.source && intent.connection.target) {
    const id = intent.connection.edgeId ?? 'e' + doc.edges.length
    doc.edges = [
      ...doc.edges.filter((e) => e.id !== id),
      {
        id,
        kind: 'relation',
        label: id,
        source: intent.connection.source,
        target: intent.connection.target,
      },
    ]
    doc.layout.edges = [...doc.layout.edges.filter((e) => e.id !== id), { id, vertices: [] }]
  }
  if (intent.type === 'remove') {
    const ns = new Set(intent.targets.filter((t) => t.kind === 'node').map((t) => t.id))
    const es = new Set(intent.targets.filter((t) => t.kind === 'edge').map((t) => t.id))
    doc.nodes = doc.nodes.filter((n) => !ns.has(n.id))
    doc.edges = doc.edges.filter(
      (e) => !es.has(e.id) && !ns.has(e.source?.nodeId ?? '') && !ns.has(e.target?.nodeId ?? ''),
    )
    doc.layout.nodes = doc.layout.nodes.filter((n) => !ns.has(n.id))
    doc.layout.edges = doc.layout.edges.filter((e) => doc.edges.some((x) => x.id === e.id))
    selection.value = []
  }
  view.value = doc
  history.commit(doc)
  commits.value++
}
function undo() {
  view.value = history.undo()
}
function redo() {
  view.value = history.redo()
}
function compound() {
  const doc = structuredClone(JSON.parse(JSON.stringify(view.value))) as GraphView
  doc.nodes = doc.nodes.map((n) => (n.id === 'A' ? { ...n, label: 'renamed' } : n))
  doc.layout.nodes = doc.layout.nodes.map((n) => (n.id === 'A' ? { ...n, x: n.x + 100 } : n))
  view.value = doc
  history.commit(doc)
  commits.value++
}
function snapshot() {
  iteration.value = 2
  view.value = {
    ...view.value,
    nodes: view.value.nodes.map((n) =>
      n.id === 'inspect'
        ? { ...n, label: '2 iterations: 1 failed, 1 completed', tone: 'warning' }
        : n,
    ),
  }
}
Object.assign(window, {
  graphHarness: {
    change,
    edit,
    undo,
    redo,
    compound,
    snapshot,
    getView: () => JSON.parse(JSON.stringify(view.value)),
    getCommits: () => commits.value,
    toggle: () => (mounted.value = !mounted.value),
  },
})
</script>
<template>
  <button @click="change('trace')">Trace</button
  ><button @click="change('production')">Production</button
  ><button @click="change('trace', true)">1000/3000</button>
  <button @click="mode = mode === 'design' ? 'run' : 'design'">Readonly</button
  ><button @click="undo">Undo</button><button @click="redo">Redo</button>
  <button @click="compound">Compound</button
  ><button @click="saved = JSON.stringify(view)">Save</button
  ><button @click="view = JSON.parse(saved)">Reload</button
  ><button @click="mounted = !mounted">Mount</button><button @click="snapshot">Snapshot 2</button>
  <output data-testid="count">{{ view.nodes.length }}/{{ view.edges.length }}</output
  ><output data-testid="selection">{{ JSON.stringify(selection) }}</output
  ><output data-testid="commits">{{ commits }}</output
  ><output>{{ error }}</output>
  <div style="height: 650px">
    <GraphWorkbench
      v-if="mounted"
      :view="view"
      :mode="mode"
      :selection="selection"
      @selection-change="selection = $event"
      @intent="edit"
      @undo="undo"
      @redo="redo"
      @error="error = $event"
    />
  </div>
  <pre v-if="view.scope.contextKey === 'production'" data-testid="instances">{{ instances }}</pre>
</template>
