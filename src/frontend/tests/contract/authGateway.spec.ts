/**
 * AuthGateway 契约测试:以接口为单位验证,任何实现(Mock / Phase 3 Http)必须通过。
 * 期望值(账号、权限、错误码)按实现参数化——契约关注行为形状
 * (登录返回完整会话、失败抛类型化错误且不泄露密码、刷新旋转、登出幂等),
 * 不要求两种实现返回相同字面量。
 */

import { http, HttpResponse } from 'msw'
import { afterAll, afterEach, beforeAll, beforeEach, describe, expect, it } from 'vitest'

import { ApiError } from '@/api/errors'
import { createHttpClient } from '@/api/httpClient'
import { createIdentityAuthApi } from '@/api/identity/identityApi'
import type { IdentityAuthSessionDto } from '@/api/identity/types'
import {
  MOCK_PASSWORD,
  MOCK_PERMISSIONS,
  MOCK_USERNAME,
  createHttpAuthGateway,
  createMockAuthGateway,
} from '@/auth'
import type { ApiErrorKind } from '@/types/api'
import type { AuthGateway, AuthUser, LoginCommand } from '@/auth/types'

import { server } from '../fixtures/mswServer'

/** 实现相关的期望值;两个实现各自声明,契约套件据此断言。 */
export interface AuthGatewayErrorExpectation {
  kind: ApiErrorKind
  code: string
}

export interface AuthGatewayContractExpectations {
  validLogin: LoginCommand
  username: string
  /** 显示名的期望子串(实现可用不同全名)。 */
  displayNameSubstring: string
  permissions: readonly string[]
  invalidCredentials: AuthGatewayErrorExpectation
  invalidRefresh: AuthGatewayErrorExpectation
}

/** 运行完整契约套件;factory 每次返回全新网关实例(隔离状态)。 */
export function runAuthGatewayContractSuite(
  factory: () => AuthGateway,
  expectations: AuthGatewayContractExpectations,
): void {
  describe('AuthGateway 契约', () => {
    let gateway: AuthGateway

    beforeEach(() => {
      gateway = factory()
    })

    it('login 成功返回完整会话', async () => {
      const session = await gateway.login(expectations.validLogin)
      expect(session.accessToken).toBeTruthy()
      expect(session.refreshToken).toBeTruthy()
      expect(Date.parse(session.expiresAt)).not.toBeNaN()
      expect(Date.parse(session.expiresAt)).toBeGreaterThan(Date.now())
      expect(session.user.username).toBe(expectations.username)
      expect(session.user.displayName).toContain(expectations.displayNameSubstring)
      expect(session.user.permissions).toEqual(
        expect.arrayContaining([...expectations.permissions]),
      )
    })

    it('login 失败抛出业务错误且不泄露密码', async () => {
      const outcome = await gateway
        .login({ username: expectations.username, password: 'wrong-pass' })
        .then(
          () => null,
          (error: unknown) => error,
        )
      expect(outcome).toBeInstanceOf(ApiError)
      const apiError = outcome as ApiError
      expect(apiError.kind).toBe(expectations.invalidCredentials.kind)
      expect(apiError.details.code).toBe(expectations.invalidCredentials.code)
      expect(apiError.details.message).not.toContain('wrong-pass')
    })

    it('未知用户名与错误密码返回相同的通用错误', async () => {
      const unknownUser = await gateway.login({ username: 'nobody', password: 'x' }).then(
        () => null,
        (error: unknown) => error,
      )
      const wrongPassword = await gateway
        .login({ username: expectations.username, password: 'x' })
        .then(
          () => null,
          (error: unknown) => error,
        )
      expect((unknownUser as ApiError).details.message).toBe(
        (wrongPassword as ApiError).details.message,
      )
    })

    it('refresh 用合法刷新令牌返回新会话', async () => {
      const first = await gateway.login(expectations.validLogin)
      const second = await gateway.refresh(first.refreshToken)
      expect(second.accessToken).toBeTruthy()
      expect(second.refreshToken).toBeTruthy()
      expect(Date.parse(second.expiresAt)).toBeGreaterThanOrEqual(Date.parse(first.expiresAt))
      expect(second.user.username).toBe(expectations.username)
    })

    it('refresh 用非法刷新令牌抛错误', async () => {
      const outcome = await gateway.refresh('not-a-valid-token').then(
        () => null,
        (error: unknown) => error,
      )
      expect(outcome).toBeInstanceOf(ApiError)
      const apiError = outcome as ApiError
      expect(apiError.kind).toBe(expectations.invalidRefresh.kind)
      expect(apiError.details.code).toBe(expectations.invalidRefresh.code)
    })

    it('logout 正常完成', async () => {
      await expect(gateway.logout()).resolves.toBeUndefined()
    })

    it('getCurrentUser 返回当前用户', async () => {
      const user: AuthUser = await gateway.getCurrentUser()
      expect(user.username).toBe(expectations.username)
      expect(user.permissions).toEqual([...expectations.permissions])
    })
  })
}

