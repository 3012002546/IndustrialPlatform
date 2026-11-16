import type {
  Box,
  ConnectionIntent,
  Diagnostic,
  GraphScope,
  GraphView,
  LayoutView,
  RuntimeInstance,
  RuntimeSnapshot,
} from '@/components/studio/graph/types'

export interface ProcessDefinition {
  id: string
  revisionNId: string
  activities: {
    id: string
    name: string
    kind: 'task' | 'parallelSplit' | 'parallelJoin'
  }[]
  transitions: { id: string; from: string; to: string }[]
  layout: LayoutView
}

const lanes = [
  ['issue', '领料', 0, 120],
  ['weigh', '称量', 195, 120],
  ['split', '并行开始', 390, 120],
  ['process', '加工', 600, 10],
  ['inspect', '检验', 600, 230],
  ['join', '汇合', 815, 120],
  ['release', '放行', 1010, 120],
] as const
const paths = [
  ['issue', 'weigh'],
  ['weigh', 'split'],
  ['split', 'process'],
  ['split', 'inspect'],
  ['process', 'join'],
  ['inspect', 'join'],
  ['join', 'release'],
] as const

export const published: ProcessDefinition = {
  id: 'fixture-process',
  revisionNId: 'fixture-process-r1',
  activities: lanes.map(([id, name]) => ({
    id,
    name,
    kind: id === 'split' ? 'parallelSplit' : id === 'join' ? 'parallelJoin' : 'task',
  })),
  transitions: paths.map(([from, to], i) => ({ id: 'c' + (i + 1), from, to })),
  layout: {
    nodes: lanes.map(([id, , x, y]) => ({ id, x, y, width: 166, height: 76 })),
    edges: paths.map((_, i) => ({ id: 'c' + (i + 1), vertices: [] })),
  },
}
const ports = [
  { id: 'in', role: 'in', side: 'left', order: 0 },
  { id: 'out', role: 'out', side: 'right', order: 0 },
] as const

export function project(def: ProcessDefinition, scope: GraphScope): GraphView {
  return {
    graphContractVersion: 1,
    scope,
    kind: scope.definition.kind === 'draft' ? 'definition' : 'runtime',
    nodes: def.activities.map((a) => ({
      id: a.id,
      definitionNodeId: a.id,
      kind: 'production.' + a.kind,
      label: a.name,
      ports,
    })),
    edges: def.transitions.map((t) => ({
      id: t.id,
      definitionEdgeId: t.id,
      kind: 'control',
      label: '',
      source: { nodeId: t.from, portId: 'out' },
      target: { nodeId: t.to, portId: 'in' },
    })),
    layout: def.layout,
    diagnostics: [],
  }
}

export function checkConnection(def: ProcessDefinition, intent: ConnectionIntent): Diagnostic[] {
  const { source, target, edgeId } = intent
  const ids = new Set(def.activities.map((a) => a.id))
  const invalid =
    !source ||
    !target ||
    source.portId !== 'out' ||
    target.portId !== 'in' ||
    !ids.has(source.nodeId) ||
    !ids.has(target.nodeId) ||
    source.nodeId === target.nodeId ||
    def.transitions.some(
      (t) => t.id !== edgeId && t.from === source.nodeId && t.to === target.nodeId,
    )
  return invalid
    ? [
        {
          id: 'connection',
          severity: 'error',
          code: 'invalid-control',
          message: '请选择不同活动的输出和输入端口，同方向的控制线只能有一条。',
        },
      ]
    : []
}

export function patchBox(def: ProcessDefinition, id: string, patch: Partial<Box>) {
  def.layout.nodes = def.layout.nodes.map((n) => (n.id === id ? { ...n, ...patch } : n))
}

export type Frame = 1 | 2
const at = (minute: number) => '2026-10-31T00:' + String(minute).padStart(2, '0') + ':00Z'
const states = {
  Completed: ['已完成', 'success'],
  Running: ['运行中', 'info'],
  Waiting: ['等待', 'neutral'],
  Failed: ['失败', 'danger'],
} as const

export function snapshot(def: ProcessDefinition, frame: Frame): RuntimeSnapshot {
  if (def.id !== published.id || def.revisionNId !== 'fixture-process-r1')
    throw new Error('该批次的回放需要流程修订 r1。')
  const scope: GraphScope = {
    contextKey: 'production-run',
    definition: {
      kind: 'published',
      resourceNId: def.id,
      revisionNId: def.revisionNId,
    },
  }
  const graph = project(def, scope)
  const instance = (
    node: string,
    id: string,
    iteration: string,
    state: keyof typeof states,
    start: number | null,
    end: number | null,
  ): RuntimeInstance => ({
    id,
    definitionNodeId: node,
    iteration,
    attempt: '1',
    stateKey: state,
    stateLabel: states[state][0],
    tone: states[state][1],
    startedOn: start === null ? null : at(start),
    endedOn: end === null ? null : at(end),
    detailRef: 'detail:' + id,
  })
  const done = frame === 2
  const instances = [
    instance('issue', 'i-issue', '1', 'Completed', 0, 1),
    instance('weigh', 'i-weigh', '1', 'Completed', 1, 2),
    instance('split', 'i-split', '1', 'Completed', 2, 2),
    instance('process', 'i-process', '1', 'Completed', 2, 6),
    instance('inspect', 'i-inspect-1', '1', 'Failed', 2, 4),
    instance('inspect', 'i-inspect-2', '2', done ? 'Completed' : 'Running', 4, done ? 7 : null),
    instance('join', 'i-join', '1', done ? 'Completed' : 'Waiting', 2, done ? 7 : null),
    instance(
      'release',
      'i-release',
      '1',
      done ? 'Completed' : 'Waiting',
      done ? 7 : null,
      done ? 8 : null,
    ),
  ]
  const overlays = def.activities.map((a) => {
    const items = instances.filter((i) => i.definitionNodeId === a.id)
    const first = items[0]!
    return {
      nodeId: a.id,
      instanceIds: items.map((i) => i.id),
      label:
        items.length === 1 ? first.stateLabel : '2 次 · 1 失败 / ' + (done ? '1 完成' : '1 运行'),
      tone: items.length === 1 ? first.tone : ('warning' as const),
    }
  })
  graph.nodes = graph.nodes.map((n, i) => ({
    ...n,
    label: n.label + '\n' + overlays[i]!.label,
    tone: overlays[i]!.tone,
  }))
  const time = at(done ? 8 : 6)
  return {
    graphContractVersion: 1,
    graph,
    instances,
    overlays,
    stamp: {
      scope,
      runKey: 'process-001',
      generation: '1',
      responseVersion: String(frame),
      requestId: 'fixture-p' + frame,
    },
    observedOn: time,
    receivedOn: time,
    completeness: 'complete',
    reasons: [],
    observations: [
      {
        sourceKey: 'fixture-production',
        observedOn: time,
        receivedOn: time,
        quality: 'known',
        reason: '模拟状态',
      },
    ],
  }
}
