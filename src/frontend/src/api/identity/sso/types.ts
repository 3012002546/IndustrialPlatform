/**
 * SSO 端点线上契约(与后端 Contracts 对齐,§26.4/§26.5/§26.9):
 * 全部 camelCase;标识一律为 NId 字符串,不含数据库 Id。
 * 一次性票据(ticket)只在回调跳转 Query 中出现 60 秒,交换即消费;
 * 浏览器 SSO 会话句柄只存在于 HttpOnly Cookie,绝不出现在响应体。
 */

import type { IdentityAuthSessionDto } from '../types'

/** 发现结果 Provider 摘要(§26.4);Protocol 为协议枚举名。 */
export interface SsoDiscoveryProviderDto {
  providerNId: string
  name: string
  protocol: string
  autoRedirect: boolean
}

/**
 * 授权开始响应(§26.4):
 * - reused=true 时 ticket 非空,前端直接交换;
 * - 否则 needsSelection=true 时进入 Provider 选择(providers),或携带 authorizeUri 跳转 IdP。
 */
export interface SsoBeginResponseDto {
  reused: boolean
  ticket: string | null
  /** 服务端已校验的站内回跳地址。 */
  returnUrl: string | null
  needsSelection: boolean
  providerNId: string | null
  authorizeUri: string | null
  providers: SsoDiscoveryProviderDto[] | null
}

/** 一次性票据交换请求(§26.5)。 */
export interface SsoExchangeRequestDto {
  ticket: string
}

/** 票据交换响应(§26.5):完整认证会话与已校验站内回跳地址(句柄不回传)。 */
export interface SsoExchangeResponseDto {
  session: IdentityAuthSessionDto
  returnUrl: string | null
}

/** 登出请求(§26.9);postLogoutRedirectUri 必须为站内相对地址或已注册端点。 */
export interface SsoLogoutRequestDto {
  clientId?: string | null
  postLogoutRedirectUri?: string | null
  refreshToken?: string | null
}

/** 登出响应:IdpRedirectUri 非空时前端需跳转 IdP 完成联邦登出。 */
export interface SsoLogoutResponseDto {
  idpRedirectUri: string | null
  isFederated: boolean
}
