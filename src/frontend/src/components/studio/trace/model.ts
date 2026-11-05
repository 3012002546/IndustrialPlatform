import type { Binding } from '../shared/model'

export type { Binding }
export type TraceDirection = 'Upstream' | 'Downstream' | 'Both'
export type TraceLayoutDirection = 'LeftToRight' | 'TopToBottom'
export interface TraceMapping {
  target: string
  source: Binding
}
export interface TraceIdentityBindings {
  sourceSystem: Binding
  factory: Binding
  businessKey: Binding
}
export interface TraceIdentity {
  sourceSystem: string
  factory: string
  businessKey: string
}
export interface TraceParameter {
  name: string
  label?: string
  type: string
  required: boolean
  nullable?: boolean
  source?: string
  contextKey?: string
  itemType?: string
  defaultValue?: unknown
}
export interface TraceSearchField {
  parameterName: string
  label: string
  control: 'Text' | 'Select' | 'DateRange' | 'BusinessSelector'
  required: boolean
  optionsDatasetRevisionNId?: string
  valueFieldId?: string
  labelFieldId?: string
}
export interface TraceColumn {
  fieldId: string
  label: string
  width: number
  visible: boolean
}
export interface TraceStart {
  objectTypeIds: string[]
  datasetRevisionNId: string
  collectionPath: string[]
  parameterMap: TraceMapping[]
  searchFields: TraceSearchField[]
  columns: TraceColumn[]
  rowKey: Binding
  objectType: Binding
  identityMap: TraceIdentityBindings
}
export interface TraceField {
  label: string
  value: Binding
  format?: string
}
export interface TraceDetailSection {
  id: string
  label: string
  kind: 'Fields' | 'Table'
  datasetRevisionNId: string
  parameterMap: TraceMapping[]
  collectionPath: string[]
  fields: TraceField[]
  columns: TraceColumn[]
  rowKey?: Binding
  pageSize: number
}
export interface TraceLink {
  id: string
  label: string
  kind: 'File' | 'External'
  value: Binding
  parameterMap?: TraceMapping[]
  openMode: 'Modal'
  allowedOriginKey?: string
  fileSource?: { mode: 'Owned' | 'Mes'; sourceKey?: string; version?: Binding }
}
export interface TraceObject {
  id: string
  name: string
  identity: TraceIdentityBindings & {
    grain: 'Batch' | 'Package' | 'Serial' | 'WorkOrder' | 'Operation' | 'Custom'
  }
  summary: TraceField[]
  detail: {
    datasetRevisionNId: string
    parameterMap: TraceMapping[]
    fields: TraceField[]
    links: TraceLink[]
    sections?: TraceDetailSection[]
  }
  style: { color: string; iconKey: string; width: number; height: number }
}
export interface TraceTraversal {
  datasetRevisionNId: string
  inputMap: TraceMapping[]
  batchParameterName?: string
  result: {
    collectionPath: string[]
    targetObjectTypeId: string
    targetIdentity: TraceIdentityBindings
    summaryMap: TraceField[]
  }
}
export interface TraceRelation {
  id: string
  name: string
  fromTypeId: string
  toTypeId: string
  kind: 'Genealogy' | 'Context'
  semantic: 'Consume' | 'Produce' | 'Split' | 'Merge' | 'Ship' | 'Use' | 'Inspect' | 'Custom'
  forward: TraceTraversal
  reverse: TraceTraversal
  identity: Binding
  evidence: { recordKey: Binding; occurredOn: Binding; quantity?: Binding; unit?: Binding }
  label: Binding
}
export interface TraceDocument {
  schemaVersion: 1
  presentation?: { layoutDirection: TraceLayoutDirection }
  parameters: TraceParameter[]
  scope: { factoryIds: string[]; industryTags: string[] }
  start: TraceStart
  objects: TraceObject[]
  relations: TraceRelation[]
  layout: {
    nodes: { objectTypeId: string; x: number; y: number; width: number; height: number }[]
    edges: {
      relationId: string
      sourcePort: string | null
      targetPort: string | null
      vertices: { x: number; y: number }[]
    }[]
  }
  limits: { maxDepth: number; maxNodes: number; maxEdges: number; timeoutSeconds: number }
  [key: string]: unknown
}
export const rowBinding = (field = ''): Binding => ({
  from: 'Row',
  path: field ? [field] : [],
  nullPolicy: 'Error',
})
export const identityBindings = (): TraceIdentityBindings => ({
  sourceSystem: rowBinding('sourceSystem'),
  factory: rowBinding('factory'),
  businessKey: rowBinding('businessKey'),
})
export const traceContexts = ['Row', 'Node', 'PageParam', 'Context', 'Constant']
export function newTraceObject(id: string = crypto.randomUUID(), name = '对象类型'): TraceObject {
  return {
    id,
    name,
    identity: { ...identityBindings(), grain: 'Batch' },
    summary: [],
    detail: { datasetRevisionNId: '', parameterMap: [], fields: [], links: [] },
    style: { color: '', iconKey: 'box', width: 180, height: 80 },
  }
}
export function newTraversal(targetObjectTypeId: string): TraceTraversal {
  return {
    datasetRevisionNId: '',
    inputMap: [],
    result: {
      collectionPath: [],
      targetObjectTypeId,
      targetIdentity: identityBindings(),
      summaryMap: [],
    },
  }
}
export function newTraceRelation(
  from: string,
  to: string,
  id: string = crypto.randomUUID(),
): TraceRelation {
  return {
    id,
    name: '',
    fromTypeId: from,
    toTypeId: to,
    kind: 'Genealogy',
    semantic: 'Consume',
    forward: newTraversal(to),
    reverse: newTraversal(from),
    identity: rowBinding('recordKey'),
    evidence: { recordKey: rowBinding('recordKey'), occurredOn: rowBinding('occurredOn') },
    label: rowBinding('recordKey'),
  }
}
export function initialTraceDocument(): TraceDocument {
  return {
    schemaVersion: 1,
    parameters: [],
    scope: { factoryIds: [], industryTags: [] },
    start: {
      objectTypeIds: [],
      datasetRevisionNId: '',
      collectionPath: [],
      parameterMap: [],
      searchFields: [],
      columns: [],
      rowKey: rowBinding('id'),
      objectType: { from: 'Constant', value: '', nullPolicy: 'Error' },
      identityMap: identityBindings(),
    },
    objects: [],
    relations: [],
    layout: { nodes: [], edges: [] },
    limits: { maxDepth: 12, maxNodes: 1000, maxEdges: 3000, timeoutSeconds: 30 },
  }
}
/** Unknown schema is never silently migrated into an editable document. */
export function readTraceDocument(value: unknown): TraceDocument {
  if (!value || typeof value !== 'object') throw new Error('TRACE_DOCUMENT_INVALID')
  const doc = value as TraceDocument
  if (doc.schemaVersion !== 1) throw new Error('GRAPH_VERSION_UNSUPPORTED')
  if (
    !Array.isArray(doc.objects) ||
    !Array.isArray(doc.relations) ||
    !Array.isArray(doc.layout?.nodes) ||
    !Array.isArray(doc.layout?.edges) ||
    !doc.start ||
    !doc.limits
  )
    throw new Error('GRAPH_STRUCTURE_INVALID')
  return JSON.parse(JSON.stringify(doc)) as TraceDocument
}
