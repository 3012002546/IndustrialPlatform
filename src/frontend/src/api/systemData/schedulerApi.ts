import type { HttpClient } from '@/api/httpClient'

export interface SchedulerHandler {
  handlerKey: string
  name: string
  description: string
  retrySafety: string
  supportsCancellation: boolean
  maxTimeoutSeconds: number
  parametersEditable: boolean
}
export interface JobValues {
  name: string
  description: string | null
  parameters: unknown
  cronExpression: string
  timeZoneId: string
  timeoutSeconds: number
  maxAttempts: number
}
export interface JobVersion {
  optimisticVersion: string
  concurrencyVersion: string
}
export interface SchedulerJob extends JobValues, JobVersion {
  nId: string
  handlerKey: string
  status: string
  retrySafety: string
  jobKind?: 'Recurring' | 'OneShot'
  ownerService?: string | null
  ownerObjectNId?: string | null
  isSystemOwned: boolean
  createdOn: string
  lastUpdatedOn: string
  syncState: string
  syncErrorCode: string | null
  nextFireOn: string | null
}
export interface SchedulerRun {
  nId: string
  jobNId: string
  jobName: string
  source: string
  originalRunNId: string | null
  status: string
  acceptedOn: string
  startedOn: string | null
  deadlineOn: string | null
  completedOn: string | null
  cancelRequestedOn: string | null
  attemptsUsed: number
  maxAttempts: number
  retrySafety: string
  resultCode: string | null
  resultSummary: { outcome: string; resultCode: string; message: string | null } | null
  traceId: string
  stateVersion: string
  actions: { canCancel: boolean; canRetry: boolean; canResolve: boolean }
}
export interface RunDetail {
  run: SchedulerRun
  handlerKey: string
  parameters: unknown
  operationKey: string
  attemptHistory: {
    availability: string
    reasonCode: string | null
    items: {
      number: number
      status: string
      startedOn: string | null
      completedOn: string | null
      resultCode: string | null
      errorSummary: string | null
      traceId: string | null
    }[]
  }
  resolution: {
    decision: string
    reason: string
    evidenceReference: string
    actorUserNId: string
    resolvedOn: string
    traceId: string
  } | null
}
export interface HealthCheck {
  serviceNId: string
  capabilityKey: string
  name: string
  criticality: string
  reportedState: string
  effectiveState: string
  checkedOn: string | null
  receivedOn: string | null
  reasonCode: string | null
  reasonSummary: string | null
  traceId: string | null
  sourceTraceId: string | null
}
export interface HealthService {
  serviceNId: string
  name: string
  state: string
  lastObservedOn: string | null
  checks: HealthCheck[]
  configuredCount: number
  enabledCount: number
  healthyCount: number
  degradedCount: number
  unavailableCount: number
  unknownCount: number
  disabledCount: number
}
export interface HealthSummary {
  state: string
  generatedOn: string
  services: HealthService[]
  healthyCount: number
  degradedCount: number
  unavailableCount: number
  unknownCount: number
  disabledCount: number
}
export interface SchedulerPage<T> {
  items: T[]
  totalCount: string
  page: number
  pageSize: number
}
export interface SchedulePreview {
  timeZoneId: string
  evaluatedOn: string
  occurrences: { utc: string; zonedTime: string }[]
}
export interface RunAccepted {
  runNId: string
  status: string
  acceptedOn: string
}
const BASE = '/systemdata/api/v1'
const id = encodeURIComponent
const query = (values: Record<string, string | number | undefined>): string => {
  const params = new URLSearchParams()
  Object.entries(values).forEach(([key, value]) => {
    if (value !== undefined && value !== '') params.set(key, String(value))
  })
  return '?' + params.toString()
}
export function createSchedulerApi(client: HttpClient) {
  return {
    handlers: () => client.get<SchedulerHandler[]>(`${BASE}/scheduler/handlers`),
    jobs: (values: Record<string, string | number | undefined>) =>
      client.get<SchedulerPage<SchedulerJob>>(`${BASE}/scheduler/jobs${query(values)}`),
    job: (nId: string) => client.get<SchedulerJob>(`${BASE}/scheduler/jobs/${id(nId)}`),
    create: (values: JobValues & { nId: string; handlerKey: string }) =>
      client.post<SchedulerJob>(`${BASE}/scheduler/jobs`, values),
    update: (nId: string, values: JobValues & JobVersion) =>
      client.put<SchedulerJob>(`${BASE}/scheduler/jobs/${id(nId)}`, values),
    state: (
      nId: string,
      action: 'enable' | 'disable' | 'delete' | 'restore',
      version: JobVersion,
    ) => client.post<SchedulerJob>(`${BASE}/scheduler/jobs/${id(nId)}/${action}`, version),
    preview: (cronExpression: string, timeZoneId: string) =>
      client.post<SchedulePreview>(`${BASE}/scheduler/jobs/preview`, {
        cronExpression,
        timeZoneId,
      }),
    trigger: (nId: string, version: JobVersion, requestKey: string) =>
      client.post<RunAccepted>(`${BASE}/scheduler/jobs/${id(nId)}/run`, version, {
        headers: { 'Idempotency-Key': requestKey },
      }),
    runs: (values: Record<string, string | number | undefined>) =>
      client.get<SchedulerPage<SchedulerRun>>(`${BASE}/scheduler/runs${query(values)}`),
    run: (nId: string) => client.get<RunDetail>(`${BASE}/scheduler/runs/${id(nId)}`),
    cancel: (nId: string, stateVersion: string) =>
      client.post<SchedulerRun>(`${BASE}/scheduler/runs/${id(nId)}/cancel`, { stateVersion }),
    retry: (nId: string, stateVersion: string, requestKey: string) =>
      client.post<RunAccepted>(
        `${BASE}/scheduler/runs/${id(nId)}/retry`,
        { stateVersion },
        { headers: { 'Idempotency-Key': requestKey } },
      ),
    resolve: (
      nId: string,
      values: { stateVersion: string; decision: string; reason: string; evidenceReference: string },
    ) => client.post<SchedulerRun>(`${BASE}/scheduler/runs/${id(nId)}/resolve`, values),
    health: (workspace = false) =>
      client.get<HealthSummary>(`${BASE}/platform-health/${workspace ? 'workspace' : 'summary'}`),
  }
}
export type SchedulerApi = ReturnType<typeof createSchedulerApi>
let api: SchedulerApi | null = null
export function registerSchedulerApi(value: SchedulerApi): void {
  api = value
}
export function getSchedulerApi(): SchedulerApi {
  if (!api) throw new Error('任务与健康接口尚未连接。')
  return api
}
