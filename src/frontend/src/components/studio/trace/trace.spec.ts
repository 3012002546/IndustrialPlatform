import { describe, expect, it } from 'vitest'
import { traceAdapter, projectTrace, mergeTraceResults, projectTraceResult } from './adapter'
import { initialTraceDocument, newTraceObject, newTraceRelation, readTraceDocument } from './model'
import { createHistory } from '../shared/model'
import type { TraceResult } from '@/api/studioTrace'
import type { GraphScope } from '../graph/types'

const scope: GraphScope = {
  contextKey: 'page',
  definition: { kind: 'draft', resourceNId: 'model', documentStamp: '1' },
}
function definition() {
  const doc = initialTraceDocument()
  doc.objects = [newTraceObject('lot')]
  doc.objects[0]!.name = 'Lot'
  doc.objects[0]!.detail.datasetRevisionNId = 'detail-r1'
  doc.start.objectTypeIds = ['lot']
  doc.start.datasetRevisionNId = 'start-r1'
  const r = newTraceRelation('lot', 'lot', 'consume')
  r.forward.datasetRevisionNId = 'forward-r1'
  r.reverse.datasetRevisionNId = 'reverse-r1'
  doc.relations = [r]
  doc.layout.edges = [{ relationId: 'consume', sourcePort: 'out', targetPort: 'in', vertices: [] }]
  return doc
}
export function resultFixture(): TraceResult {
  return {
    requestId: 'q1',
    revisionNId: 'r1',
    nodes: ['A', 'B', 'C', 'A2'].map((key) => ({
      key,
      objectTypeId: 'lot',
      nodeToken: `token-${key}`,
      grain: 'Batch',
      identity: {
        sourceSystem: 'MES',
        factory: key === 'A2' ? 'F2' : 'F1',
        businessKey: key === 'A2' ? 'A' : key,
      },
      summary: [{ label: 'code', value: key === 'A2' ? 'A' : key }],
    })),
    edges: [
      ['M1', 'A', 'C'],
      ['M2', 'A', 'C'],
      ['M3', 'B', 'C'],
      ['M4', 'C', 'A'],
    ].map(([key, from, to]) => ({
      key: key!,
      relationId: 'consume',
      from: from!,
      to: to!,
      kind: 'Genealogy',
      evidence: { recordKey: key, occurredOn: '2026-10-31T00:00:00Z', quantity: '2.0', unit: 'kg' },
    })),
    status: 'Complete',
    partialReasons: [],
    observedOn: null,
    sourceObservations: [],
  }
}
describe('TraceModel domain adapter', () => {
  it('assigns readable non-colliding object names while retaining stable IDs', () => {
    const doc = definition()
    doc.objects[0]!.name = '对象类型 2'
    const added = traceAdapter.edit(
      doc,
      { type: 'addNode', catalogKey: 'trace.object', at: { x: 10, y: 20 } },
      { scope, expectedDocumentStamp: '1' },
    )
    expect(added.ok).toBe(true)
    if (!added.ok) return
    expect(added.value.document.objects[1]!.name).toBe('对象类型 3')
    expect(added.value.document.objects[1]!.id).not.toBe('lot')
    const old = newTraceObject('old')
    old.name = ''
    doc.objects = [old]
    expect(projectTrace(doc, scope).nodes[0]!.label).toBe('对象类型 1')
    expect(doc.objects[0]!.name).toBe('')
  })
  it('projects deterministic missing layout without modifying the document', () => {
    const doc = definition(),
      before = JSON.stringify(doc),
      graph = projectTrace(doc, scope)
    expect(graph.layout.nodes[0]).toEqual({ id: 'lot', x: 80, y: 80, width: 180, height: 80 })
    expect(JSON.stringify(doc)).toBe(before)
  })
  it('preserves unknown same-schema fields and all mappings in a layout edit', () => {
    const doc = definition()
    doc.extension = { custom: true }
    const changed = traceAdapter.edit(
      doc,
      { type: 'layout', nodes: [{ id: 'lot', x: 123, y: 45, width: 200, height: 90 }], edges: [] },
      { scope, expectedDocumentStamp: '1' },
    )
    expect(changed.ok).toBe(true)
    if (!changed.ok) return
    expect(changed.value.document.extension).toEqual(doc.extension)
    expect(changed.value.document.relations).toEqual(doc.relations)
    expect(doc.layout.nodes).toEqual([])
  })
  it('disconnects without deleting mappings and restores in one undo', () => {
    const doc = definition(),
      history = createHistory(doc)
    const changed = traceAdapter.edit(
      doc,
      {
        type: 'connect',
        connection: {
          edgeId: 'consume',
          source: { nodeId: 'lot', portId: 'out' },
          target: null,
          edgeKind: 'trace.Genealogy',
        },
      },
      { scope, expectedDocumentStamp: '1' },
    )
    expect(changed.ok).toBe(true)
    if (!changed.ok) return
    expect(changed.value.document.relations).toEqual(doc.relations)
    expect(changed.diagnostics.some((d) => d.path === 'layout.edges[0].targetPort')).toBe(true)
    history.commit(changed.value.document)
    expect(history.undo()).toEqual(doc)
  })
  it('cascades object deletion through self-loops, layout and start type references', () => {
    const doc = definition()
    const changed = traceAdapter.edit(
      doc,
      { type: 'remove', targets: [{ kind: 'node', id: 'lot' }] },
      { scope, expectedDocumentStamp: '1' },
    )
    expect(changed.ok).toBe(true)
    if (!changed.ok) return
    expect(changed.value.document.objects).toEqual([])
    expect(changed.value.document.relations).toEqual([])
    expect(changed.value.document.layout.edges).toEqual([])
    expect(changed.value.document.start.objectTypeIds).toEqual([])
  })
  it('allows a self-loop and multiple relations, but rejects reversed ports', () => {
    const doc = definition()
    expect(
      traceAdapter.checkConnection(doc, {
        source: { nodeId: 'lot', portId: 'out' },
        target: { nodeId: 'lot', portId: 'in' },
        edgeKind: 'trace.Genealogy',
      }),
    ).toEqual([])
    expect(
      traceAdapter.checkConnection(doc, {
        source: { nodeId: 'lot', portId: 'in' },
        target: { nodeId: 'lot', portId: 'out' },
        edgeKind: 'trace.Genealogy',
      }),
    ).toHaveLength(1)
  })
  it('reconnection keeps stable relation identity and requires output mapping repair', () => {
    const doc = definition()
    doc.objects.push(newTraceObject('serial'))
    const changed = traceAdapter.edit(
      doc,
      {
        type: 'connect',
        connection: {
          edgeId: 'consume',
          source: { nodeId: 'lot', portId: 'out' },
          target: { nodeId: 'serial', portId: 'in' },
          edgeKind: 'trace.Genealogy',
        },
      },
      { scope, expectedDocumentStamp: '1' },
    )
    expect(changed.ok).toBe(true)
    if (!changed.ok) return
    expect(changed.value.document.relations[0]!.id).toBe('consume')
    expect(changed.value.document.relations[0]!.forward.result.targetObjectTypeId).toBe('lot')
    expect(
      changed.diagnostics.some((d) => d.path === 'relations[0].forward.result.targetObjectTypeId'),
    ).toBe(true)
  })
  it('rejects invalid geometry without changing the original and rejects stale/readonly edits', () => {
    const doc = definition()
    expect(
      traceAdapter.edit(
        doc,
        {
          type: 'layout',
          nodes: [{ id: 'lot', x: Infinity, y: 0, width: 10, height: 10 }],
          edges: [],
        },
        { scope, expectedDocumentStamp: '1' },
      ).ok,
    ).toBe(false)
    expect(doc.layout.nodes).toEqual([])
    expect(
      traceAdapter.edit(doc, { type: 'remove', targets: [] }, { scope, expectedDocumentStamp: '0' })
        .ok,
    ).toBe(false)
    expect(
      traceAdapter.edit(
        doc,
        { type: 'remove', targets: [] },
        {
          scope: {
            contextKey: 'page',
            definition: { kind: 'published', resourceNId: 'model', revisionNId: 'r1' },
          },
          expectedDocumentStamp: '1',
        },
      ).ok,
    ).toBe(false)
  })
  it('does not downgrade unknown document schemas', () => {
    expect(() => readTraceDocument({ ...definition(), schemaVersion: 2 })).toThrow(
      'GRAPH_VERSION_UNSUPPORTED',
    )
  })
})
describe('trace runtime graph identity and partial merge', () => {
  it('separates repeated evidence and return paths in both layout directions', () => {
    const result = resultFixture()
    result.edges = [
      { ...result.edges[0]!, from: 'A', to: 'B' },
      { ...result.edges[1]!, from: 'A', to: 'B' },
      { ...result.edges[2]!, from: 'B', to: 'A' },
    ]
    for (const direction of ['LeftToRight', 'TopToBottom'] as const) {
      const graph = projectTraceResult(result, scope, direction)
      const paths = graph.layout.edges.map((edge) => edge.vertices)
      expect(paths[0]).toEqual([])
      expect(paths[1]).toHaveLength(4)
      expect(paths[2]).toHaveLength(4)
      expect(paths[1]).not.toEqual(paths[2])
      expect(graph.edges.map((edge) => [edge.source?.nodeId, edge.target?.nodeId])).toEqual([
        ['A', 'B'],
        ['A', 'B'],
        ['B', 'A'],
      ])
    }
  })
  it('lays out vertical branches without overlap and keeps evidence, cycles and port semantics', () => {
    const result = resultFixture()
    const before = JSON.stringify(result)
    for (const graph of [
      projectTraceResult(result, scope),
      projectTraceResult(result, scope, 'TopToBottom'),
    ]) {
      expect(graph.edges.map((edge) => edge.id)).toEqual(result.edges.map((edge) => edge.key))
      for (const [index, a] of graph.layout.nodes.entries())
        for (const b of graph.layout.nodes.slice(index + 1))
          expect(
            a.x + a.width <= b.x ||
              b.x + b.width <= a.x ||
              a.y + a.height <= b.y ||
              b.y + b.height <= a.y,
          ).toBe(true)
    }
    result.edges = [
      { ...result.edges[0]!, from: 'A', to: 'B' },
      { ...result.edges[1]!, from: 'B', to: 'C' },
      { ...result.edges[2]!, from: 'A', to: 'C' },
      { ...result.edges[3]!, from: 'C', to: 'A2', kind: 'Context' },
    ]
    const graph = projectTraceResult(result, scope, 'TopToBottom')
    const positions = new Map(graph.layout.nodes.map((node) => [node.id, node]))
    expect(positions.get('A2')!.y).toBeGreaterThan(positions.get('C')!.y)
    expect(
      graph.nodes.every(
        (node) => node.ports[0]!.side === 'top' && node.ports[1]!.side === 'bottom',
      ),
    ).toBe(true)
    expect(graph.layout.edges.find((edge) => edge.id === 'M3')!.vertices[1]!.x).toBeGreaterThan(
      Math.max(...graph.layout.nodes.map((node) => node.x + node.width)),
    )
    expect(JSON.parse(before).nodes).toEqual(result.nodes)
  })
  it('places context records after their source and routes skip-level facts outside nodes', () => {
    const result = resultFixture()
    result.edges = [
      { ...result.edges[0]!, from: 'A', to: 'B' },
      { ...result.edges[1]!, from: 'B', to: 'C' },
      { ...result.edges[2]!, from: 'A', to: 'C' },
      { ...result.edges[3]!, from: 'C', to: 'A2', kind: 'Context' },
    ]
    const graph = projectTraceResult(result, scope)
    const positions = new Map(graph.layout.nodes.map((node) => [node.id, node]))
    expect(positions.get('A2')!.x).toBeGreaterThan(positions.get('C')!.x)
    const vertices = graph.layout.edges.find((edge) => edge.id === 'M3')!.vertices
    expect(vertices).toHaveLength(4)
    expect(vertices[1]!.y).toBeGreaterThan(
      Math.max(...graph.layout.nodes.map((node) => node.y + node.height)),
    )
    expect(graph.edges.find((edge) => edge.id === 'M4')!.kind).toBe('trace.Context')
  })
  it('lays out material flow from parents through the merge to outputs regardless of response order', () => {
    const result = resultFixture()
    result.edges = [
      { ...result.edges[0]!, from: 'B', to: 'A' },
      { ...result.edges[1]!, from: 'C', to: 'A' },
      { ...result.edges[2]!, from: 'A', to: 'A2' },
    ]
    const before = JSON.stringify(result)
    const positions = new Map(projectTraceResult(result, scope).layout.nodes.map((n) => [n.id, n]))
    expect(positions.get('B')!.x).toBeLessThan(positions.get('A')!.x)
    expect(positions.get('C')!.x).toBeLessThan(positions.get('A')!.x)
    expect(positions.get('A')!.x).toBeLessThan(positions.get('A2')!.x)
    expect(positions.get('B')!.y).not.toBe(positions.get('C')!.y)
    expect(JSON.stringify(result)).toBe(before)
  })
  it('retains both same-endpoint evidence records, multiple parents, cycles and cross-factory identities', () => {
    const result = mergeTraceResults(null, resultFixture())
    const graph = projectTraceResult(result, scope)
    expect(graph.nodes).toHaveLength(4)
    expect(graph.edges).toHaveLength(4)
    expect(
      graph.edges.filter((e) => e.source?.nodeId === 'A' && e.target?.nodeId === 'C'),
    ).toHaveLength(2)
    expect(JSON.stringify(graph)).not.toContain('token-')
    expect(graph.edges.find((e) => e.id === 'M4')?.target?.nodeId).toBe('A')
    expect(graph.layout.nodes.every((n) => Number.isFinite(n.x) && Number.isFinite(n.y))).toBe(true)
    expect(new Set(graph.layout.nodes.map((n) => `${n.x},${n.y}`)).size).toBe(4)
  })
  it('deduplicates repeated expand and accepts refreshed opaque node tokens', () => {
    const base = resultFixture(),
      next = resultFixture()
    next.nodes[0]!.nodeToken = 'new-token'
    const merged = mergeTraceResults(base, next)
    expect(merged.nodes).toHaveLength(4)
    expect(merged.edges).toHaveLength(4)
    expect(merged.status).toBe('Complete')
    expect(merged.nodes[0]!.nodeToken).toBe('new-token')
  })
  it('preserves original evidence and reports conflicting values as Partial', () => {
    const base = resultFixture(),
      next = resultFixture()
    next.edges[0]!.evidence.quantity = '999'
    const merged = mergeTraceResults(base, next)
    expect(merged.edges[0]!.evidence.quantity).toBe('2.0')
    expect(merged.status).toBe('Partial')
    expect(merged.partialReasons).toContain('TRACE_MERGE_CONFLICT')
  })
  it('retains earlier unresolved Partial reasons after a successful expansion', () => {
    const base = resultFixture()
    base.status = 'Partial'
    base.partialReasons = ['SOURCE_TIMEOUT']
    expect(mergeTraceResults(base, resultFixture()).partialReasons).toContain('SOURCE_TIMEOUT')
  })
  it('rejects dangling edges, duplicate incoming keys and mismatched revisions atomically', () => {
    const bad = resultFixture()
    bad.edges[0]!.to = 'missing'
    expect(() => mergeTraceResults(null, bad)).toThrow('GRAPH_STRUCTURE_INVALID')
    const duplicate = resultFixture()
    duplicate.nodes.push(duplicate.nodes[0]!)
    expect(() => mergeTraceResults(null, duplicate)).toThrow()
    expect(() =>
      mergeTraceResults(resultFixture(), { ...resultFixture(), revisionNId: 'r2' }),
    ).toThrow('TRACE_REVISION_MISMATCH')
  })
})
