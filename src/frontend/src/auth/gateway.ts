/**
 * 认证边界装配点:应用启动时按运行配置注册网关(Phase 2 Mock / Phase 3 Http)。
 * Store 通过 getAuthGateway() 消费,替换网关不改变页面与 Store 的消费模型。
 */

import { createMockAuthGateway } from './mockAuthGateway'
import type { AuthGateway, AuthSession } from './types'

let currentGateway: AuthGateway = createMockAuthGateway()
let currentSession: AuthSession | null = null

export function setAuthGateway(gateway: AuthGateway): void {
  currentGateway = gateway
}

export function getAuthGateway(): AuthGateway {
  return currentGateway
}

/** 会话令牌镜像:供 HTTP 层 getToken 同步读取(Authorization 注入),由 AuthStore 维护。 */
export function setCurrentSession(session: AuthSession | null): void {
  currentSession = session
}

export function getCurrentSession(): AuthSession | null {
  return currentSession
}
