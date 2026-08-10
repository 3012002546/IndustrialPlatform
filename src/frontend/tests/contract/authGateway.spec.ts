/**
 * AuthGateway 契约测试:以接口为单位验证,任何实现(Mock / Phase 3 Http)必须通过。
 * Phase 3 新增 HttpAuthGateway 时,追加 runAuthGatewayContractSuite(() => createHttpAuthGateway(...))。
 */

import { beforeEach, describe, expect, it } from 'vitest'

import { ApiError } from '@/api/errors'
import { MOCK_PASSWORD, MOCK_PERMISSIONS, MOCK_USERNAME, createMockAuthGateway } from '@/auth'
import type { AuthGateway } from '@/auth/types'

const VALID_LOGIN = { username: MOCK_USERNAME, password: MOCK_PASSWORD }

/** 运行完整契约套件;factory 每次返回全新网关实例(隔离状态)。 */
export function runAuthGatewayContractSuite(factory: () => AuthGateway): void {
  describe('AuthGateway 契约', () => {
    let gateway: AuthGateway

    beforeEach(() => {
      gateway = factory()
    })

    it('login 成功返回完整会话', async () => {
      const session = await gateway.login(VALID_LOGIN)
      expect(session.accessToken).toBeTruthy()
      expect(session.refreshToken).toBeTruthy()
      expect(Date.parse(session.expiresAt)).not.toBeNaN()
      expect(Date.parse(session.expiresAt)).toBeGreaterThan(Date.now())
      expect(session.user.username).toBe(MOCK_USERNAME)
      expect(session.user.displayName).toContain('演示')
      expect(session.user.permissions).toEqual(expect.arrayContaining([...MOCK_PERMISSIONS]))
    })

    it('login 失败抛出 business 错误且不泄露密码', async () => {
      const outcome = await gateway.login({ username: MOCK_USERNAME, password: 'wrong-pass' }).then(
        () => null,
        (error: unknown) => error,
      )
      expect(outcome).toBeInstanceOf(ApiError)
      const apiError = outcome as ApiError
      expect(apiError.kind).toBe('business')
      expect(apiError.details.code).toBe('AUTH_1001')
      expect(apiError.details.message).not.toContain('wrong-pass')
    })

    it('未知用户名与错误密码返回相同的通用错误', async () => {
      const unknownUser = await gateway.login({ username: 'nobody', password: 'x' }).then(
        () => null,
        (error: unknown) => error,
      )
      const wrongPassword = await gateway.login({ username: MOCK_USERNAME, password: 'x' }).then(
        () => null,
        (error: unknown) => error,
      )
      expect((unknownUser as ApiError).details.message).toBe(
        (wrongPassword as ApiError).details.message,
      )
    })

    it('refresh 用合法刷新令牌返回新会话', async () => {
      const first = await gateway.login(VALID_LOGIN)
      const second = await gateway.refresh(first.refreshToken)
      expect(second.accessToken).toBeTruthy()
      expect(second.refreshToken).toBeTruthy()
      expect(Date.parse(second.expiresAt)).toBeGreaterThanOrEqual(Date.parse(first.expiresAt))
      expect(second.user.username).toBe(MOCK_USERNAME)
    })

    it('refresh 用非法刷新令牌抛 business 错误', async () => {
      const outcome = await gateway.refresh('not-a-valid-token').then(
        () => null,
        (error: unknown) => error,
      )
      expect(outcome).toBeInstanceOf(ApiError)
      expect((outcome as ApiError).kind).toBe('business')
      expect((outcome as ApiError).details.code).toBe('AUTH_1002')
    })

    it('logout 正常完成', async () => {
      await expect(gateway.logout()).resolves.toBeUndefined()
    })

    it('getCurrentUser 返回当前演示用户', async () => {
      const user = await gateway.getCurrentUser()
      expect(user.username).toBe(MOCK_USERNAME)
      expect(user.permissions).toEqual([...MOCK_PERMISSIONS])
    })
  })
}

runAuthGatewayContractSuite(() => createMockAuthGateway({ delayMs: 0 }))
