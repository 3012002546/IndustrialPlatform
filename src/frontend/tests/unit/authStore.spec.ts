/**
 * AuthStore 单元测试(§10.2):登录/恢复/刷新/退出/权限/单飞。
 * 验证 password 不进入 Store/Storage;退出时即使 Gateway 失败也清理本地会话。
 */

import { createPinia, setActivePinia } from 'pinia'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import { createApiError } from '@/api/errors'
import {
  AUTH_SESSION_STORAGE_KEY,
  getCurrentSession,
  setAuthGateway,
  setCurrentSession,
  writeAuthSession,
} from '@/auth'
import type { AuthGateway, AuthSession } from '@/auth/types'
import { useAuthStore } from '@/stores/authStore'

const VALID_LOGIN = { username: 'mock.admin', password: 'Mock@123456' }

function makeSession(token = 'at-1', expiresInMs = 3_600_000): AuthSession {
  const issuedAt = Date.now()
  return {
    accessToken: token,
    refreshToken: `rt-${token}`,
    expiresAt: new Date(issuedAt + expiresInMs).toISOString(),
    user: {
      userId: 'u1',
      username: 'mock.admin',
      displayName: 'Mock 演示账号',
      tenantId: 't1',
      roles: ['admin'],
      permissions: ['platform.home.view', 'platform.pda.view'],
    },
  }
}

function createFakeGateway(
  options: { refreshShouldFail?: boolean; logoutShouldFail?: boolean } = {},
) {
  let refreshCalls = 0
  const gateway: AuthGateway = {
    async login(command) {
      if (command.username !== VALID_LOGIN.username || command.password !== VALID_LOGIN.password) {
        throw createApiError('business', '用户名或密码错误', 'corr', { code: 'AUTH_1001' })
      }
      return makeSession('at-login')
    },
    async refresh() {
      refreshCalls += 1
      if (options.refreshShouldFail) {
        throw createApiError('business', '刷新令牌无效', 'corr', { code: 'AUTH_1002' })
      }
      return makeSession(`at-refresh-${refreshCalls}`)
    },
    async logout() {
      if (options.logoutShouldFail) throw new Error('logout failed')
    },
    async getCurrentUser() {
      return makeSession().user
    },
  }
  return { gateway, getRefreshCalls: () => refreshCalls }
}

describe('AuthStore — 登录', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    setAuthGateway(createFakeGateway().gateway)
    sessionStorage.clear()
    setCurrentSession(null)
  })

  afterEach(() => {
    vi.restoreAllMocks()
  })

  it('登录成功提交会话并持久化', async () => {
    const store = useAuthStore()
    await store.login(VALID_LOGIN)
    expect(store.isAuthenticated).toBe(true)
    expect(store.user?.username).toBe('mock.admin')
    expect(sessionStorage.getItem(AUTH_SESSION_STORAGE_KEY)).not.toBeNull()
    expect(getCurrentSession()?.accessToken).toBe(store.session?.accessToken)
  })

  it('password 不进入 Store 与 Storage', async () => {
    const store = useAuthStore()
    await store.login(VALID_LOGIN)
    expect(JSON.stringify(store.session)).not.toContain('Mock@123456')
    expect(JSON.stringify(store.session)).not.toContain('password')
    const stored = sessionStorage.getItem(AUTH_SESSION_STORAGE_KEY) ?? ''
    expect(stored).not.toContain('Mock@123456')
    expect(stored).not.toContain('password')
  })

  it('登录失败不改变状态且不写入存储', async () => {
    const store = useAuthStore()
    await expect(store.login({ username: 'mock.admin', password: 'wrong' })).rejects.toMatchObject({
      kind: 'business',
      details: { code: 'AUTH_1001' },
    })
    expect(store.isAuthenticated).toBe(false)
    expect(sessionStorage.getItem(AUTH_SESSION_STORAGE_KEY)).toBeNull()
  })
})

