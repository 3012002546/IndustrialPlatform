import { describe, expect, it } from 'vitest'
import {
  cellId,
  cellTarget,
  projectionCells,
  validateProjection,
} from '@/components/studio/graph/projection'
import type { GraphView } from '@/components/studio/graph/types'

function view(): GraphView {
  return {
    graphContractVersion: 1,
    scope: {
      contextKey: 'fixture',
      definition: { kind: 'draft', resourceNId: 'r', documentStamp: '1' },
    },
    kind: 'definition',
    diagnostics: [],
    nodes: ['a', 'b', 'c'].map((id) => ({
      id,
      kind: 'test',
      label: id,
      definitionNodeId: id,
      ports: [
        { id: 'in', role: 'in', side: 'left', order: 0 },
        { id: 'out', role: 'out', side: 'right', order: 0 },
      ],
    })),
    edges: [
      ['one', 'a', 'c'],
      ['two', 'a', 'c'],
      ['three', 'b', 'c'],
      ['loop', 'c', 'a'],
    ].map(([id, source, target]) => ({
      id: id!,
      kind: 'relation',
      label: id!,
      source: { nodeId: source!, portId: 'out' },
      target: { nodeId: target!, portId: 'in' },
    })),
    layout: {
      nodes: ['a', 'b', 'c'].map((id, index) => ({
        id,
        x: index * 220,
        y: index * 100,
        width: 180,
        height: 80,
      })),
      edges: ['one', 'two', 'three', 'loop'].map((id) => ({ id, vertices: [] })),
    },
  }
}
describe('public graph projection', () => {
  it('preserves multi-parent, parallel evidence edges and a cycle without importing a domain', () => {
    const graph = view(),
      cells = projectionCells(graph, true)
    expect(cells).toHaveLength(7)
    expect(new Set(cells.map((c) => c.id)).size).toBe(7)
    expect(cells.find((c) => c.id === cellId('edge', 'one'))).not.toEqual(
      cells.find((c) => c.id === cellId('edge', 'two')),
    )
    expect(graph.layout.edges.every((e) => e.vertices.length === 0)).toBe(true)
  })
  it('keeps cell identity reversible even when IDs contain delimiters or overlap node/edge IDs', () => {
    for (const id of ['a/b', '["node", "a"]', 'a:edge', '工厂1/批次']) {
      expect(cellTarget(cellId('node', id))).toEqual({ kind: 'node', id })
      expect(cellTarget(cellId('edge', id))).toEqual({ kind: 'edge', id })
      expect(cellId('node', id)).not.toBe(cellId('edge', id))
    }
    expect(cellTarget('temporary-x6-edge')).toBeUndefined()
  })
  it('rejects duplicate identities rather than quietly replacing a cell', () => {
    const graph = view()
    graph.nodes = [...graph.nodes, graph.nodes[0]!]
    expect(() => validateProjection(graph)).toThrow('GRAPH_STRUCTURE_INVALID')
  })
  it('allows X6 selection boxes to be found and removed using its attribute selector', () => {
    const container = document.createElement('div')
    for (const kind of ['node', 'edge'] as const)
      for (const domainId of ['lot', '["node", "a"]', '工厂/批次%1', 'a"b\\c\n']) {
        const id = cellId(kind, domainId)
        const box = document.createElement('div')
        box.setAttribute('data-cell-id', id)
        container.append(box)
        // X6 SelectionImpl.destroySelectionBox interpolates the cell ID without escaping.
        const selected = container.querySelector(`[data-cell-id="${id}"]`)
        expect(selected).toBe(box)
        selected!.remove()
        expect(cellTarget(id)).toEqual({ kind, id: domainId })
      }
    expect(container.childElementCount).toBe(0)
  })
  it('rejects dangling ports and invalid numeric geometry', () => {
    const graph = view()
    graph.edges = [{ ...graph.edges[0]!, target: { nodeId: 'c', portId: 'missing' } }]
    graph.layout.edges = [graph.layout.edges[0]!]
    expect(() => validateProjection(graph)).toThrow()
    const other = view()
    other.layout.nodes = [{ ...other.layout.nodes[0]!, x: NaN }, ...other.layout.nodes.slice(1)]
    expect(() => validateProjection(other)).toThrow()
  })
  it('accepts disconnected draft relations but refuses suspended runtime facts', () => {
    const graph = view()
    graph.edges = graph.edges.map((e, i) => (i ? e : { ...e, target: null }))
    expect(() => validateProjection(graph)).not.toThrow()
    graph.kind = 'runtime'
    expect(() => validateProjection(graph)).toThrow()
  })
  it('handles a self-loop without treating it as a dependency cycle', () => {
    const graph = view()
    graph.edges = graph.edges.map((e, i) =>
      i ? e : { ...e, target: { nodeId: 'a', portId: 'in' } },
    )
    const edge = projectionCells(graph, true).find((c) => c.id === cellId('edge', 'one'))
    expect(edge).toMatchObject({
      source: { cell: cellId('node', 'a') },
      target: { cell: cellId('node', 'a') },
    })
  })
  it('renders arbitrary labels as text attributes and disables output magnets in readonly mode', () => {
    const graph = view()
    graph.nodes = graph.nodes.map((n) => ({ ...n, label: '<img onerror="alert(1)">' }))
    const node = projectionCells(graph, false)[0]!
    expect(node.attrs?.label?.text).toBe('<img onerror="alert(1)">')
    expect(JSON.stringify(node)).toContain('passive')
  })
  it('validates the full 1000-node 3000-edge graph without silently dropping cells', () => {
    const graph = view()
    graph.nodes = Array.from({ length: 1000 }, (_, i) => ({ ...graph.nodes[0]!, id: String(i) }))
    graph.edges = Array.from({ length: 3000 }, (_, i) => ({
      id: 'e' + i,
      kind: 'relation',
      label: '',
      source: { nodeId: String(i % 1000), portId: 'out' },
      target: { nodeId: String((i + 1) % 1000), portId: 'in' },
    }))
    graph.layout = {
      nodes: graph.nodes.map((n, i) => ({
        id: n.id,
        x: (i % 40) * 220,
        y: Math.floor(i / 40) * 120,
        width: 180,
        height: 80,
      })),
      edges: graph.edges.map((e) => ({ id: e.id, vertices: [] })),
    }
    expect(projectionCells(graph, false)).toHaveLength(4000)
  })
})
