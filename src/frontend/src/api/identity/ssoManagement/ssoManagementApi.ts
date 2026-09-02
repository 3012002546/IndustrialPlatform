/**
 * Identity SSO 管理 API(TASK-ID-015,§26.7/§26.8):
 * 经网关 /identity/api/v1/sso-management 前缀访问后端 Management 端点。
 * 读操作需 identity.sso.view,写操作需 identity.sso.manage,连接测试需 identity.sso.test。
 * 移除端点双版本随查询串提交(后端 [FromQuery] 绑定)。
 */

import type { HttpClient } from '@/api/httpClient'

import type {
  AddSsoEndpointRequestDto,
  BindSsoAccountRequestDto,
  CreateSsoClientRequestDto,
  CreateSsoProviderRequestDto,
  ExternalAccountSummaryDto,
  ProviderSummaryDto,
  ProviderTestResultDto,
  SetSsoClientEnabledRequestDto,
  SetSsoEndpointEnabledRequestDto,
  SetSsoProviderEnabledRequestDto,
  SsoClientSummaryDto,
  UpdateSsoClientRequestDto,
  UpdateSsoProviderRequestDto,
  UpdateSsoProviderSecretRequestDto,
} from './types'

const IDENTITY_SSO_MANAGEMENT_PREFIX = '/identity/api/v1/sso-management'

export interface IdentitySsoManagementApi {
  // 企业登录源(§26.8)
  listProviders(): Promise<ProviderSummaryDto[]>
  exportProviders(params: SsoExportParams): Promise<Blob>
  getProvider(providerNId: string): Promise<ProviderSummaryDto>
  createProvider(request: CreateSsoProviderRequestDto): Promise<ProviderSummaryDto>
  updateProvider(
    providerNId: string,
    request: UpdateSsoProviderRequestDto,
  ): Promise<ProviderSummaryDto>
  updateProviderSecret(
    providerNId: string,
    request: UpdateSsoProviderSecretRequestDto,
  ): Promise<ProviderSummaryDto>
  setProviderEnabled(
    providerNId: string,
    request: SetSsoProviderEnabledRequestDto,
  ): Promise<ProviderSummaryDto>
  testProvider(providerNId: string): Promise<ProviderTestResultDto>

  // 外部账号(§26.3/§26.8)
  listAccounts(providerNId: string): Promise<ExternalAccountSummaryDto[]>
  exportAccounts(providerNId: string, params: SsoExportParams): Promise<Blob>
  bindAccount(
    providerNId: string,
    request: BindSsoAccountRequestDto,
  ): Promise<ExternalAccountSummaryDto>
  unbindAccount(providerNId: string, userNId: string): Promise<void>

  // 平台 SSO Client(§26.7)
  listClients(): Promise<SsoClientSummaryDto[]>
  exportClients(params: SsoExportParams): Promise<Blob>
  exportClientEndpoints(clientNId: string, params: SsoExportParams): Promise<Blob>
  getClient(clientNId: string): Promise<SsoClientSummaryDto>
  createClient(request: CreateSsoClientRequestDto): Promise<SsoClientSummaryDto>
  updateClient(clientNId: string, request: UpdateSsoClientRequestDto): Promise<SsoClientSummaryDto>
  setClientEnabled(
    clientNId: string,
    request: SetSsoClientEnabledRequestDto,
  ): Promise<SsoClientSummaryDto>
  addClientEndpoint(
    clientNId: string,
    request: AddSsoEndpointRequestDto,
  ): Promise<SsoClientSummaryDto>
  setClientEndpointEnabled(
    clientNId: string,
    endpointNId: string,
    request: SetSsoEndpointEnabledRequestDto,
  ): Promise<SsoClientSummaryDto>
  removeClientEndpoint(
    clientNId: string,
    endpointNId: string,
    expectedOptimisticVersion: number,
    expectedConcurrencyVersion: string,
  ): Promise<void>
}

export interface SsoExportParams {
  search?: string | undefined
  name?: string | undefined
  protocol?: string | undefined
  enabled?: boolean | undefined
  quantity?: number | 'all' | undefined
  sortField?: string | undefined
  sortOrder?: 'asc' | 'desc' | undefined
  columns?: string[] | undefined
}

function query(params: SsoExportParams): string {
  const entries = Object.entries(params).filter(([, value]) => value !== undefined && value !== '')
  return entries.length === 0
    ? ''
    : `?${entries.map(([key, value]) => `${encodeURIComponent(key)}=${encodeURIComponent(Array.isArray(value) ? value.join(',') : String(value))}`).join('&')}`
}

