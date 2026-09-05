import type { HttpClient, RequestOptions } from '@/api/httpClient'
import type { PageResult, ReferenceDataQuery, VersionRequest } from './types'
import type {
  CodePreview,
  CodingRuleDetail,
  CodingRuleSummary,
  CreateCodingRuleRequest,
  GenerateCodeRequest,
  GeneratedCode,
  PreviewCodeRequest,
  UpdateCodingRuleRequest,
} from './codingRuleTypes'

const BASE = '/referencedata/api/v1/reference-data'
const ADMIN = `${BASE}/admin/coding-rules`
const RUNTIME = `${BASE}/coding-rules`
const rulePath = (id: string) => `${ADMIN}/${encodeURIComponent(id)}`

function queryString(query: object): string {
  const params = new URLSearchParams()
  for (const [key, value] of Object.entries(query))
    if (value !== undefined && value !== null && value !== '') params.set(key, String(value))
  return `?${params.toString()}`
}

function withIdempotencyKey(key: string, options: RequestOptions = {}): RequestOptions {
  return {
    ...options,
    headers: { ...(options.headers ?? {}), 'Idempotency-Key': key },
  }
}

export function createCodingRuleApi(client: HttpClient) {
  return {
    listCodingRules: (query: ReferenceDataQuery, options?: RequestOptions) =>
      client.get<PageResult<CodingRuleSummary>>(`${ADMIN}${queryString(query)}`, options),
    getCodingRule: (id: string, options?: RequestOptions) =>
      client.get<CodingRuleDetail>(rulePath(id), options),
    createCodingRule: (request: CreateCodingRuleRequest) =>
      client.post<CodingRuleDetail>(ADMIN, request),
    updateCodingRule: (id: string, request: UpdateCodingRuleRequest) =>
      client.put<CodingRuleDetail>(rulePath(id), request),
    cloneCodingRule: (id: string, request: VersionRequest) =>
      client.post<CodingRuleDetail>(`${rulePath(id)}/clone`, request),
    publishCodingRule: (id: string, request: VersionRequest) =>
      client.post<CodingRuleDetail>(`${rulePath(id)}/publish`, request),
    disableCodingRule: (id: string, request: VersionRequest) =>
      client.post<CodingRuleDetail>(`${rulePath(id)}/disable`, request),
    previewCodingRule: (id: string, request: PreviewCodeRequest, options?: RequestOptions) =>
      client.post<CodePreview>(`${rulePath(id)}/preview`, request, options),
    previewCode: (nId: string, request: PreviewCodeRequest, options?: RequestOptions) =>
      client.post<CodePreview>(`${RUNTIME}/${encodeURIComponent(nId)}/preview`, request, options),
    generateCode: (
      nId: string,
      request: GenerateCodeRequest,
      idempotencyKey: string,
      options?: RequestOptions,
    ) =>
      client.post<GeneratedCode>(
        `${RUNTIME}/${encodeURIComponent(nId)}/generate`,
        request,
        withIdempotencyKey(idempotencyKey, options),
      ),
  }
}

export type CodingRuleApi = ReturnType<typeof createCodingRuleApi>