describe('AuthStore — 恢复', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    setAuthGateway(createFakeGateway().gateway)
    sessionStorage.clear()
    setCurrentSession(null)
  })

  afterEach(() => {
    vi.restoreAllMocks()
  })

  it('从有效存储恢复会话', async () => {
    writeAuthSession(sessionStorage, makeSession('at-stored'))
    const store = useAuthStore()
    await store.restore()
    expect(store.isAuthenticated).toBe(true)
    expect(store.session?.accessToken).toBe('at-stored')
  })

  it('损坏或过期存储被清理且不恢复', async () => {
    sessionStorage.setItem(AUTH_SESSION_STORAGE_KEY, '{broken')
    const store = useAuthStore()
    await store.restore()
    expect(store.isAuthenticated).toBe(false)
    expect(sessionStorage.getItem(AUTH_SESSION_STORAGE_KEY)).toBeNull()
  })

  it('过期会话被清理且不恢复', async () => {
    writeAuthSession(sessionStorage, makeSession('at-expired', -1000))
    const store = useAuthStore()
    await store.restore()
    expect(store.isAuthenticated).toBe(false)
    expect(sessionStorage.getItem(AUTH_SESSION_STORAGE_KEY)).toBeNull()
  })

  it('并发 restore 单飞:只读取一次存储', async () => {
    writeAuthSession(sessionStorage, makeSession('at-single'))
    const getItemSpy = vi.spyOn(Storage.prototype, 'getItem')
    const store = useAuthStore()
    await Promise.all([store.restore(), store.restore()])
    expect(getItemSpy).toHaveBeenCalledTimes(1)
    expect(store.isAuthenticated).toBe(true)
  })
})

describe('AuthStore — 刷新', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    sessionStorage.clear()
    setCurrentSession(null)
  })

  afterEach(() => {
    vi.restoreAllMocks()
  })

  it('刷新成功更新会话与存储', async () => {
    const { gateway } = createFakeGateway()
    setAuthGateway(gateway)
    const store = useAuthStore()
    await store.login(VALID_LOGIN)
    await store.refresh()
    expect(store.session?.accessToken).toBe('at-refresh-1')
    const stored = sessionStorage.getItem(AUTH_SESSION_STORAGE_KEY) ?? ''
    expect(stored).toContain('at-refresh-1')
  })

  it('无会话时刷新抛 unauthorized', async () => {
    setAuthGateway(createFakeGateway().gateway)
    const store = useAuthStore()
    await expect(store.refresh()).rejects.toMatchObject({ kind: 'unauthorized' })
  })

  it('刷新失败清理本地会话', async () => {
    setAuthGateway(createFakeGateway({ refreshShouldFail: true }).gateway)
    const store = useAuthStore()
    await store.login(VALID_LOGIN)
    await expect(store.refresh()).rejects.toMatchObject({ details: { code: 'AUTH_1002' } })
    expect(store.isAuthenticated).toBe(false)
    expect(sessionStorage.getItem(AUTH_SESSION_STORAGE_KEY)).toBeNull()
    expect(getCurrentSession()).toBeNull()
  })

  it('并发 refresh 单飞:只调用一次网关', async () => {
    const { gateway, getRefreshCalls } = createFakeGateway()
    setAuthGateway(gateway)
    const store = useAuthStore()
    await store.login(VALID_LOGIN)
    await Promise.all([store.refresh(), store.refresh()])
    expect(getRefreshCalls()).toBe(1)
    expect(store.session?.accessToken).toBe('at-refresh-1')
  })
})

describe('AuthStore — 退出与权限', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    sessionStorage.clear()
    setCurrentSession(null)
  })

  afterEach(() => {
    vi.restoreAllMocks()
  })

  it('退出清理会话与存储', async () => {
    setAuthGateway(createFakeGateway().gateway)
    const store = useAuthStore()
    await store.login(VALID_LOGIN)
    await store.logout()
    expect(store.isAuthenticated).toBe(false)
    expect(sessionStorage.getItem(AUTH_SESSION_STORAGE_KEY)).toBeNull()
    expect(getCurrentSession()).toBeNull()
  })

  it('Gateway 退出失败也清理本地会话', async () => {
    setAuthGateway(createFakeGateway({ logoutShouldFail: true }).gateway)
    const store = useAuthStore()
    await store.login(VALID_LOGIN)
    await expect(store.logout()).resolves.toBeUndefined()
    expect(store.isAuthenticated).toBe(false)
    expect(sessionStorage.getItem(AUTH_SESSION_STORAGE_KEY)).toBeNull()
  })

  it('hasPermission 按权限判断', async () => {
    setAuthGateway(createFakeGateway().gateway)
    const store = useAuthStore()
    expect(store.hasPermission('platform.home.view')).toBe(false)
    await store.login(VALID_LOGIN)
    expect(store.hasPermission('platform.home.view')).toBe(true)
    expect(store.hasPermission('platform.mobile.view')).toBe(false)
  })
})
