/** Display contracts only. Domain documents, credentials and execution stay with the consumer. */
export type Id = string
export type Seq = string
export type Mode = 'design' | 'preview' | 'run'
export type Target =
  { kind: 'node'; id: Id } | { kind: 'edge'; id: Id } | { kind: 'port'; nodeId: Id; id: Id }
export type DefinitionRef =
  | { kind: 'draft'; resourceNId: Id; documentStamp: Seq }
  | { kind: 'published'; resourceNId: Id; revisionNId: Id }
export interface GraphScope {
  contextKey: Id
  definition: DefinitionRef
}
export interface Point {
  x: number
  y: number
}
export interface Box extends Point {
  width: number
  height: number
}
export interface PortView {
  id: Id
  role: 'in' | 'out' | 'both'
  side: 'left' | 'right' | 'top' | 'bottom'
  order: number
  label?: string
}
export interface NodeView {
  id: Id
  kind: string
  label: string
  ports: readonly PortView[]
  definitionNodeId?: Id
  detailRef?: Id
  tone?: 'neutral' | 'info' | 'success' | 'warning' | 'danger'
}
export interface Endpoint {
  nodeId: Id
  portId: Id
}
export interface EdgeView {
  id: Id
  kind: string
  label: string
  source: Endpoint | null
  target: Endpoint | null
  definitionEdgeId?: Id
  detailRef?: Id
  tone?: NodeView['tone']
}
export interface LayoutView {
  nodes: readonly ({ id: Id } & Box)[]
  edges: readonly { id: Id; vertices: readonly Point[] }[]
}
export interface Diagnostic {
  id: Id
  code: string
  severity: 'error' | 'warning' | 'info'
  message: string
  target?: Target
  path?: string
}
export interface GraphView {
  graphContractVersion: 1
  scope: GraphScope
  kind: 'definition' | 'runtime'
  nodes: readonly NodeView[]
  edges: readonly EdgeView[]
  layout: LayoutView
  diagnostics: readonly Diagnostic[]
}
export interface ConnectionIntent {
  edgeId?: Id
  source: Endpoint | null
  target: Endpoint | null
  edgeKind: string
}
export type GraphIntent =
  | { type: 'addNode'; catalogKey: string; at: Point }
  | { type: 'remove'; targets: readonly Target[] }
  | { type: 'connect'; connection: ConnectionIntent }
  | { type: 'layout'; nodes: LayoutView['nodes']; edges: LayoutView['edges'] }
export interface ViewportState {
  zoom: number
  x: number
  y: number
}
export type Outcome<T> =
  | { ok: true; value: T; diagnostics: readonly Diagnostic[] }
  | { ok: false; code: string; diagnostics: readonly Diagnostic[] }
export interface CatalogEntry {
  key: string
  label: string
  group: string
  enabled: boolean
  disabledReason?: string
}
export interface EditContext {
  scope: GraphScope
  expectedDocumentStamp: Seq
}
export interface PreparedEdit<D> {
  document: D
  selection: readonly Target[]
}
export interface DomainGraphAdapter<D, C> {
  readonly key: string
  readonly graphContractVersion: 1
  catalog(document: Readonly<D>): readonly CatalogEntry[]
  project(document: Readonly<D>, scope: GraphScope): Outcome<GraphView>
  checkConnection(document: Readonly<D>, intent: ConnectionIntent): readonly Diagnostic[]
  edit(
    document: Readonly<D>,
    command: GraphIntent | C,
    context: EditContext,
  ): Outcome<PreparedEdit<D>>
  validate(document: Readonly<D>, scope: GraphScope): readonly Diagnostic[]
}
export interface RuntimeStamp {
  scope: GraphScope
  runKey: Id
  generation: Seq
  responseVersion: Seq
  requestId: Id
}
export interface Observation {
  sourceKey: Id
  observedOn: string | null
  receivedOn: string | null
  quality: 'known' | 'unknown'
  reason?: string
}
export interface RuntimeInstance {
  id: Id
  definitionNodeId: Id
  iteration: Seq
  attempt: Seq
  stateKey: string
  stateLabel: string
  tone: NonNullable<NodeView['tone']>
  startedOn: string | null
  endedOn: string | null
  detailRef: Id
}
export interface InstanceOverlay {
  nodeId: Id
  instanceIds: readonly Id[]
  label: string
  tone: NonNullable<NodeView['tone']>
}
export interface RuntimeSnapshot {
  graphContractVersion: 1
  stamp: RuntimeStamp
  graph: GraphView
  receivedOn: string
  observedOn: string | null
  completeness: 'complete' | 'partial' | 'unknown'
  reasons: readonly string[]
  observations: readonly Observation[]
  instances: readonly RuntimeInstance[]
  overlays: readonly InstanceOverlay[]
}
export interface DetailSelection {
  stamp: RuntimeStamp
  selectionGeneration: Seq
  target: Target
  detailRef: Id
  instanceId?: Id
}
