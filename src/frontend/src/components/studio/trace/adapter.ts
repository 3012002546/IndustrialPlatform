import type {
  ConnectionIntent,
  Diagnostic,
  DomainGraphAdapter,
  GraphScope,
  GraphView,
  Target,
} from '../graph/types'
import { clone } from '../shared/model'
import {
  newTraceObject,
  newTraceRelation,
  type TraceDocument,
  type TraceLayoutDirection,
  type TraceObject,
  type TraceRelation,
} from './model'
import type { TraceResult } from '@/api/studioTrace'
import { traceObjectKinds } from './catalogue'

export type TracePropertyCommand =
  | {
      type: 'trace.start'
      start: TraceDocument['start']
      parameters: TraceDocument['parameters']
      scope: TraceDocument['scope']
      limits: TraceDocument['limits']
      presentation?: TraceDocument['presentation']
    }
  | { type: 'trace.object'; object: TraceObject }
  | { type: 'trace.relation'; relation: TraceRelation }
const ports = [
  { id: 'in', role: 'in' as const, side: 'left' as const, order: 0 },
  { id: 'out', role: 'out' as const, side: 'right' as const, order: 1 },
]
const diagnostic = (
  path: string,
  message: string,
  target?: Target,
  code = 'TRACE_MAPPING_INVALID',
): Diagnostic => ({
  id: path,
  code,
  severity: 'error',
  path,
  message,
  ...(target ? { target } : {}),
})
const finite = (n: number, min: number, max: number) => Number.isFinite(n) && n >= min && n <= max
export function traceStructureErrors(doc: TraceDocument): Diagnostic[] {
  const errors: Diagnostic[] = []
  if (
    doc.presentation &&
    !['LeftToRight', 'TopToBottom'].includes(doc.presentation.layoutDirection)
  )
    errors.push(diagnostic('presentation.layoutDirection', 'Invalid graph layout direction'))
  for (const [name, items] of [
    ['objects', doc.objects],
    ['relations', doc.relations],
  ] as const) {
    const ids = new Set<string>()
    for (const [index, item] of items.entries()) {
      if (!item.id || ids.has(item.id))
        errors.push(
          diagnostic(
            `${name}[${index}].id`,
            'Stable ID is missing or duplicated',
            undefined,
            'GRAPH_STRUCTURE_INVALID',
          ),
        )
      ids.add(item.id)
    }
  }
  const nodeIds = new Set(doc.objects.map((o) => o.id)),
    edgeIds = new Set(doc.relations.map((r) => r.id))
  const seenNodes = new Set<string>(),
    seenEdges = new Set<string>()
  for (const [i, n] of doc.layout.nodes.entries()) {
    if (
      !nodeIds.has(n.objectTypeId) ||
      seenNodes.has(n.objectTypeId) ||
      !finite(n.x, -1e6, 1e6) ||
      !finite(n.y, -1e6, 1e6) ||
      !finite(n.width, 1, 10000) ||
      !finite(n.height, 1, 10000)
    )
      errors.push(
        diagnostic(
          `layout.nodes[${i}]`,
          'Invalid node geometry or reference',
          undefined,
          'GRAPH_STRUCTURE_INVALID',
        ),
      )
    seenNodes.add(n.objectTypeId)
  }
  for (const [i, e] of doc.layout.edges.entries()) {
    if (
      !edgeIds.has(e.relationId) ||
      seenEdges.has(e.relationId) ||
      !Array.isArray(e.vertices) ||
      e.vertices.some((p) => !finite(p.x, -1e6, 1e6) || !finite(p.y, -1e6, 1e6)) ||
      e.sourcePort === undefined ||
      e.targetPort === undefined
    )
      errors.push(
        diagnostic(
          `layout.edges[${i}]`,
          'Invalid edge geometry or reference',
          undefined,
          'GRAPH_STRUCTURE_INVALID',
        ),
      )
    seenEdges.add(e.relationId)
  }
  return errors
}
export function validateTrace(doc: TraceDocument): Diagnostic[] {
  const errors = traceStructureErrors(doc)
  if (!doc.start.datasetRevisionNId)
    errors.push(diagnostic('start.datasetRevisionNId', 'Select a published start Dataset'))
  if (
    !doc.start.objectTypeIds.length ||
    doc.start.objectTypeIds.some((id) => !doc.objects.some((o) => o.id === id))
  )
    errors.push(diagnostic('start.objectTypeIds', 'Select valid start object types'))
  for (const [i, o] of doc.objects.entries()) {
    if (!o.name.trim())
      errors.push(
        diagnostic(`objects[${i}].name`, 'Object name is required', { kind: 'node', id: o.id }),
      )
    const sectionIds = new Set<string>()
    for (const [j, section] of (o.detail.sections ?? []).entries()) {
      const path = `objects[${i}].detail.sections[${j}]`
      const target: Target = { kind: 'node', id: o.id }
      if (!section.id || sectionIds.has(section.id))
        errors.push(diagnostic(`${path}.id`, 'Detail section ID is missing or duplicated', target))
      sectionIds.add(section.id)
      if (!section.label.trim())
        errors.push(diagnostic(`${path}.label`, 'Detail section name is required', target))
      if (!section.datasetRevisionNId)
        errors.push(
          diagnostic(`${path}.datasetRevisionNId`, 'Select a published detail Dataset', target),
        )
      if (!section.parameterMap.some((mapping) => ['Node', 'Row'].includes(mapping.source.from)))
        errors.push(
          diagnostic(
            `${path}.parameterMap`,
            'Detail section requires a node context parameter',
            target,
          ),
        )
    }
    if (!o.detail.datasetRevisionNId)
      errors.push(
        diagnostic(`objects[${i}].detail.datasetRevisionNId`, 'Select a published detail Dataset', {
          kind: 'node',
          id: o.id,
        }),
      )
  }
  for (const [i, r] of doc.relations.entries()) {
    const target: Target = { kind: 'edge', id: r.id }
    if (
      !doc.objects.some((o) => o.id === r.fromTypeId) ||
      !doc.objects.some((o) => o.id === r.toTypeId)
    )
      errors.push(diagnostic(`relations[${i}]`, 'Relation endpoints must exist', target))
    for (const direction of ['forward', 'reverse'] as const) {
      if (!r[direction].datasetRevisionNId)
        errors.push(
          diagnostic(
            `relations[${i}].${direction}.datasetRevisionNId`,
            'Both traversal directions require a Dataset',
            target,
          ),
        )
      if (
        r[direction].result.targetObjectTypeId !==
        (direction === 'forward' ? r.toTypeId : r.fromTypeId)
      )
        errors.push(
          diagnostic(
            `relations[${i}].${direction}.result.targetObjectTypeId`,
            'Traversal output does not match the endpoint; repair the mapping',
            target,
          ),
        )
    }
    const layoutIndex = doc.layout.edges.findIndex((e) => e.relationId === r.id),
      layout = doc.layout.edges[layoutIndex]
    if (layout)
      for (const [key, expected] of [
        ['sourcePort', 'out'],
        ['targetPort', 'in'],
      ] as const)
        if (layout[key] !== expected)
          errors.push(
            diagnostic(
              `layout.edges[${layoutIndex}].${key}`,
              'Relation port is disconnected or unknown',
              target,
            ),
          )
  }
  return errors
}
export function projectTrace(doc: TraceDocument, scope: GraphScope): GraphView {
  return {
    graphContractVersion: 1,
    scope,
    kind: 'definition',
    diagnostics: validateTrace(doc),
    nodes: doc.objects.map((o, i) => ({
      id: o.id,
      definitionNodeId: o.id,
      kind: 'trace.object',
      label: o.name.trim() || `对象类型 ${i + 1}`,
      ports,
    })),
    edges: doc.relations.map((r) => {
      const l = doc.layout.edges.find((e) => e.relationId === r.id)
      return {
        id: r.id,
        definitionEdgeId: r.id,
        kind: `trace.${r.kind}`,
        label: r.name || r.semantic,
        source:
          l?.sourcePort === null ? null : { nodeId: r.fromTypeId, portId: l?.sourcePort ?? 'out' },
        target:
          l?.targetPort === null ? null : { nodeId: r.toTypeId, portId: l?.targetPort ?? 'in' },
        tone: r.kind === 'Context' ? 'info' : 'neutral',
      }
    }),
    layout: {
      nodes: doc.objects.map((o, i) => {
        const n = doc.layout.nodes.find((n) => n.objectTypeId === o.id)
        return {
          id: o.id,
          x: n?.x ?? 80 + 240 * (i % 4),
          y: n?.y ?? 80 + 160 * Math.floor(i / 4),
          width: n?.width ?? (finite(o.style.width, 1, 10000) ? o.style.width : 180),
          height: n?.height ?? (finite(o.style.height, 1, 10000) ? o.style.height : 80),
        }
      }),
      edges: doc.relations.map((r) => ({
        id: r.id,
        vertices: clone(doc.layout.edges.find((e) => e.relationId === r.id)?.vertices ?? []),
      })),
    },
  }
}
export function checkTraceConnection(
  doc: TraceDocument,
  connection: ConnectionIntent,
): Diagnostic[] {
  if (!connection.edgeId && (!connection.source || !connection.target))
    return [diagnostic('connection', 'Choose both endpoints')]
  for (const [end, port] of [
    [connection.source, 'out'],
    [connection.target, 'in'],
  ] as const)
    if (end && (!doc.objects.some((o) => o.id === end.nodeId) || end.portId !== port))
      return [diagnostic('connection', 'Connection must join an output to an input')]
  return []
}
export const traceAdapter: DomainGraphAdapter<TraceDocument, TracePropertyCommand> = {
  key: 'trace',
  graphContractVersion: 1,
  catalog: () => [
    { key: 'trace.object', label: 'Object', group: 'trace', enabled: true },
    ...traceObjectKinds.map((item) => ({
      key: `trace.object.${item.grain}`,
      label: item.name,
      group: 'trace',
      enabled: true,
    })),
  ],
  validate: validateTrace,
  checkConnection: checkTraceConnection,
  project(doc, scope) {
    const errors = traceStructureErrors(doc)
    return errors.length
      ? { ok: false, code: 'GRAPH_STRUCTURE_INVALID', diagnostics: errors }
      : { ok: true, value: projectTrace(doc, scope), diagnostics: validateTrace(doc) }
  },
  edit(document, command, context) {
    if (context.scope.definition.kind !== 'draft')
      return { ok: false, code: 'GRAPH_READ_ONLY', diagnostics: [] }
    if (context.expectedDocumentStamp !== context.scope.definition.documentStamp)
      return { ok: false, code: 'GRAPH_CONTEXT_STALE', diagnostics: [] }
    const doc = clone(document) as TraceDocument
    let selection: Target[] = []
    switch (command.type) {
      case 'addNode': {
        const preset = traceObjectKinds.find(
          (item) => command.catalogKey === `trace.object.${item.grain}`,
        )
        if (command.catalogKey !== 'trace.object' && !preset)
          return { ok: false, code: 'GRAPH_STRUCTURE_INVALID', diagnostics: [] }
        const name = preset?.name ?? '对象类型'
        let number = doc.objects.length + 1
        while (doc.objects.some((o) => o.name.trim() === `${name} ${number}`)) number++
        const o = newTraceObject(crypto.randomUUID(), `${name} ${number}`)
        if (preset) o.identity.grain = preset.grain
        doc.objects.push(o)
        doc.layout.nodes.push({ objectTypeId: o.id, ...command.at, width: 180, height: 80 })
        selection = [{ kind: 'node', id: o.id }]
        break
      }
      case 'remove': {
        const nodes = new Set(command.targets.filter((t) => t.kind === 'node').map((t) => t.id)),
          edges = new Set(command.targets.filter((t) => t.kind === 'edge').map((t) => t.id))
        doc.objects = doc.objects.filter((o) => !nodes.has(o.id))
        doc.relations = doc.relations.filter(
          (r) => !nodes.has(r.fromTypeId) && !nodes.has(r.toTypeId) && !edges.has(r.id),
        )
        doc.layout.nodes = doc.layout.nodes.filter((n) => !nodes.has(n.objectTypeId))
        doc.layout.edges = doc.layout.edges.filter((e) =>
          doc.relations.some((r) => r.id === e.relationId),
        )
        doc.start.objectTypeIds = doc.start.objectTypeIds.filter((id) => !nodes.has(id))
        break
      }
      case 'connect': {
        const c = command.connection,
          errors = checkTraceConnection(doc, c)
        if (errors.length) return { ok: false, code: 'TRACE_MAPPING_INVALID', diagnostics: errors }
        let r = doc.relations.find((r) => r.id === c.edgeId)
        if (!r) {
          if (c.edgeId || !c.source || !c.target)
            return { ok: false, code: 'GRAPH_STRUCTURE_INVALID', diagnostics: [] }
          r = newTraceRelation(c.source.nodeId, c.target.nodeId)
          doc.relations.push(r)
        }
        if (c.source) r.fromTypeId = c.source.nodeId
        if (c.target) r.toTypeId = c.target.nodeId
        let edge = doc.layout.edges.find((e) => e.relationId === r.id)
        if (!edge) {
          edge = { relationId: r.id, sourcePort: 'out', targetPort: 'in', vertices: [] }
          doc.layout.edges.push(edge)
        }
        edge.sourcePort = c.source?.portId ?? null
        edge.targetPort = c.target?.portId ?? null
        selection = [{ kind: 'edge', id: r.id }]
        break
      }
      case 'layout':
        for (const n of command.nodes) {
          doc.layout.nodes = doc.layout.nodes.filter((l) => l.objectTypeId !== n.id)
          doc.layout.nodes.push({
            objectTypeId: n.id,
            x: n.x,
            y: n.y,
            width: n.width,
            height: n.height,
          })
        }
        for (const e of command.edges) {
          const old = doc.layout.edges.find((l) => l.relationId === e.id)
          doc.layout.edges = doc.layout.edges.filter((l) => l.relationId !== e.id)
          doc.layout.edges.push({
            relationId: e.id,
            sourcePort: old ? old.sourcePort : 'out',
            targetPort: old ? old.targetPort : 'in',
            vertices: clone([...e.vertices]),
          })
        }
        break
      case 'trace.start':
        doc.start = clone(command.start)
        doc.parameters = clone(command.parameters)
        doc.scope = clone(command.scope)
        doc.limits = clone(command.limits)
        if (command.presentation) doc.presentation = clone(command.presentation)
        break
      case 'trace.object': {
        const index = doc.objects.findIndex((o) => o.id === command.object.id)
        if (index < 0) return { ok: false, code: 'GRAPH_CONTEXT_STALE', diagnostics: [] }
        doc.objects[index] = clone(command.object)
        const layout = doc.layout.nodes.find((n) => n.objectTypeId === command.object.id)
        if (layout) {
          layout.width = command.object.style.width
          layout.height = command.object.style.height
        }
        selection = [{ kind: 'node', id: command.object.id }]
        break
      }
      case 'trace.relation': {
        const index = doc.relations.findIndex((r) => r.id === command.relation.id)
        if (index < 0) return { ok: false, code: 'GRAPH_CONTEXT_STALE', diagnostics: [] }
        doc.relations[index] = clone(command.relation)
        selection = [{ kind: 'edge', id: command.relation.id }]
        break
      }
    }
    const errors = traceStructureErrors(doc)
    return errors.length
      ? { ok: false, code: 'GRAPH_STRUCTURE_INVALID', diagnostics: errors }
      : { ok: true, value: { document: doc, selection }, diagnostics: validateTrace(doc) }
  },
}

