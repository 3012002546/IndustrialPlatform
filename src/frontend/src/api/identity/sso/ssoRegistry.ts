/**
 * Identity SSO API 注册表:共享 httpClient(带 401 刷新拦截、令牌注入与
 * withCredentials)在 createIndustrialApp(http 模式)装配时注入,页面经 getSsoApi() 取用。
 * mock 模式不注册 —— SSO 登录依赖真实 Identity 网关,演示账号无企业登录源。
 */

import type { IdentitySsoApi } from './ssoApi'

let ssoApi: IdentitySsoApi | null = null

/** 应用装配(http 模式)注入共享实例。 */
export function registerSsoApi(api: IdentitySsoApi): void {
  ssoApi = api
}

/** 页面取 SSO API;未注册时抛错(仅 authMode=http 可用)。 */
export function getSsoApi(): IdentitySsoApi {
  if (ssoApi === null) {
    throw new Error('IdentitySsoApi 未注册:仅在 authMode=http 下可用')
  }
  return ssoApi
}
