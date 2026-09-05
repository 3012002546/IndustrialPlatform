import type { HttpClient, RequestOptions } from '@/api/httpClient'
import type { PageResult, ReferenceDataQuery, ReferenceScope, VersionRequest } from './types'
import type {
  CreateUnitDimension,
  AvailableUnitDimension,
  RuntimeUnitDimension,
  UnitConversionRequest,
  UnitConversionResult,
  UnitDimension,
  UnitDimensionSummary,
  UpdateUnitDimension,
} from './unitOfMeasureTypes'

const BASE = '/referencedata/api/v1/reference-data'
const ADMIN = `${BASE}/admin/units-of-measure/dimensions`
const RUNTIME = `${BASE}/units-of-measure`
const dimensionPath = (id: string) => `${ADMIN}/${encodeURIComponent(id)}`

function queryString(query: object): string {
  const params = new URLSearchParams()
  for (const [key, value] of Object.entries(query))
    if (value !== undefined && value !== null && value !== '') params.set(key, String(value))
  return `?${params}`
}

export function createUnitOfMeasureApi(client: HttpClient) {
  return {
    listUnitDimensions: (query: ReferenceDataQuery, options?: RequestOptions) =>
      client.get<PageResult<UnitDimensionSummary>>(`${ADMIN}${queryString(query)}`, options),
    getUnitDimension: (id: string, options?: RequestOptions) =>
      client.get<UnitDimension>(dimensionPath(id), options),
    createUnitDimension: (request: CreateUnitDimension) =>
      client.post<UnitDimension>(ADMIN, request),
    updateUnitDimension: (id: string, request: UpdateUnitDimension) =>
      client.put<UnitDimension>(dimensionPath(id), request),
    cloneUnitDimension: (id: string, request: VersionRequest) =>
      client.post<UnitDimension>(`${dimensionPath(id)}/clone`, request),
    publishUnitDimension: (id: string, request: VersionRequest) =>
      client.post<UnitDimension>(`${dimensionPath(id)}/publish`, request),
    disableUnitDimension: (id: string, request: VersionRequest) =>
      client.post<UnitDimension>(`${dimensionPath(id)}/disable`, request),
    listAvailableUnitDimensions: (
      query: Pick<ReferenceDataQuery, 'pageIndex' | 'pageSize' | 'keyword'>,
      options?: RequestOptions,
    ) =>
      client.get<PageResult<AvailableUnitDimension>>(
        `${RUNTIME}/dimensions${queryString(query)}`,
        options,
      ),
    getCurrentUnitDimension: (
      nId: string,
      sourceScope: ReferenceScope,
      sourceTenantNId: string | null,
      options?: RequestOptions,
    ) =>
      client.get<RuntimeUnitDimension>(
        `${RUNTIME}/dimensions/${encodeURIComponent(nId)}${queryString({ sourceScope, sourceTenantNId })}`,
        options,
      ),
    getUnitDimensionRevision: (
      nId: string,
      revision: number,
      sourceScope: ReferenceScope,
      sourceTenantNId: string | null,
      options?: RequestOptions,
    ) =>
      client.get<RuntimeUnitDimension>(
        `${RUNTIME}/dimensions/${encodeURIComponent(nId)}/revisions/${revision}${queryString({ sourceScope, sourceTenantNId })}`,
        options,
      ),
    convertUnit: (request: UnitConversionRequest, options?: RequestOptions) =>
      client.post<UnitConversionResult>(`${RUNTIME}/convert`, request, options),
  }
}
