import type { HttpClient } from './httpClient'
import { getStudioApi, type StudioPage } from './studio'
import type {
  TraceColumn,
  TraceDirection,
  TraceDocument,
  TraceIdentity,
  TraceLayoutDirection,
  TraceParameter,
  TraceSearchField,
} from '@/components/studio/trace/model'

export interface TraceStartRow {
  startKey: string
  objectTypeId: string
  identity: TraceIdentity
  values: Record<string, unknown>
}
export interface TraceNode {
  key: string
  objectTypeId: string
  identity: TraceIdentity
  summary: { label: string; value: unknown }[]
  grain: string
  nodeToken: string
  boundary?: boolean
  boundaryReasons?: string[]
}
export interface TraceEdge {
  key: string
  relationId: string
  from: string
  to: string
  kind: string
  evidence: { recordKey: unknown; occurredOn: unknown; quantity?: unknown; unit?: unknown }
}
export interface TraceRelationPreview {
  resolvedInput: unknown
  sourceRows: unknown[]
  nodes: Omit<TraceNode, 'nodeToken'>[]
  edges: Omit<TraceEdge, 'key' | 'kind'>[]
  diagnostics: unknown[]
}
export interface TraceResult {
  requestId: string
  revisionNId: string
  nodes: TraceNode[]
  edges: TraceEdge[]
  status: 'Complete' | 'Partial'
  partialReasons: string[]
  observedOn: string | null
  sourceObservations: {
    sourceKey?: string
    datasetRevisionNId?: string
    observedOn: string | null
    receivedOn?: string | null
    quality?: { status: string; reason?: string }
    reason?: string
  }[]
  cursor?: string
}
export interface TraceContract {
  presentation?: { layoutDirection: TraceLayoutDirection }
  resource: { name: string; nId: string; publishedRevisionNId: string }
  revisionNId: string
  revisionNo: string
  maxDepth: number
  parameters: TraceParameter[]
  relations: { id: string; name: string; kind: 'Genealogy' | 'Context' }[]
  start: { searchFields: TraceSearchField[]; columns: TraceColumn[] }
}
export interface TraceExecutableModel {
  nId: string
  name: string
  revisionNId: string
  revisionNo: string
}
export interface TraceDetail {
  fields: { label: string; value: unknown; format?: string }[]
  links: { id: string; label: string; kind: string }[]
  observedOn: string | null
  sections?: TraceDetailSectionSummary[]
}
export interface TraceDetailSectionSummary {
  id: string
  label: string
  kind: 'Fields' | 'Table'
  pageSize: number
}
export interface TraceSectionResult {
  sectionId: string
  kind: 'Fields' | 'Table'
  fields: { label: string; value: unknown; format?: string }[]
  columns: TraceColumn[]
  items: { key: string; values: Record<string, unknown> }[]
  totalCount: string | number
  page: number
  pageSize: number
  observedOn: string | null
}
export interface TraceModalLink {
  title?: string
  newTabUrl?: string
  kind: string
  modalUrl: string
  expiresOn: string
  embeddable: boolean
}
export interface TraceLinkSources {
  ownedAvailable: boolean
  external: {
    key: string
    embeddable: boolean
    valueParameter: string
    allowedParameters: string[]
  }[]
  mes: {
    key: string
    scanPolicy: string
    allowedContentTypes: string[]
    maxBytes: string
    requiresFiles: boolean
    available: boolean
  }[]
}
export interface TraceNodeRequest {
  revisionNId: string
  nodeKey: string
  nodeToken: string
}
export function createTraceApi(client: HttpClient) {
  const base = '/api/v1/studio',
    id = encodeURIComponent
  const post = <T>(path: string, body: unknown, signal?: AbortSignal, timeoutMs?: number) =>
    client.post<T>(base + path, body, {
      ...(signal ? { signal } : {}),
      ...(timeoutMs === undefined ? {} : { timeoutMs }),
      headers: { 'Idempotency-Key': crypto.randomUUID() },
      preserveJsonNumberKeys: ['totalCount', 'quantity'],
    })
  return {
    models: (signal?: AbortSignal) =>
      client.get<{ items: TraceExecutableModel[] }>(
        base + '/trace-models/runnable',
        signal ? { signal } : {},
      ),
    linkSources: () => client.get<TraceLinkSources>(base + '/trace/link-sources'),
    contract: (nId: string, revisionNId?: string, signal?: AbortSignal) =>
      client.get<TraceContract>(
        `${base}/trace/${id(nId)}/contract${revisionNId ? '?revisionNId=' + id(revisionNId) : ''}`,
        signal ? { signal } : {},
      ),
    starts: (
      nId: string,
      input: {
        revisionNId: string
        parameters: Record<string, unknown>
        page: number
        pageSize: number
      },
      signal?: AbortSignal,
    ) => post<StudioPage<TraceStartRow>>(`/trace/${id(nId)}/starts`, input, signal),
    options: (
      nId: string,
      input: { revisionNId: string; parameterName: string; parameters: Record<string, unknown> },
      signal?: AbortSignal,
    ) =>
      post<{ items: { value: string; label: string }[] }>(
        `/trace/${id(nId)}/search-options`,
        input,
        signal,
      ),
    previewOptions: (
      document: TraceDocument,
      parameterName: string,
      parameters: Record<string, unknown>,
      signal?: AbortSignal,
    ) =>
      post<{ items: { value: string; label: string }[] }>(
        '/trace-models/preview-options',
        { document, parameterName, parameters },
        signal,
      ),
    query: (
      nId: string,
      input: {
        revisionNId: string
        startKeys: string[]
        direction: TraceDirection
        relationIds?: string[]
        maxDepth?: number
      },
      signal?: AbortSignal,
      // Traversal allows 30 seconds, plus authorization and response checks outside that budget.
    ) => post<TraceResult>(`/trace/${id(nId)}/query`, input, signal, 60000),
    expand: (
      nId: string,
      input: TraceNodeRequest & {
        direction: TraceDirection
        relationIds?: string[]
        cursor?: string
      },
      signal?: AbortSignal,
    ) => post<TraceResult>(`/trace/${id(nId)}/expand`, input, signal, 60000),
    detail: (nId: string, input: TraceNodeRequest, signal?: AbortSignal) =>
      post<TraceDetail>(`/trace/${id(nId)}/node-detail`, input, signal),
    section: (
      nId: string,
      input: TraceNodeRequest & { sectionId: string; page: number; pageSize: number },
      signal?: AbortSignal,
    ) => post<TraceSectionResult>(`/trace/${id(nId)}/node-section`, input, signal),
    link: (nId: string, input: TraceNodeRequest & { linkId: string }, signal?: AbortSignal) =>
      post<TraceModalLink>(`/trace/${id(nId)}/node-link`, input, signal),
    previewStart: (
      document: TraceDocument,
      parameters: Record<string, unknown>,
      page = 1,
      signal?: AbortSignal,
    ) =>
      post<
        StudioPage<TraceStartRow> & {
          diagnostics: unknown[]
          collectionPath: string[]
          resolvedParameters: Record<string, unknown>
        }
      >('/trace-models/preview-start', { document, parameters, page, pageSize: 20 }, signal),
    previewRelation: (
      document: TraceDocument,
      relationId: string,
      direction: TraceDirection,
      nodeSample: unknown,
      signal?: AbortSignal,
    ) =>
      post<TraceRelationPreview>(
        '/trace-models/preview-relation',
        { document, relationId, direction, nodeSample },
        signal,
      ),
  }
}
export type TraceApi = ReturnType<typeof createTraceApi>
export const getTraceApi = () => createTraceApi(getStudioApi().client)
