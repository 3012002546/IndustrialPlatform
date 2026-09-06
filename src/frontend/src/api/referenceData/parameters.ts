import type { HttpClient, RequestOptions } from '@/api/httpClient'
import type { PageResult, ReferenceDataQuery } from './types'
import type {
  ConfigurationDomain,
  ConfigurationDomainSummary,
  ConfigurationHistory,
  ConfigurationVersion,
  CreateConfigurationDomain,
  EffectiveConfiguration,
  UpdateConfigurationDomain,
  WriteConfigurationKey,
  WriteConfigurationValue,
} from './parameterTypes'

const BASE = '/referencedata/api/v1/reference-data'
const domainPath = (id: string) => `${BASE}/admin/configuration-domains/${encodeURIComponent(id)}`
const keyPath = (id: string, keyId: string) => `${domainPath(id)}/keys/${encodeURIComponent(keyId)}`
const valuePath = (id: string, keyId: string, valueId: string) =>
  `${keyPath(id, keyId)}/values/${encodeURIComponent(valueId)}`
const jsonOptions = { headers: { 'Content-Type': 'application/json' } }

// Keep the original JSON numeric token. Parsing then stringifying would round Int64/Decimal values in JavaScript.
function rawValue(value: string | null): string {
  if (value === null) return 'null'
  const parsed: unknown = JSON.parse(value)
  if (parsed === null) throw new Error('REF-VALIDATION-FAILED')
  return value
}
export function configurationKeyBody(request: WriteConfigurationKey): string {
  const { valueJson, defaultValueJson, ...fields } = request
  return `${JSON.stringify(fields).slice(0, -1)},"value":${rawValue(valueJson)},"defaultValue":${rawValue(defaultValueJson)}}`
}
export function configurationValueBody(request: WriteConfigurationValue): string {
  const { valueJson, ...fields } = request
  return `${JSON.stringify(fields).slice(0, -1)},"value":${rawValue(valueJson)}}`
}

export function createParameterApi(client: HttpClient) {
  return {
    listConfigurationDomains(query: ReferenceDataQuery, options?: RequestOptions) {
      const params = new URLSearchParams()
      for (const [key, value] of Object.entries(query))
        if (value !== undefined && value !== '') params.set(key, String(value))
      return client.get<PageResult<ConfigurationDomainSummary>>(
        `${BASE}/admin/configuration-domains?${params}`,
        options,
      )
    },
    getConfigurationDomain: (id: string, options?: RequestOptions) =>
      client.get<ConfigurationDomain>(domainPath(id), options),
    createConfigurationDomain: (request: CreateConfigurationDomain) =>
      client.post<ConfigurationDomain>(`${BASE}/admin/configuration-domains`, request),
    updateConfigurationDomain: (id: string, request: UpdateConfigurationDomain) =>
      client.put<ConfigurationDomain>(domainPath(id), request),
    setConfigurationDomainStatus: (id: string, enabled: boolean, request: ConfigurationVersion) =>
      client.post<ConfigurationDomain>(
        `${domainPath(id)}/${enabled ? 'enable' : 'disable'}`,
        request,
      ),
    addConfigurationKey: (id: string, request: WriteConfigurationKey) =>
      client.post<ConfigurationDomain>(
        `${domainPath(id)}/keys`,
        configurationKeyBody(request),
        jsonOptions,
      ),
    updateConfigurationKey: (id: string, keyId: string, request: WriteConfigurationKey) =>
      client.put<ConfigurationDomain>(
        keyPath(id, keyId),
        configurationKeyBody(request),
        jsonOptions,
      ),
    setConfigurationKeyStatus: (
      id: string,
      keyId: string,
      enabled: boolean,
      request: ConfigurationVersion,
    ) =>
      client.post<ConfigurationDomain>(
        `${keyPath(id, keyId)}/${enabled ? 'enable' : 'disable'}`,
        request,
      ),
    addConfigurationValue: (id: string, keyId: string, request: WriteConfigurationValue) =>
      client.post<ConfigurationDomain>(
        `${keyPath(id, keyId)}/values`,
        configurationValueBody(request),
        jsonOptions,
      ),
    updateConfigurationValue: (
      id: string,
      keyId: string,
      valueId: string,
      request: WriteConfigurationValue,
    ) =>
      client.put<ConfigurationDomain>(
        valuePath(id, keyId, valueId),
        configurationValueBody(request),
        jsonOptions,
      ),
    setConfigurationValueEnabled: (
      id: string,
      keyId: string,
      valueId: string,
      enabled: boolean,
      request: ConfigurationVersion,
    ) =>
      client.post<ConfigurationDomain>(
        `${valuePath(id, keyId, valueId)}/${enabled ? 'enable' : 'disable'}`,
        request,
      ),
    deleteConfigurationValue: (
      id: string,
      keyId: string,
      valueId: string,
      request: ConfigurationVersion,
    ) => client.delete<ConfigurationDomain>(valuePath(id, keyId, valueId), request, jsonOptions),
    configurationHistory: (
      id: string,
      keyId: string | null,
      pageIndex: number,
      pageSize: number,
      options?: RequestOptions,
    ) =>
      client.get<PageResult<ConfigurationHistory>>(
        `${keyId === null ? domainPath(id) : keyPath(id, keyId)}/history?pageIndex=${pageIndex}&pageSize=${pageSize}`,
        options,
      ),
    resolveConfiguration: (domain: string, key: string, options?: RequestOptions) =>
      client.get<EffectiveConfiguration>(
        `${BASE}/configuration-domains/${encodeURIComponent(domain)}/keys/${encodeURIComponent(key)}`,
        options,
      ),
    resolveConfigurationDomain: (domain: string, options?: RequestOptions) =>
      client.get<{ appDomainNId: string; keys: EffectiveConfiguration[] }>(
        `${BASE}/configuration-domains/${encodeURIComponent(domain)}`,
        options,
      ),
  }
}
