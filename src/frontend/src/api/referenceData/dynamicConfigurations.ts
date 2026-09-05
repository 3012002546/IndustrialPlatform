import type { HttpClient, RequestOptions } from '@/api/httpClient'
import type { PageResult, ReferenceDataQuery, VersionRequest } from './types'
import type {
  CreateDynamicConfiguration,
  DynamicConfiguration,
  DynamicConfigurationSummary,
  DynamicFieldWrite,
  DynamicPublicationCheck,
  DynamicRecord,
  DynamicRecordMutation,
  DynamicRecordQuery,
  DynamicRecordWrite,
  DynamicSchema,
  UpdateDynamicConfiguration,
} from './dynamicTypes'

const BASE = '/referencedata/api/v1/reference-data'
const ADMIN = `${BASE}/admin/dynamic-properties/configurations`
const path = (id: string) => `${ADMIN}/${encodeURIComponent(id)}`
const jsonOptions = { headers: { 'Content-Type': 'application/json' } }
function queryString(query: object): string {
  const params = new URLSearchParams()
  for (const [key, value] of Object.entries(query))
    if (value !== undefined && value !== null && value !== '') params.set(key, String(value))
  return `?${params}`
}
function rawValue(value: string | null, numeric = false): string {
  if (value === null) return 'null'
  const parsed: unknown = JSON.parse(value)
  if (parsed === null || (numeric && typeof parsed !== 'number'))
    throw new Error('REF-DYNAMIC-CONFIG-FIELD-INVALID')
  return value
}
function fieldBody(field: DynamicFieldWrite): string {
  const { defaultValueJson, minValueJson, maxValueJson, ...fields } = field
  return `${JSON.stringify(fields).slice(0, -1)},"defaultValue":${rawValue(defaultValueJson)},"minValue":${rawValue(minValueJson, true)},"maxValue":${rawValue(maxValueJson, true)}}`
}
export function dynamicConfigurationBody(
  request: CreateDynamicConfiguration | UpdateDynamicConfiguration,
): string {
  const { fields, ...rest } = request
  return `${JSON.stringify(rest).slice(0, -1)},"fields":[${fields.map(fieldBody).join(',')}]}`
}
export function dynamicRecordBody(request: DynamicRecordWrite): string {
  const { valuesJson, ...fields } = request
  const values = Object.entries(valuesJson)
    .map(([key, value]) => `${JSON.stringify(key)}:${rawValue(value)}`)
    .join(',')
  return `${JSON.stringify(fields).slice(0, -1)},"values":{${values}}}`
}
export function createDynamicConfigurationApi(client: HttpClient) {
  return {
    listDynamicConfigurations: (query: ReferenceDataQuery, options?: RequestOptions) =>
      client.get<PageResult<DynamicConfigurationSummary>>(`${ADMIN}${queryString(query)}`, options),
    getDynamicConfiguration: (id: string, options?: RequestOptions) =>
      client.get<DynamicConfiguration>(path(id), options),
    createDynamicConfiguration: (request: CreateDynamicConfiguration) =>
      client.post<DynamicConfiguration>(ADMIN, dynamicConfigurationBody(request), jsonOptions),
    updateDynamicConfiguration: (id: string, request: UpdateDynamicConfiguration) =>
      client.put<DynamicConfiguration>(path(id), dynamicConfigurationBody(request), jsonOptions),
    cloneDynamicConfiguration: (id: string, request: VersionRequest) =>
      client.post<DynamicConfiguration>(`${path(id)}/clone`, request),
    checkDynamicPublication: (id: string, options?: RequestOptions) =>
      client.get<DynamicPublicationCheck>(`${path(id)}/publication-check`, options),
    publishDynamicConfiguration: (id: string, request: VersionRequest) =>
      client.post<DynamicConfiguration>(`${path(id)}/publish`, request),
    disableDynamicConfiguration: (id: string, request: VersionRequest) =>
      client.post<DynamicConfiguration>(`${path(id)}/disable`, request),
    listDynamicRecords: (id: string, query: DynamicRecordQuery, options?: RequestOptions) =>
      client.get<PageResult<DynamicRecord>>(`${path(id)}/records${queryString(query)}`, options),
    addDynamicRecord: (id: string, request: DynamicRecordWrite) =>
      client.post<DynamicRecordMutation>(
        `${path(id)}/records`,
        dynamicRecordBody(request),
        jsonOptions,
      ),
    updateDynamicRecord: (id: string, recordId: string, request: DynamicRecordWrite) =>
      client.put<DynamicRecordMutation>(
        `${path(id)}/records/${encodeURIComponent(recordId)}`,
        dynamicRecordBody(request),
        jsonOptions,
      ),
    disableDynamicRecord: (id: string, recordId: string, request: VersionRequest) =>
      client.post<DynamicRecordMutation>(
        `${path(id)}/records/${encodeURIComponent(recordId)}/disable`,
        request,
      ),
    getDynamicSchema: (nId: string, options?: RequestOptions) =>
      client.get<DynamicSchema>(
        `${BASE}/dynamic-properties/configurations/${encodeURIComponent(nId)}/schema`,
        options,
      ),
    getDynamicSnapshotRecords: (
      schema: DynamicSchema,
      query: DynamicRecordQuery,
      options?: RequestOptions,
    ) => {
      const { nId, ...page } = query
      return client.get<PageResult<DynamicRecord>>(
        `${BASE}/dynamic-properties/configurations/${encodeURIComponent(schema.nId)}/records${queryString({ ...page, recordNId: nId, revision: schema.revision, sourceScope: schema.sourceScope, sourceTenantNId: schema.sourceTenantNId })}`,
        options,
      )
    },
  }
}