export function createIdentitySsoManagementApi(client: HttpClient): IdentitySsoManagementApi {
  const base = IDENTITY_SSO_MANAGEMENT_PREFIX

  return {
    // 企业登录源
    listProviders: () => client.get<ProviderSummaryDto[]>(`${base}/providers`),
    exportProviders: (params) => {
      if (client.getBlob === undefined)
        return Promise.reject(new Error('当前 HTTP 客户端不支持文件下载'))
      return client.getBlob(`${base}/providers/export${query(params)}`)
    },
    getProvider: (providerNId) =>
      client.get<ProviderSummaryDto>(`${base}/providers/${encodeURIComponent(providerNId)}`),
    createProvider: (request) => client.post<ProviderSummaryDto>(`${base}/providers`, request),
    updateProvider: (providerNId, request) =>
      client.put<ProviderSummaryDto>(
        `${base}/providers/${encodeURIComponent(providerNId)}`,
        request,
      ),
    updateProviderSecret: (providerNId, request) =>
      client.put<ProviderSummaryDto>(
        `${base}/providers/${encodeURIComponent(providerNId)}/secret`,
        request,
      ),
    setProviderEnabled: (providerNId, request) =>
      client.put<ProviderSummaryDto>(
        `${base}/providers/${encodeURIComponent(providerNId)}/enabled`,
        request,
      ),
    testProvider: (providerNId) =>
      client.post<ProviderTestResultDto>(
        `${base}/providers/${encodeURIComponent(providerNId)}/test`,
      ),

    // 外部账号
    listAccounts: (providerNId) =>
      client.get<ExternalAccountSummaryDto[]>(
        `${base}/providers/${encodeURIComponent(providerNId)}/accounts`,
      ),
    exportAccounts: (providerNId, params) => {
      if (client.getBlob === undefined)
        return Promise.reject(new Error('当前 HTTP 客户端不支持文件下载'))
      return client.getBlob(
        `${base}/providers/${encodeURIComponent(providerNId)}/accounts/export${query(params)}`,
      )
    },
    bindAccount: (providerNId, request) =>
      client.post<ExternalAccountSummaryDto>(
        `${base}/providers/${encodeURIComponent(providerNId)}/accounts`,
        request,
      ),
    unbindAccount: (providerNId, userNId) =>
      client.delete<void>(
        `${base}/providers/${encodeURIComponent(providerNId)}/accounts/${encodeURIComponent(userNId)}`,
      ),

    // 平台 SSO Client
    listClients: () => client.get<SsoClientSummaryDto[]>(`${base}/clients`),
    exportClients: (params) => {
      if (client.getBlob === undefined)
        return Promise.reject(new Error('当前 HTTP 客户端不支持文件下载'))
      return client.getBlob(`${base}/clients/export${query(params)}`)
    },
    exportClientEndpoints: (clientNId, params) => {
      if (client.getBlob === undefined)
        return Promise.reject(new Error('当前 HTTP 客户端不支持文件下载'))
      return client.getBlob(
        `${base}/clients/${encodeURIComponent(clientNId)}/endpoints/export${query(params)}`,
      )
    },
    getClient: (clientNId) =>
      client.get<SsoClientSummaryDto>(`${base}/clients/${encodeURIComponent(clientNId)}`),
    createClient: (request) => client.post<SsoClientSummaryDto>(`${base}/clients`, request),
    updateClient: (clientNId, request) =>
      client.put<SsoClientSummaryDto>(`${base}/clients/${encodeURIComponent(clientNId)}`, request),
    setClientEnabled: (clientNId, request) =>
      client.put<SsoClientSummaryDto>(
        `${base}/clients/${encodeURIComponent(clientNId)}/enabled`,
        request,
      ),
    addClientEndpoint: (clientNId, request) =>
      client.post<SsoClientSummaryDto>(
        `${base}/clients/${encodeURIComponent(clientNId)}/endpoints`,
        request,
      ),
    setClientEndpointEnabled: (clientNId, endpointNId, request) =>
      client.put<SsoClientSummaryDto>(
        `${base}/clients/${encodeURIComponent(clientNId)}/endpoints/${encodeURIComponent(endpointNId)}/enabled`,
        request,
      ),
    removeClientEndpoint: (
      clientNId,
      endpointNId,
      expectedOptimisticVersion,
      expectedConcurrencyVersion,
    ) =>
      client.delete<void>(
        `${base}/clients/${encodeURIComponent(clientNId)}/endpoints/${encodeURIComponent(endpointNId)}` +
          `?expectedOptimisticVersion=${expectedOptimisticVersion}` +
          `&expectedConcurrencyVersion=${encodeURIComponent(expectedConcurrencyVersion)}`,
      ),
  }
}
