/**
 * Mock 认证网关(Phase 2):验证固定演示账号,生成带模拟延迟的会话。
 * 只用于本地开发/测试;生产构建由运行配置禁止启用 mock(见 runtimeConfig)。
 */

import { createCorrelationId } from '@/api/correlation'
import { createApiError } from '@/api/errors'

import type { AuthGateway, AuthSession, AuthUser, LoginCommand } from './types'

/** 开发演示账号(仅本地开发演示,不得描述为生产管理员)。 */
export const MOCK_USERNAME = 'mock.admin'
export const MOCK_PASSWORD = 'Mock@123456'

/** 第一批演示权限。 */
export const MOCK_PERMISSIONS = [
  'platform.home.view',
  'platform.pda.view',
  'platform.mobile.view',
] as const

export const MOCK_USER: AuthUser = {
  userId: 'mock-admin-0001',
  username: MOCK_USERNAME,
  displayName: 'Mock 演示账号',
  tenantId: 'dev-tenant',
  roles: ['admin'],
  permissions: [...MOCK_PERMISSIONS],
  mustChangePassword: false,
}

export interface MockAuthGatewayOptions {
  /** 模拟网络延迟(毫秒);测试传 0 保证确定性。 */
  delayMs?: number
  /** 时间源;测试可注入固定时钟。 */
  now?: () => number
  /** 会话有效期(毫秒),默认 1 小时。 */
  sessionDurationMs?: number
}

/** 创建 Mock 认证网关。 */
export function createMockAuthGateway(options: MockAuthGatewayOptions = {}): AuthGateway {
  const delayMs = options.delayMs ?? 0
  const now = options.now ?? Date.now
  const sessionDurationMs = options.sessionDurationMs ?? 60 * 60 * 1000

  async function settle<T>(value: T): Promise<T> {
    if (delayMs > 0) await new Promise((resolve) => setTimeout(resolve, delayMs))
    return value
  }

  function businessError(code: string, message: string): never {
    throw createApiError('business', message, createCorrelationId(), { code })
  }

  function buildSession(): AuthSession {
    const issuedAt = now()
    const expiresAt = new Date(issuedAt + sessionDurationMs).toISOString()
    const nonce = Math.random().toString(36).slice(2, 10)
    return {
      accessToken: `mock.at.${MOCK_USERNAME}.${issuedAt}.${nonce}`,
      refreshToken: `mock.rt.${MOCK_USERNAME}.${issuedAt}.${nonce}`,
      expiresAt,
      user: { ...MOCK_USER, permissions: [...MOCK_PERMISSIONS] },
    }
  }

  return {
    async login(command: LoginCommand): Promise<AuthSession> {
      if (command.username !== MOCK_USERNAME || command.password !== MOCK_PASSWORD) {
        businessError('AUTH_1001', '用户名或密码错误')
      }
      return settle(buildSession())
    },
    async refresh(refreshToken: string): Promise<AuthSession> {
      if (!refreshToken.startsWith('mock.rt.')) {
        businessError('AUTH_1002', '刷新令牌无效或已过期')
      }
      return settle(buildSession())
    },
    async logout(): Promise<void> {
      await settle(undefined)
    },
    async getCurrentUser(): Promise<AuthUser> {
      return settle({ ...MOCK_USER, permissions: [...MOCK_PERMISSIONS] })
    },
    async changePassword(currentPassword: string, newPassword: string): Promise<void> {
      if (currentPassword !== MOCK_PASSWORD) {
        businessError('AUTH_1003', '当前密码错误')
      }
      if (newPassword === currentPassword) {
        businessError('AUTH_1004', '新密码不能与当前密码相同')
      }
      await settle(undefined)
    },
    async getBootstrapStatus() {
      return settle({ state: 'Ready', adminExists: true } as const)
    },
  }
}
