/**
 * Identity SSO 管理 API 注册表:共享 httpClient(带 401 刷新拦截与令牌注入)在
 * createIndustrialApp(http 模式)装配时注入,页面经 getSsoManagementApi() 取用。
 * mock 模式不注册 —— 管理页面已被权限门禁拦截(演示账号无 identity.sso.* 权限)。
 */

import type { IdentitySsoManagementApi } from './ssoManagementApi'

let ssoManagementApi: IdentitySsoManagementApi | null = null

/** 应用装配(http 模式)注入共享实例。 */
export function registerSsoManagementApi(api: IdentitySsoManagementApi): void {
  ssoManagementApi = api
}

/** 页面取 SSO 管理 API;未注册时抛错(仅 authMode=http 可用)。 */
export function getSsoManagementApi(): IdentitySsoManagementApi {
  if (ssoManagementApi === null) {
    throw new Error('IdentitySsoManagementApi 未注册:仅在 authMode=http 下可用')
  }
  return ssoManagementApi
}
