import type { HttpClient, RequestOptions } from '@/api/httpClient'
import { createParameterApi } from './parameters'
import { createDynamicConfigurationApi } from './dynamicConfigurations'
import { createUnitOfMeasureApi } from './unitOfMeasure'
import { createMetadataApi } from './metadata'
import { createCodingRuleApi } from './codingRules'
import { createStateMachineApi } from './stateMachines'
import type {
  CreateDictionaryRequest,
  DictionaryDetail,
  DictionaryPublicationCheck,
  DictionarySummary,
  EffectiveDictionary,
  PageResult,
  ReferenceDataQuery,
  UpdateDictionaryRequest,
  VersionRequest,
} from './types'

const BASE = '/referencedata/api/v1/reference-data'
function queryString(query: ReferenceDataQuery): string {
  const params = new URLSearchParams()
  for (const [key, value] of Object.entries(query))
    if (value !== undefined && value !== '') params.set(key, String(value))
  return `?${params.toString()}`
}
const dictionaryPath = (id: string) => `${BASE}/admin/dictionaries/${encodeURIComponent(id)}`

export function createReferenceDataApi(client: HttpClient) {
  return {
    ...createParameterApi(client),
    ...createDynamicConfigurationApi(client),
    ...createUnitOfMeasureApi(client),
    ...createMetadataApi(client),
    ...createCodingRuleApi(client),
    ...createStateMachineApi(client),
    listDictionaries: (query: ReferenceDataQuery, options?: RequestOptions) =>
      client.get<PageResult<DictionarySummary>>(
        `${BASE}/admin/dictionaries${queryString(query)}`,
        options,
      ),
    getDictionary: (id: string, options?: RequestOptions) =>
      client.get<DictionaryDetail>(dictionaryPath(id), options),
    createDictionary: (request: CreateDictionaryRequest) =>
      client.post<DictionaryDetail>(`${BASE}/admin/dictionaries`, request),
    updateDictionary: (id: string, request: UpdateDictionaryRequest) =>
      client.put<DictionaryDetail>(dictionaryPath(id), request),
    cloneDictionary: (id: string, request: VersionRequest) =>
      client.post<DictionaryDetail>(`${dictionaryPath(id)}/clone`, request),
    checkDictionaryPublication: (id: string, options?: RequestOptions) =>
      client.get<DictionaryPublicationCheck>(`${dictionaryPath(id)}/publication-check`, options),
    publishDictionary: (id: string, request: VersionRequest) =>
      client.post<DictionaryDetail>(`${dictionaryPath(id)}/publish`, request),
    disableDictionary: (id: string, request: VersionRequest) =>
      client.post<DictionaryDetail>(`${dictionaryPath(id)}/disable`, request),
    getEffectiveDictionary: (nId: string, options?: RequestOptions) =>
      client.get<EffectiveDictionary>(`${BASE}/dictionaries/${encodeURIComponent(nId)}`, options),
  }
}
export type ReferenceDataApi = ReturnType<typeof createReferenceDataApi>
let referenceDataApi: ReferenceDataApi | null = null
export function registerReferenceDataApi(api: ReferenceDataApi): void {
  referenceDataApi = api
}
export function getReferenceDataApi(): ReferenceDataApi | null {
  return referenceDataApi
}
