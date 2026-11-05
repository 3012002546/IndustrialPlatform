import type { TraceRelationPreview, TraceResult, TraceStartRow } from '@/api/studioTrace'
import type { TraceDirection, TraceDocument } from './model'

/** Preview responses contain neighbours and facts, not a complete runtime graph. */
export function tracePreviewResult(
  value: TraceRelationPreview,
  sample: TraceStartRow,
  doc: TraceDocument,
  relationId: string,
  direction: TraceDirection,
): TraceResult {
  const relation = doc.relations.find((item) => item.id === relationId)!
  const anchorKey =
    (direction === 'Upstream' ? value.edges[0]?.to : value.edges[0]?.from) ?? 'preview-start'
  const nodes = new Map(value.nodes.map((node) => [node.key, { ...node, nodeToken: '' }]))
  nodes.set(anchorKey, {
    key: anchorKey,
    objectTypeId: sample.objectTypeId,
    identity: sample.identity,
    summary: [],
    grain: doc.objects.find((item) => item.id === sample.objectTypeId)?.identity.grain ?? 'Batch',
    nodeToken: '',
  })
  return {
    requestId: 'preview',
    revisionNId: 'preview',
    nodes: [...nodes.values()],
    edges: value.edges.map((edge, index) => ({
      ...edge,
      key: `preview-edge-${index}`,
      kind: relation.kind,
    })),
    status: value.diagnostics.length ? 'Partial' : 'Complete',
    partialReasons: [],
    observedOn: null,
    sourceObservations: [],
  }
}
