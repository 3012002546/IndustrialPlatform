/**
 * Identity 认证端点(经 Gateway /identity 前缀,§15):
 * login / refresh / logout / me。只做路径与请求体封装,类型映射见 mapper.ts。
 * 401 刷新重试等横切行为由 httpClient 的 authRefresh 拦截处理(认证路径本身不触发)。
 */

import type { HttpClient } from '@/api/httpClient'

import type {
  IdentityAuthSessionDto,
  IdentityAuthUserDto,
  IdentityBootstrapStatusDto,
  IdentityChangePasswordRequest,
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
  changePassword(request: IdentityChangePasswordRequest): Promise<void>
  /** bootstrap 状态(§29A.5):登录页在 HTTP 模式据此展示初始化未完成诊断。 */
  getBootstrapStatus(): Promise<IdentityBootstrapStatusDto>
}

export function createIdentityAuthApi(client: HttpClient): IdentityAuthApi {
  return {
    login: (request) =>
      client.post<IdentityAuthSessionDto>(`${IDENTITY_AUTH_PREFIX}/login`, request),
    refresh: (request) =>
      client.post<IdentityAuthSessionDto>(`${IDENTITY_AUTH_PREFIX}/refresh`, request),
    logout: (request) => client.post<void>(`${IDENTITY_AUTH_PREFIX}/logout`, request),
    getCurrentUser: () => client.get<IdentityAuthUserDto>(`${IDENTITY_AUTH_PREFIX}/me`),
    changePassword: (request) =>
      client.post<void>(`${IDENTITY_AUTH_PREFIX}/change-password`, request),
    getBootstrapStatus: () => client.get<IdentityBootstrapStatusDto>('/identity/api/v1/bootstrap/status'),
  }
}