function traceResultLayout(
  result: TraceResult,
  direction: TraceLayoutDirection,
): GraphView['layout'] {
  const vertical = direction === 'TopToBottom'
  const successors = new Map(result.nodes.map((n) => [n.key, new Set<string>()]))
  const incoming = new Map(result.nodes.map((n) => [n.key, 0]))
  const ranks = new Map(result.nodes.map((n) => [n.key, 0]))
  for (const edge of result.edges) {
    const next = successors.get(edge.from)
    // Context links participate only in visual placement; traversal semantics remain server-owned.
    if (!next || !incoming.has(edge.to) || next.has(edge.to)) continue
    next.add(edge.to)
    incoming.set(edge.to, incoming.get(edge.to)! + 1)
  }
  const queue = result.nodes.filter((n) => incoming.get(n.key) === 0).map((n) => n.key)
  for (let i = 0; i < queue.length; i++) {
    const key = queue[i]!
    for (const next of successors.get(key)!) {
      ranks.set(next, Math.max(ranks.get(next)!, ranks.get(key)! + 1))
      incoming.set(next, incoming.get(next)! - 1)
      if (incoming.get(next) === 0) queue.push(next)
    }
  }
  // Cycles are valid trace facts. Keep every unresolved node visible in a bounded fallback grid.
  const completed = new Set(queue)
  const fallbackRank = queue.length ? Math.max(...queue.map((key) => ranks.get(key)!)) + 1 : 0
  let cyclicIndex = 0
  const layers = new Map<number, string[]>()
  for (const node of result.nodes) {
    const rank = completed.has(node.key) ? ranks.get(node.key)! : fallbackRank + (cyclicIndex++ % 4)
    const layer = layers.get(rank) ?? []
    layer.push(node.key)
    layers.set(rank, layer)
  }
  const maxRows = Math.max(1, ...[...layers.values()].map((layer) => layer.length))
  const nodes = [...layers].flatMap(([rank, layer]) =>
    layer.map((id, row) => ({
      id,
      x: 80 + (vertical ? (row + (maxRows - layer.length) / 2) * 260 : rank * 300),
      y: 80 + (vertical ? rank * 200 : (row + (maxRows - layer.length) / 2) * 160),
      width: 180,
      height: 80,
    })),
  )
  const positions = new Map(nodes.map((node) => [node.id, node]))
  const bottom = Math.max(0, ...nodes.map((node) => node.y + node.height))
  const right = Math.max(0, ...nodes.map((node) => node.x + node.width))
  let bypass = 0
  const routedPairs = new Set<string>()
  const edges = result.edges.map((edge) => {
    const source = positions.get(edge.from),
      target = positions.get(edge.to)
    if (!source || !target) return { id: edge.key, vertices: [] }
    const pair = JSON.stringify([edge.from, edge.to])
    const repeated = routedPairs.has(pair)
    routedPairs.add(pair)
    const distance = vertical ? target.y - source.y : target.x - source.x
    if (!repeated && distance > 0 && distance <= (vertical ? 200 : 300))
      return { id: edge.key, vertices: [] }
    // Return paths, cycles and repeated facts need distinct lanes as well as skip-level edges.
    if (vertical) {
      const x = right + 50 + (bypass++ % 8) * 24
      return {
        id: edge.key,
        vertices: [
          { x: source.x + source.width / 2, y: source.y + source.height + 30 },
          { x, y: source.y + source.height + 30 },
          { x, y: target.y - 30 },
          { x: target.x + target.width / 2, y: target.y - 30 },
        ],
      }
    }
    // Skip-level facts use the space below the graph, so they cannot cross intermediate nodes.
    const y = bottom + 50 + (bypass++ % 8) * 24
    return {
      id: edge.key,
      vertices: [
        { x: source.x + source.width + 30, y: source.y + source.height / 2 },
        { x: source.x + source.width + 30, y },
        { x: target.x - 30, y },
        { x: target.x - 30, y: target.y + target.height / 2 },
      ],
    }
  })
  return { nodes, edges }
}

