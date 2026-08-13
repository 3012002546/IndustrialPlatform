/**
 * Identity 认证端点(经 Gateway /identity 前缀,§15):
 * login / refresh / logout / me。只做路径与请求体封装,类型映射见 mapper.ts。
 * 401 刷新重试等横切行为由 httpClient 的 authRefresh 拦截处理(认证路径本身不触发)。
 */

import type { HttpClient } from '@/api/httpClient'

import type {
  IdentityAuthSessionDto,
  IdentityAuthUserDto,
  IdentityLoginRequest,
  IdentityLogoutRequest,
  IdentityRefreshRequest,
} from './types'

/** Gateway 上 Identity 认证端点前缀(网关剥离 /identity,Identity 内部为 api/v1/auth)。 */
export const IDENTITY_AUTH_PREFIX = '/identity/api/v1/auth'

export interface IdentityAuthApi {
  login(request: IdentityLoginRequest): Promise<IdentityAuthSessionDto>
  refresh(request: IdentityRefreshRequest): Promise<IdentityAuthSessionDto>
  logout(request: IdentityLogoutRequest): Promise<void>
  getCurrentUser(): Promise<IdentityAuthUserDto>
}

export function createIdentityAuthApi(client: HttpClient): IdentityAuthApi {
  return {
    login: (request) =>
      client.post<IdentityAuthSessionDto>(`${IDENTITY_AUTH_PREFIX}/login`, request),
    refresh: (request) =>
      client.post<IdentityAuthSessionDto>(`${IDENTITY_AUTH_PREFIX}/refresh`, request),
    logout: (request) => client.post<void>(`${IDENTITY_AUTH_PREFIX}/logout`, request),
    getCurrentUser: () => client.get<IdentityAuthUserDto>(`${IDENTITY_AUTH_PREFIX}/me`),
  }
}
