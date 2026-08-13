/**
 * 真实 HTTP 认证网关(Phase 3):经 Gateway 调用 Identity 认证端点(§15)。
 * 与 Mock 共享 AuthGateway 契约(见 tests/contract/authGateway.spec.ts)。
 *
 * 边界职责:
 * - 登录失败统一归一为 business(携带后端 code/message),页面据此显示「用户名或密码错误」;
 *   refresh/me 失败保留 unauthorized(会话失效语义由 AuthStore 统一处理)。
 * - password/token 不落日志:底层 httpClient 已按敏感键脱敏(§8.3/§19)。
 * - logout 读取当前会话刷新令牌(令牌镜像);未登录时幂等跳过。
 */

import { ApiError, createApiError } from '@/api/errors'
import type { IdentityAuthApi } from '@/api/identity/identityApi'
import { mapAuthSession, mapAuthUser } from '@/api/identity/mapper'
import type { AuthGateway, AuthSession, AuthUser, LoginCommand } from './types'

export interface HttpAuthGatewayDeps {
  api: IdentityAuthApi
  /** 读取当前会话刷新令牌(令牌镜像);null 表示未登录,登出幂等跳过。 */
  getRefreshToken(): string | null
}

/** 把传输层 401(会话失效语义)归还为登录的业务拒绝,保留后端 code/message。 */
function normalizeLoginError(error: unknown): unknown {
  if (error instanceof ApiError && error.kind === 'unauthorized') {
    return createApiError('business', error.message, error.details.correlationId, {
      ...(error.details.status === undefined ? {} : { status: error.details.status }),
      ...(error.details.code === undefined ? {} : { code: error.details.code }),
      ...(error.details.traceId === undefined ? {} : { traceId: error.details.traceId }),
    })
  }
  return error
}

export function createHttpAuthGateway(deps: HttpAuthGatewayDeps): AuthGateway {
  async function login(command: LoginCommand): Promise<AuthSession> {
    try {
      const dto = await deps.api.login({
        loginName: command.username,
        password: command.password,
      })
      return mapAuthSession(dto)
    } catch (error) {
      throw normalizeLoginError(error)
    }
  }

  async function refresh(refreshToken: string): Promise<AuthSession> {
    const dto = await deps.api.refresh({ refreshToken })
    return mapAuthSession(dto)
  }

  async function logout(): Promise<void> {
    const refreshToken = deps.getRefreshToken()
    if (refreshToken === null) return
    await deps.api.logout({ refreshToken })
  }

  async function getCurrentUser(): Promise<AuthUser> {
    return mapAuthUser(await deps.api.getCurrentUser())
  }

  return { login, refresh, logout, getCurrentUser }
}
