import type { HttpClient } from './httpClient'

// Schema-driven forms are validated by the Studio document validator at the API boundary.
// eslint-disable-next-line @typescript-eslint/no-explicit-any
export type Json = Record<string, any>
export interface Version {
  optimisticVersion: string
  concurrencyVersion: string
}
export interface StudioResource extends Version {
  nId: string
  kind: string
  name: string
  description?: string
  folder?: string
  isEnabled: boolean
  publishedRevisionNId: string | null
  publishedRevisionNo: string | null
  hasDraftChanges: boolean
  lastUpdatedOn: string
}
export interface StudioDraft {
  resource: StudioResource
  draftDocument: Json
  diagnostics: unknown[]
}
type StudioDraftResponse = StudioResource & Pick<StudioDraft, 'draftDocument' | 'diagnostics'>
function normalizeDraft(result: StudioDraftResponse): StudioDraft {
  const { draftDocument, diagnostics, ...resource } = result
  return { resource, draftDocument, diagnostics }
}
export interface StudioRevision {
  nId: string
  revisionNo: string
  releaseNote: string
  createdOn: string
  document: Json
}
export type StudioRevisionSummary = Pick<
  StudioRevision,
  'nId' | 'revisionNo' | 'releaseNote' | 'createdOn'
>
export interface StudioPage<T> {
  items: T[]
  totalCount: string
  page: number
  pageSize: number
}
export interface StudioSource extends Version {
  nId: string
  name: string
  description: string
  provider: string
  endpointKey: string
  catalogKey: string
  configurationVersion: string
  allowedObjects: unknown[]
  isEnabled: boolean
  credentialConfigured: boolean
  timeoutSeconds: number
  maxRows: number
  maxBytes: string
}
export interface LedgerRecord extends Version {
  nId: string
  values: Json
  isDeleted: boolean
  schemaRevisionNId: string
}
export interface StudioRuntime {
  resource: StudioResource
  revisionNId: string
  document: Json
  instances: Record<string, { data: unknown; quality?: Json }>
  diagnostics: unknown[]
  ledgerModel?: { nId: string; revisionNId: string; document: Json }
}
export interface DashboardRuntime {
  resource: StudioResource
  revisionNId: string
  document: Json
  instances: Record<string, Json>
  diagnostics: unknown[]
  transport?: string
  sourceFreshness?: Json
}
export interface StudioCapabilities {
  coreReady: boolean
  modules: {
    key: string
    installed: boolean
    enabled: boolean
    available: boolean
    reasonCode?: string
  }[]
}
export interface RegistryEntry {
  key: string
  version?: string
  label?: string
  mutating?: boolean
  inputSchema?: Json
  [key: string]: unknown
}
export interface StudioRegistry {
  components: RegistryEntry[]
  actions: RegistryEntry[]
  codeComponents: RegistryEntry[]
}
const BASE = '/api/v1/studio'
const id = encodeURIComponent
export const versionOf = (v: Version): Version => ({
  optimisticVersion: v.optimisticVersion,
  concurrencyVersion: v.concurrencyVersion,
})
export function createStudioApi(client: HttpClient) {
  const get = <T>(path: string) =>
    client.get<T>(BASE + path, {
      preserveJsonNumberKeys: [
        'optimisticVersion',
        'revisionNo',
        'totalCount',
        'configurationVersion',
        'length',
        'offset',
      ],
    })
  const post = <T>(path: string, body: unknown, key: string = crypto.randomUUID()) =>
    client.post<T>(BASE + path, body, { headers: { 'Idempotency-Key': key } })
  const put = <T>(path: string, body: unknown, key: string = crypto.randomUUID()) =>
    client.put<T>(BASE + path, body, { headers: { 'Idempotency-Key': key } })
  return {
    client,
    capabilities: async (): Promise<StudioCapabilities> => {
      const profile = await get<{ installedModules: string[]; enabledModules: string[] }>(
        '/deployment/profile',
      )
      return {
        coreReady: true,
        modules: [...new Set([...profile.installedModules, ...profile.enabledModules])].map(
          (key) => ({
            key,
            installed: profile.installedModules.includes(key),
            enabled: profile.enabledModules.includes(key),
            available:
              profile.installedModules.includes(key) && profile.enabledModules.includes(key),
          }),
        ),
      }
    },
    pageContract: (nId: string, revisionNId?: string) =>
      get<StudioRuntime>(
        `/pages/${id(nId)}/contract${revisionNId ? '?revisionNId=' + id(revisionNId) : ''}`,
      ),
    registry: async () => {
      const result = await get<StudioRegistry & { codePages?: RegistryEntry[] }>(
        '/registry/catalog',
      )
      return { ...result, codeComponents: result.codeComponents ?? result.codePages ?? [] }
    },
    list: (kind: string, params: Json = {}) =>
      get<StudioPage<StudioResource>>(
        '/resources?' +
          new URLSearchParams(
            Object.entries({ kind, ...params }).map(([k, v]) => [k, String(v ?? '')]),
          ),
      ),
    create: (body: Json) =>
      post<StudioResource>('/resources', { nId: crypto.randomUUID(), ...body }),
    draft: async (nId: string) =>
      normalizeDraft(await get<StudioDraftResponse>(`/resources/${id(nId)}/draft`)),
    save: async (r: StudioResource, document: Json) =>
      normalizeDraft(
        await put<StudioDraftResponse>(`/resources/${id(r.nId)}/draft`, {
          ...versionOf(r),
          name: r.name,
          description: r.description,
          folder: r.folder,
          document,
        }),
      ),
    validate: (nId: string, document: Json) =>
      post<{
        valid: boolean
        diagnostics: unknown[]
        dependencyPlan?: Json
        dependencyPlanHash?: string
      }>(`/resources/${id(nId)}/validate`, { document }),
    publish: (r: StudioResource, releaseNote: string, dependencyPlanHash?: unknown) =>
      post<StudioRevision>(`/resources/${id(r.nId)}/publish`, {
        ...versionOf(r),
        releaseNote,
        enable: true,
        dependencyPlanHash,
      }),
    revisions: (nId: string) => get<StudioRevisionSummary[]>(`/resources/${id(nId)}/revisions`),
    revision: (nId: string, revisionNId: string) =>
      get<StudioRevision>(`/resources/${id(nId)}/revisions/${id(revisionNId)}`),
    rollback: (r: StudioResource, revisionNId: string, reason: string) =>
      post<StudioResource>(`/resources/${id(r.nId)}/rollback`, {
        ...versionOf(r),
        revisionNId,
        reason,
      }),
    resourceAction: (r: StudioResource, action: string) =>
      post<StudioResource>(`/resources/${id(r.nId)}/${action}`, versionOf(r)),
    grants: (nId: string) => get<Json[]>(`/resources/${id(nId)}/grants`),
    saveGrants: (r: StudioResource, grants: Json[]) =>
      put<Version>(`/resources/${id(r.nId)}/grants`, { ...versionOf(r), grants }),
    sources: (params: Json = {}) =>
      get<StudioPage<StudioSource>>(
        '/sources?' +
          new URLSearchParams(Object.entries(params).map(([k, v]) => [k, String(v ?? '')])),
      ),
    sourceCatalog: (nId: string, configurationVersion: string) =>
      get<Json>(`/sources/${id(nId)}/catalog?configurationVersion=${id(configurationVersion)}`),
    saveSource: (body: Json, nId?: string) =>
      nId ? put<StudioSource>(`/sources/${id(nId)}`, body) : post<StudioSource>('/sources', body),
    sourceAction: (r: StudioSource, action: string) =>
      post<unknown>(`/sources/${id(r.nId)}/${action}`, versionOf(r)),
    datasetPreview: (document: Json, parameters: Json) =>
      post<Json>('/datasets/preview', {
        sourceNId: document.sourceNId,
        sourceConfigurationVersion: document.sourceConfigurationVersion,
        document,
        parameters,
      }),
    dataset: (nId: string, revisionNId: string, parameters: Json) =>
      post<Json>(`/datasets/${id(nId)}/execute`, { revisionNId, parameters }),
    runtime: (nId: string, revisionNId?: string, parameters: Json = {}) =>
      post<StudioRuntime>(`/pages/${id(nId)}/runtime`, { revisionNId, parameters }),
    preview: (nId: string, document: Json, parameters: Json) =>
      post<StudioRuntime>(`/pages/${id(nId)}/preview`, { document, parameters }),
    dashboardRuntime: (
      nId: string,
      revisionNId?: string,
      parameters: Json = {},
      instanceIds?: string[],
    ) =>
      get<DashboardRuntime>(
        `/dashboards/${id(nId)}/runtime?${new URLSearchParams({
          ...(revisionNId ? { revisionNId } : {}),
          parameters: JSON.stringify(parameters),
          ...(instanceIds ? { instanceIds: JSON.stringify(instanceIds) } : {}),
        })}`,
      ),
    dashboardDefinition: (nId: string, revisionNId?: string) =>
      get<Json>(
        `/dashboards/${encodeURIComponent(nId)}/definition${revisionNId ? '?revisionNId=' + encodeURIComponent(revisionNId) : ''}`,
      ),
    dashboardPreview: (
      document: Json,
      parameters: Json,
      selectedComponentId?: string,
      sampleRow?: Json,
      nId?: string,
      instanceIds?: string[],
    ) =>
      post<DashboardRuntime>('/dashboards/preview', {
        nId,
        document,
        parameters,
        selectedComponentId,
        sampleRow,
        ...(instanceIds ? { instanceIds } : {}),
      }),
    dashboardResolve: (nId: string, request: Json) =>
      post<Json>(`/dashboards/${id(nId)}/navigation/resolve`, request),
    dashboardPreviewNavigation: (nId: string, request: Json) =>
      post<Json>(`/dashboards/${id(nId)}/preview/navigation`, request),
    playlistRuntime: (nId: string, revisionNId?: string) =>
      get<Json>(
        `/playlists/${id(nId)}/runtime${revisionNId ? '?revisionNId=' + id(revisionNId) : ''}`,
      ),
    action: (nId: string, actionId: string, body: Json, preview: boolean, key?: string) =>
      post<Json>(
        preview ? '/actions/preview' : `/pages/${id(nId)}/actions/${id(actionId)}/execute`,
        preview ? { ...body, actionId } : body,
        key,
      ),
    operation: (key: string) => get<Json>(`/operations/${id(key)}`),
    ledgerQuery: (modelNId: string, revisionNId: string, body: Json) =>
      post<StudioPage<LedgerRecord>>(
        `/ledger/${id(modelNId)}/records/query?revisionNId=${id(revisionNId)}`,
        body,
      ),
    ledgerGet: (modelNId: string, nId: string, revisionNId?: string) =>
      get<LedgerRecord>(
        `/ledger/${id(modelNId)}/records/${id(nId)}${revisionNId ? '?revisionNId=' + id(revisionNId) : ''}`,
      ),
    ledgerWrite: (
      modelNId: string,
      revisionNId: string,
      values: Json,
      record?: LedgerRecord,
      key?: string,
      nId?: string,
    ) =>
      record
        ? put<LedgerRecord>(
            `/ledger/${id(modelNId)}/records/${id(record.nId)}`,
            {
              ...versionOf(record),
              values,
              revisionNId,
            },
            key,
          )
        : post<LedgerRecord>(`/ledger/${id(modelNId)}/records`, { values, revisionNId, nId }, key),
    ledgerAction: (modelNId: string, record: LedgerRecord, action: string) =>
      post<LedgerRecord>(
        `/ledger/${id(modelNId)}/records/${id(record.nId)}/${action}`,
        versionOf(record),
      ),
    upgrade: (nId: string, target: Version) =>
      post<Json>(`/ledger/${id(nId)}/upgrade-preview`, { targetDraftVersion: versionOf(target) }),
  }
}
export type StudioApi = ReturnType<typeof createStudioApi>
let api: StudioApi | null = null
export function registerStudioApi(value: StudioApi) {
  api = value
}
export function getStudioApi(): StudioApi {
  if (!api) throw new Error('Studio HTTP API unavailable')
  return api
}
