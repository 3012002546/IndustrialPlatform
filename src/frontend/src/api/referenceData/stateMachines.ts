import type { HttpClient, RequestOptions } from '@/api/httpClient'
import type { PageResult, ReferenceDataQuery, ReferenceScope, VersionRequest } from './types'
import type {
  AvailableStateMachine,
  CreateStateMachineRequest,
  RuntimeStateMachine,
  StateMachineDetail,
  StateMachinePublicationCheck,
  StateMachineSummary,
  TransitionEvaluation,
  TransitionEvaluationRequest,
  UpdateStateMachineRequest,
} from './stateMachineTypes'

const BASE = '/referencedata/api/v1/reference-data'
const ADMIN = `${BASE}/admin/state-machines`
const RUNTIME = `${BASE}/state-machines`
const definitionPath = (id: string) => `${ADMIN}/${encodeURIComponent(id)}`

function queryString(query: object): string {
  const params = new URLSearchParams()
  for (const [key, value] of Object.entries(query))
    if (value !== undefined && value !== null && value !== '') params.set(key, String(value))
  return `?${params.toString()}`
}

export function createStateMachineApi(client: HttpClient) {
  return {
    listStateMachines: (query: ReferenceDataQuery, options?: RequestOptions) =>
      client.get<PageResult<StateMachineSummary>>(`${ADMIN}${queryString(query)}`, options),
    getStateMachine: (id: string, options?: RequestOptions) =>
      client.get<StateMachineDetail>(definitionPath(id), options),
    createStateMachine: (request: CreateStateMachineRequest) =>
      client.post<StateMachineDetail>(ADMIN, request),
    updateStateMachine: (id: string, request: UpdateStateMachineRequest) =>
      client.put<StateMachineDetail>(definitionPath(id), request),
    cloneStateMachine: (id: string, request: VersionRequest) =>
      client.post<StateMachineDetail>(`${definitionPath(id)}/clone`, request),
    checkStateMachinePublication: (id: string, options?: RequestOptions) =>
      client.get<StateMachinePublicationCheck>(`${definitionPath(id)}/publication-check`, options),
    publishStateMachine: (id: string, request: VersionRequest) =>
      client.post<StateMachineDetail>(`${definitionPath(id)}/publish`, request),
    disableStateMachine: (id: string, request: VersionRequest) =>
      client.post<StateMachineDetail>(`${definitionPath(id)}/disable`, request),
    listAvailableStateMachines: (
      query: Pick<ReferenceDataQuery, 'pageIndex' | 'pageSize' | 'keyword'>,
      options?: RequestOptions,
    ) => client.get<PageResult<AvailableStateMachine>>(`${RUNTIME}${queryString(query)}`, options),
    getCurrentStateMachine: (
      nId: string,
      sourceScope: ReferenceScope,
      sourceTenantNId: string | null,
      options?: RequestOptions,
    ) =>
      client.get<RuntimeStateMachine>(
        `${RUNTIME}/${encodeURIComponent(nId)}${queryString({ sourceScope, sourceTenantNId })}`,
        options,
      ),
    getStateMachineRevision: (
      nId: string,
      revision: number,
      sourceScope: ReferenceScope,
      sourceTenantNId: string | null,
      options?: RequestOptions,
    ) =>
      client.get<RuntimeStateMachine>(
        `${RUNTIME}/${encodeURIComponent(nId)}/revisions/${revision}${queryString({ sourceScope, sourceTenantNId })}`,
        options,
      ),
    evaluateStateTransition: (
      nId: string,
      request: TransitionEvaluationRequest,
      options?: RequestOptions,
    ) =>
      client.post<TransitionEvaluation>(
        `${RUNTIME}/${encodeURIComponent(nId)}/evaluate`,
        request,
        options,
      ),
  }
}

export type StateMachineApi = ReturnType<typeof createStateMachineApi>
