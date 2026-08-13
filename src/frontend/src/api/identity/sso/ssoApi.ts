/**
 * Identity SSO API(§26):经网关 /identity/api/v1/sso 前缀访问后端 SSO 端点。
 * httpClient 已解包信封,页面直接拿到业务 DTO。
 * 共享 client 已开 withCredentials(http 模式):exchange/authorize 携带并接受
 * 浏览器 SSO 会话 Cookie(句柄只在 HttpOnly Cookie 中流转,§26.4)。
 */

import type { HttpClient } from '@/api/httpClient'

import type {
  SsoBeginResponseDto,
  SsoDiscoveryProviderDto,
  SsoExchangeRequestDto,
  SsoExchangeResponseDto,
  SsoLogoutRequestDto,
  SsoLogoutResponseDto,
} from './types'

const IDENTITY_SSO_PREFIX = '/identity/api/v1/sso'

export interface SsoAuthorizeParams {
  clientId?: string | undefined
  returnUrl?: string | undefined
  providerNId?: string | undefined
}

/** 查询串生成:跳过 undefined/null/空串,其余编码拼接(与 managementApi 同约定)。 */
function toQueryString(params: object): string {
  const entries = Object.entries(params)
    .filter(([, value]) => value !== undefined && value !== null && value !== '')
    .map(([key, value]) => `${encodeURIComponent(key)}=${encodeURIComponent(String(value))}`)
  return entries.length > 0 ? `?${entries.join('&')}` : ''
}

export interface IdentitySsoApi {
  /** 发现启用中的企业登录源(§26.4);connection 为可选名称过滤。 */
  discovery(connection?: string): Promise<SsoDiscoveryProviderDto[]>
  /** 开始授权(§26.4):无 providerNId 时按单源/多源返回跳转地址或选择列表。 */
  authorize(params: SsoAuthorizeParams): Promise<SsoBeginResponseDto>
  /** 一次性票据交换(§26.5):消费票据,签发完整认证会话。 */
  exchange(request: SsoExchangeRequestDto): Promise<SsoExchangeResponseDto>
  /** 登出(§26.9):撤销刷新会话族/sid/浏览器会话;Federated 时返回 IdP 跳转地址。 */
  logout(request: SsoLogoutRequestDto): Promise<SsoLogoutResponseDto>
}

export function createIdentitySsoApi(client: HttpClient): IdentitySsoApi {
  const base = IDENTITY_SSO_PREFIX

  return {
    discovery: (connection) =>
      client.get<SsoDiscoveryProviderDto[]>(
        `${base}/discovery${toQueryString(connection === undefined || connection === '' ? {} : { connection })}`,
      ),
    authorize: (params) =>
      client.get<SsoBeginResponseDto>(`${base}/authorize${toQueryString(params)}`),
    exchange: (request) => client.post<SsoExchangeResponseDto>(`${base}/exchange`, request),
    logout: (request) => client.post<SsoLogoutResponseDto>(`${base}/logout`, request),
  }
}