export function projectTraceResult(
  result: TraceResult,
  scope: GraphScope,
  direction: TraceLayoutDirection = 'LeftToRight',
): GraphView {
  return {
    graphContractVersion: 1,
    scope,
    kind: 'runtime',
    diagnostics: [],
    nodes: result.nodes.map((n) => ({
      id: n.key,
      definitionNodeId: n.objectTypeId,
      detailRef: n.key,
      kind: 'trace.object',
      label: n.summary.map((s) => String(s.value ?? '')).join(' · ') || n.identity.businessKey,
      ports:
        direction === 'TopToBottom'
          ? [
              { id: 'in', role: 'in', side: 'top', order: 0 },
              { id: 'out', role: 'out', side: 'bottom', order: 1 },
            ]
          : ports,
      tone: n.boundary ? 'warning' : 'neutral',
    })),
    edges: result.edges.map((e) => ({
      id: e.key,
      definitionEdgeId: e.relationId,
      detailRef: e.key,
      kind: `trace.${e.kind}`,
      label: String(e.evidence.recordKey ?? e.relationId),
      source: { nodeId: e.from, portId: 'out' },
      target: { nodeId: e.to, portId: 'in' },
      tone: e.kind === 'Context' ? 'info' : 'neutral',
    })),
    layout: traceResultLayout(result, direction),
  }
}
/** Merge by server identity; two records between identical endpoints stay distinct. */
export function mergeTraceResults(base: TraceResult | null, incoming: TraceResult): TraceResult {
  if (base && base.revisionNId !== incoming.revisionNId) throw new Error('TRACE_REVISION_MISMATCH')
  const reasons = new Set([...(base?.partialReasons ?? []), ...incoming.partialReasons])
  function comparable(value: unknown): string {
    return JSON.stringify(value, (key, item: unknown) => {
      if (key === 'nodeToken') return undefined
      if (item && typeof item === 'object' && !Array.isArray(item))
        return Object.fromEntries(Object.entries(item).sort(([a], [b]) => a.localeCompare(b)))
      return item
    })
  }
  function merge<T extends { key: string }>(old: T[], next: T[]): T[] {
    const map = new Map(old.map((v) => [v.key, v]))
    const seen = new Set<string>()
    for (const value of next) {
      if (!value.key || seen.has(value.key)) throw new Error('GRAPH_STRUCTURE_INVALID')
      seen.add(value.key)
      const prior = map.get(value.key)
      if (prior && comparable(prior) !== comparable(value)) {
        reasons.add('TRACE_MERGE_CONFLICT')
        continue
      }
      map.set(value.key, value)
    }
    return [...map.values()]
  }
  const nodes = merge(base?.nodes ?? [], incoming.nodes),
    edges = merge(base?.edges ?? [], incoming.edges),
    ids = new Set(nodes.map((n) => n.key))
  if (edges.some((e) => !ids.has(e.from) || !ids.has(e.to)))
    throw new Error('GRAPH_STRUCTURE_INVALID')
  return {
    ...incoming,
    nodes,
    edges,
    status:
      base?.status === 'Partial' || incoming.status === 'Partial' || reasons.size
        ? 'Partial'
        : 'Complete',
    partialReasons: [...reasons],
  }
}
