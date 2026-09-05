import type { HttpClient, RequestOptions } from '@/api/httpClient'
import type { PageResult, ReferenceDataQuery, ReferenceScope, VersionRequest } from './types'
import type {
  CreateMetadataSchema,
  EffectiveMetadataSchema,
  MetadataPublicationCheck,
  MetadataSchema,
  MetadataSchemaSummary,
  UpdateMetadataSchema,
} from './metadataTypes'

const BASE = '/referencedata/api/v1/reference-data'
const ADMIN = `${BASE}/admin/metadata-schemas`
const RUNTIME = `${BASE}/metadata-schemas`
const schemaPath = (id: string) => `${ADMIN}/${encodeURIComponent(id)}`
const PRECISE_DECIMAL_KEYS = ['minValue', 'maxValue'] as const

function queryString(query: object): string {
  const params = new URLSearchParams()
  for (const [key, value] of Object.entries(query))
    if (value !== undefined && value !== null && value !== '') params.set(key, String(value))
  return `?${params}`
}

function precise(options?: RequestOptions): RequestOptions {
  return { ...options, preserveJsonNumberKeys: PRECISE_DECIMAL_KEYS }
}

function decimalToken(value: string | null): string {
  if (value === null) return 'null'
  const token = value.trim()
  if (!/^-?(?:0|[1-9]\d*)(?:\.\d+)?(?:[eE][+-]?\d+)?$/.test(token)) {
    throw new TypeError(`Invalid metadata decimal: ${value}`)
  }
  return token
}

function metadataBody(request: CreateMetadataSchema | UpdateMetadataSchema): string {
  const { attributes, ...schema } = request
  const attributeJson = attributes.map((attribute) => {
    const { minValue, maxValue, ...rest } = attribute
    const json = JSON.stringify(rest)
    return `${json.slice(0, -1)},"minValue":${decimalToken(minValue)},"maxValue":${decimalToken(maxValue)}}`
  })
  const json = JSON.stringify(schema)
  return `${json.slice(0, -1)},"attributes":[${attributeJson.join(',')}]}`
}

function writeOptions(): RequestOptions {
  return precise({ headers: { 'Content-Type': 'application/json' } })
}

export function createMetadataApi(client: HttpClient) {
  return {
    listMetadataSchemas: (query: ReferenceDataQuery, options?: RequestOptions) =>
      client.get<PageResult<MetadataSchemaSummary>>(
        `${ADMIN}${queryString(query)}`,
        precise(options),
      ),
    getMetadataSchema: (id: string, options?: RequestOptions) =>
      client.get<MetadataSchema>(schemaPath(id), precise(options)),
    createMetadataSchema: (request: CreateMetadataSchema) =>
      client.post<MetadataSchema>(ADMIN, metadataBody(request), writeOptions()),
    updateMetadataSchema: (id: string, request: UpdateMetadataSchema) =>
      client.put<MetadataSchema>(schemaPath(id), metadataBody(request), writeOptions()),
    cloneMetadataSchema: (id: string, request: VersionRequest) =>
      client.post<MetadataSchema>(`${schemaPath(id)}/clone`, request, precise()),
    checkMetadataPublication: (id: string, options?: RequestOptions) =>
      client.get<MetadataPublicationCheck>(`${schemaPath(id)}/publication-check`, options),
    publishMetadataSchema: (id: string, request: VersionRequest) =>
      client.post<MetadataSchema>(`${schemaPath(id)}/publish`, request, precise()),
    disableMetadataSchema: (id: string, request: VersionRequest) =>
      client.post<MetadataSchema>(`${schemaPath(id)}/disable`, request, precise()),
    getEffectiveMetadataSchema: (nId: string, options?: RequestOptions) =>
      client.get<EffectiveMetadataSchema>(
        `${RUNTIME}/${encodeURIComponent(nId)}`,
        precise(options),
      ),
    getMetadataSchemaRevision: (
      nId: string,
      revision: number,
      sourceScope: ReferenceScope,
      sourceTenantNId: string | null,
      options?: RequestOptions,
    ) =>
      client.get<EffectiveMetadataSchema>(
        `${RUNTIME}/${encodeURIComponent(nId)}/revisions/${revision}${queryString({ sourceScope, sourceTenantNId })}`,
        precise(options),
      ),
  }
}
