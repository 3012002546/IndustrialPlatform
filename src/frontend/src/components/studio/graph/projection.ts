import type { EdgeMetadata, NodeMetadata } from '@antv/x6'
import type { GraphView, Target, Endpoint, Point } from './types'

// X6 interpolates cell IDs into quoted attribute selectors when removing selection boxes.
// Encode only this disposable view identity; domain IDs and saved documents stay unchanged.
export const cellId = (kind: 'node' | 'edge', id: string) =>
  encodeURIComponent(JSON.stringify([kind, id]))
export function cellTarget(id: string): Target | undefined {
  try {
    const value: unknown = JSON.parse(decodeURIComponent(id))
    if (
      Array.isArray(value) &&
      value.length === 2 &&
      (value[0] === 'node' || value[0] === 'edge') &&
      typeof value[1] === 'string'
    )
      return { kind: value[0], id: value[1] }
  } catch {
    /* Transient X6 edges do not yet have a domain identity. */
  }
}
const pointValid = (p: Point) =>
  Number.isFinite(p.x) && Number.isFinite(p.y) && Math.abs(p.x) <= 1e6 && Math.abs(p.y) <= 1e6
export function validateProjection(view: GraphView): void {
  if (view.graphContractVersion !== 1) throw new Error('GRAPH_VERSION_UNSUPPORTED')
  const nodes = new Map(view.nodes.map((n) => [n.id, n]))
  const edges = new Set(view.edges.map((e) => e.id))
  const boxes = new Map(view.layout.nodes.map((n) => [n.id, n]))
  const paths = new Map(view.layout.edges.map((e) => [e.id, e]))
  const invalid = () => {
    throw new Error('GRAPH_STRUCTURE_INVALID')
  }
  if (
    nodes.size !== view.nodes.length ||
    edges.size !== view.edges.length ||
    boxes.size !== view.layout.nodes.length ||
    paths.size !== view.layout.edges.length ||
    boxes.size !== nodes.size ||
    paths.size !== edges.size
  )
    invalid()
  for (const n of view.nodes) {
    if (!n.id || new Set(n.ports.map((p) => p.id)).size !== n.ports.length) invalid()
    const box = boxes.get(n.id)
    if (
      !box ||
      !pointValid(box) ||
      !Number.isFinite(box.width) ||
      !Number.isFinite(box.height) ||
      box.width < 1 ||
      box.height < 1 ||
      box.width > 1e4 ||
      box.height > 1e4
    )
      invalid()
  }
  const validEndpoint = (e: Endpoint | null) =>
    e === null
      ? view.kind === 'definition' && view.scope.definition.kind === 'draft'
      : !!nodes.get(e.nodeId)?.ports.some((p) => p.id === e.portId)
  for (const edge of view.edges) {
    const line = paths.get(edge.id)
    if (
      !edge.id ||
      !validEndpoint(edge.source) ||
      !validEndpoint(edge.target) ||
      !line ||
      !line.vertices.every(pointValid)
    )
      invalid()
  }
}
const tones = {
  neutral: '#64748b',
  info: '#409eff',
  success: '#67c23a',
  warning: '#e6a23c',
  danger: '#f56c6c',
}

/** X6 metadata is disposable presentation, never the serialized domain definition. */
export function projectionCells(
  view: GraphView,
  editable: boolean,
): (NodeMetadata | EdgeMetadata)[] {
  validateProjection(view)
  const boxes = new Map(view.layout.nodes.map((n) => [n.id, n]))
  const paths = new Map(view.layout.edges.map((e) => [e.id, e.vertices]))
  const groups = new Map<string, string[]>()
  for (const edge of view.edges) {
    if (!edge.source || !edge.target) continue
    const key = JSON.stringify([edge.source.nodeId, edge.target.nodeId].sort())
    groups.set(key, [...(groups.get(key) ?? []), edge.id])
  }
  const nodes: NodeMetadata[] = view.nodes.map((n) => ({
    id: cellId('node', n.id),
    shape: 'rect',
    x: boxes.get(n.id)!.x,
    y: boxes.get(n.id)!.y,
    width: boxes.get(n.id)!.width,
    height: boxes.get(n.id)!.height,
    attrs: {
      body: {
        rx: 8,
        ry: 8,
        stroke: tones[n.tone ?? 'neutral'],
        strokeWidth: 2,
        fill: 'var(--el-bg-color, #fff)',
      },
      label: {
        text: n.label,
        fill: 'var(--el-text-color-primary, #303133)',
        fontSize: 13,
        textWrap: { width: -18, height: -14, ellipsis: true },
      },
    },
    ports: {
      groups: Object.fromEntries(
        ['left', 'right', 'top', 'bottom'].map((side) => [
          side,
          {
            position: side,
            attrs: { circle: { r: 5, stroke: '#64748b', fill: 'var(--el-bg-color, #fff)' } },
          },
        ]),
      ),
      items: [...n.ports]
        .sort((a, b) => a.order - b.order || a.id.localeCompare(b.id))
        .map((p) => ({
          id: p.id,
          group: p.side,
          attrs: { circle: { magnet: editable && p.role !== 'in' ? true : 'passive' } },
        })),
    },
  }))
  const edges: EdgeMetadata[] = view.edges.map((e) => {
    let vertices = paths.get(e.id) ?? []
    const a = e.source && boxes.get(e.source.nodeId),
      b = e.target && boxes.get(e.target.nodeId)
    if (!vertices.length && a && b) {
      if (e.source!.nodeId === e.target!.nodeId)
        vertices = [
          { x: a.x + a.width + 50, y: a.y - 50 },
          { x: a.x - 50, y: a.y - 50 },
        ]
      else {
        const peers = groups.get(JSON.stringify([e.source!.nodeId, e.target!.nodeId].sort())) ?? []
        if (peers.length > 1) {
          const offset = (peers.indexOf(e.id) - (peers.length - 1) / 2) * 44
          vertices = [
            {
              x: (a.x + a.width / 2 + b.x + b.width / 2) / 2,
              y: (a.y + a.height / 2 + b.y + b.height / 2) / 2 + offset,
            },
          ]
        }
      }
    }
    const terminal = (ep: Endpoint | null, fallback: Point) =>
      ep ? { cell: cellId('node', ep.nodeId), port: ep.portId } : fallback
    return {
      id: cellId('edge', e.id),
      shape: 'edge',
      zIndex: 0,
      source: terminal(e.source, { x: b ? b.x - 100 : 40, y: b ? b.y + 36 : 40 }),
      target: terminal(e.target, { x: a ? a.x + a.width + 100 : 240, y: a ? a.y + 36 : 40 }),
      vertices: vertices.map((p) => ({ ...p })),
      connector: { name: 'rounded' },
      attrs: {
        line: {
          stroke: tones[e.tone ?? 'neutral'],
          strokeWidth: 2,
          targetMarker: 'classic',
          strokeDasharray: e.source && e.target ? '' : '6 4',
        },
      },
      labels: e.label
        ? [
            {
              attrs: {
                label: {
                  text: e.label,
                  fill: 'var(--el-text-color-primary, #303133)',
                  fontSize: 12,
                },
                body: { fill: 'var(--el-bg-color, #fff)' },
              },
            },
          ]
        : [],
    }
  })
  return [...nodes, ...edges]
}