// ---------------------------------------------------------------------------
// Mock 实现:Phase 2 演示账号。
// ---------------------------------------------------------------------------

runAuthGatewayContractSuite(() => createMockAuthGateway({ delayMs: 0 }), {
  validLogin: { username: MOCK_USERNAME, password: MOCK_PASSWORD },
  username: MOCK_USERNAME,
  displayNameSubstring: '演示',
  permissions: [...MOCK_PERMISSIONS],
  invalidCredentials: { kind: 'business', code: 'AUTH_1001' },
  invalidRefresh: { kind: 'business', code: 'AUTH_1002' },
})

// ---------------------------------------------------------------------------
// Http 实现:经 Gateway 调 Identity 端点,MSW 模拟后端(§15 契约形状)。
// ---------------------------------------------------------------------------

const BASE = 'http://localhost:5080'
const HTTP_USERNAME = 'alice.ops'
const HTTP_PASSWORD = 'Alice@123456'
const HTTP_PERMISSIONS = ['platform.home.view', 'platform.pda.view', 'platform.mobile.view']

const userDto = (): {
  userNId: string
  loginName: string
  name: string
  tenantNId: string
  roleNIds: string[]
  permissionNIds: string[]
  mustChangePassword: boolean
} => ({
  userNId: 'usr-alice-001',
  loginName: HTTP_USERNAME,
  name: 'Alice Ops',
  tenantNId: 'tnt-dev-001',
  roleNIds: ['role-ops-001'],
  permissionNIds: [...HTTP_PERMISSIONS],
  mustChangePassword: false,
})

let issued = 0
function sessionDto(): IdentityAuthSessionDto {
  issued += 1
  const now = Date.now()
  return {
    accessToken: `at.${issued}`,
    refreshToken: `rt.${issued}`,
    expiresAt: new Date(now + 3600_000).toISOString(),
    user: userDto(),
  }
}

function okEnvelope(data: unknown): Record<string, unknown> {
  return { success: true, code: '200', message: 'success', data }
}

function failEnvelope(code: string, message: string, status: number) {
  return HttpResponse.json({ success: false, code, message, data: null }, { status })
}

function installHttpIdentityHandlers(): void {
  server.use(
    http.post(`${BASE}/identity/api/v1/auth/login`, async ({ request }) => {
      const body = (await request.json()) as { loginName?: string; password?: string }
      if (body.loginName !== HTTP_USERNAME || body.password !== HTTP_PASSWORD) {
        return failEnvelope('ID_AUTH_INVALID_CREDENTIALS', '用户名或密码错误。', 401)
      }
      return HttpResponse.json(okEnvelope(sessionDto()))
    }),
    http.post(`${BASE}/identity/api/v1/auth/refresh`, async ({ request }) => {
      const body = (await request.json()) as { refreshToken?: string }
      if (typeof body.refreshToken !== 'string' || !body.refreshToken.startsWith('rt.')) {
        return failEnvelope('ID_AUTH_REFRESH_INVALID', '刷新令牌无效或已过期，请重新登录。', 401)
      }
      return HttpResponse.json(okEnvelope(sessionDto()))
    }),
    http.post(`${BASE}/identity/api/v1/auth/logout`, () => HttpResponse.json(okEnvelope(null))),
    http.get(`${BASE}/identity/api/v1/auth/me`, () => HttpResponse.json(okEnvelope(userDto()))),
  )
}

function createHttpAuthGatewayForTest(): AuthGateway {
  const client = createHttpClient({
    baseUrl: BASE,
    timeoutMs: 1000,
    getToken: () => 'at.current',
    getCorrelationId: () => 'corr-http',
  })
  return createHttpAuthGateway({
    api: createIdentityAuthApi(client),
    getRefreshToken: () => 'rt.current',
  })
}

describe('HttpAuthGateway 契约(经 MSW 调 Identity)', () => {
  beforeAll(() => server.listen({ onUnhandledRequest: 'error' }))
  afterEach(() => server.resetHandlers())
  afterAll(() => server.close())

  beforeEach(() => installHttpIdentityHandlers())

  runAuthGatewayContractSuite(() => createHttpAuthGatewayForTest(), {
    validLogin: { username: HTTP_USERNAME, password: HTTP_PASSWORD },
    username: HTTP_USERNAME,
    displayNameSubstring: 'Alice',
    permissions: [...HTTP_PERMISSIONS],
    // 登录失败:网关把传输层 401 归一为 business,页面据此显示「用户名或密码错误」。
    invalidCredentials: { kind: 'business', code: 'ID_AUTH_INVALID_CREDENTIALS' },
    // 刷新失败:保留 unauthorized(会话失效语义由 AuthStore 统一处理)。
    invalidRefresh: { kind: 'unauthorized', code: 'ID_AUTH_REFRESH_INVALID' },
  })
})
