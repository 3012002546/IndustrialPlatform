/**
 * 全局守卫测试(§12.3):恢复会话、公共/受保护、权限、终端分流、标题、
 * redirect、刷新、前进/后退与重定向循环。
 */

import { createPinia, setActivePinia } from 'pinia'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { createMemoryHistory, createRouter, type Router } from 'vue-router'

import * as device from '@/device'
import { createMockAuthGateway, setAuthGateway, setCurrentSession, writeAuthSession } from '@/auth'
import type { AuthGateway, AuthSession } from '@/auth/types'
import { useAuthStore } from '@/stores/authStore'
import { installRouterGuards } from '@/router/guards'
import { routes } from '@/router/routes'

const VALID_LOGIN = { username: 'mock.admin', password: 'Mock@123456' }
const ALL_PERMISSIONS = ['platform.home.view', 'platform.pda.view', 'platform.mobile.view']

function makeSession(permissions: string[] = []): AuthSession {
  return {
    accessToken: 'at',
    refreshToken: 'rt',
    expiresAt: new Date(Date.now() + 3_600_000).toISOString(),
    user: {
      userId: 'u1',
      username: 'mock.admin',
      displayName: 'Mock 演示账号',
      tenantId: 't1',
      roles: ['admin'],
      permissions,
    },
  }
}

function gatewayWithPermissions(permissions: string[]): AuthGateway {
  return {
    ...createMockAuthGateway({ delayMs: 0 }),
    login: async () => makeSession(permissions),
  }
}

function buildRouter(): Router {
  const router = createRouter({ history: createMemoryHistory(), routes })
  installRouterGuards(router)
  return router
}

/** router.go() 不返回导航 promise,用 afterEach 监听一次导航完成。 */
function go(router: Router, delta: number): Promise<void> {
  return new Promise((resolve) => {
    const stop = router.afterEach(() => {
      stop()
      resolve()
    })
    router.go(delta)
  })
}

function stubViewport(width: number, hasTouch = false): void {
  vi.spyOn(device, 'getViewportInfo').mockReturnValue({ width, hasTouch })
}

async function login(permissions: string[] = ALL_PERMISSIONS): Promise<void> {
  setAuthGateway(gatewayWithPermissions(permissions))
  await useAuthStore().login(VALID_LOGIN)
}

describe('路由守卫 — 会话与公共/受保护', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    setAuthGateway(createMockAuthGateway({ delayMs: 0 }))
    sessionStorage.clear()
    localStorage.clear()
    setCurrentSession(null)
    document.title = ''
  })

  afterEach(() => {
    vi.restoreAllMocks()
  })

  it('未登录访问受保护路由 → 跳登录并携带站内 redirect', async () => {
    stubViewport(1280)
    const router = buildRouter()
    await router.push('/pc/home')
    expect(router.currentRoute.value.name).toBe('login')
    expect(router.currentRoute.value.query.redirect).toBe('/pc/home')
  })

  it('未登录访问公共路由 /login 与 /403 不被拦截', async () => {
    stubViewport(1280)
    const router = buildRouter()
    await router.push('/login')
    expect(router.currentRoute.value.name).toBe('login')
    await router.push('/403')
    expect(router.currentRoute.value.name).toBe('forbidden')
  })

  it('已登录访问登录页 → 重定向到生效终端首页', async () => {
    stubViewport(1280)
    await login()
    const router = buildRouter()
    await router.push('/login')
    expect(router.currentRoute.value.name).toBe('pc-home')
  })

  it('刷新(新 Router 实例)从存储恢复会话后放行受保护路由', async () => {
    stubViewport(1280)
    writeAuthSession(sessionStorage, makeSession(['platform.home.view']))
    const router = buildRouter()
    await router.push('/pc/home')
    expect(router.currentRoute.value.name).toBe('pc-home')
  })
})

describe('路由守卫 — 权限', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    setAuthGateway(createMockAuthGateway({ delayMs: 0 }))
    sessionStorage.clear()
    localStorage.clear()
    setCurrentSession(null)
    document.title = ''
  })

  afterEach(() => {
    vi.restoreAllMocks()
  })

  it('无权限访问受保护路由 → 403', async () => {
    stubViewport(1280)
    await login([])
    const router = buildRouter()
    await router.push('/pc/home')
    expect(router.currentRoute.value.name).toBe('forbidden')
  })

  it('有权限访问受保护路由 → 放行', async () => {
    stubViewport(1280)
    await login(['platform.home.view'])
    const router = buildRouter()
    await router.push('/pc/home')
    expect(router.currentRoute.value.name).toBe('pc-home')
  })
})

describe('路由守卫 — 终端分流', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    setAuthGateway(createMockAuthGateway({ delayMs: 0 }))
    sessionStorage.clear()
    localStorage.clear()
    setCurrentSession(null)
    document.title = ''
  })

  afterEach(() => {
    vi.restoreAllMocks()
  })

  it('根路由按生效终端分流(PC → pc-home)', async () => {
    stubViewport(1280, false)
    await login()
    const router = buildRouter()
    await router.push('/')
    expect(router.currentRoute.value.name).toBe('pc-home')
  })

  it('根路由按生效终端分流(Mobile → mobile-home)', async () => {
    stubViewport(700, false)
    await login()
    const router = buildRouter()
    await router.push('/')
    expect(router.currentRoute.value.name).toBe('mobile-home')
  })

  it('手动覆盖 pda 优先于自动识别', async () => {
    localStorage.setItem('industrial-platform.terminal.override.v1', 'pda')
    stubViewport(1280, false)
    await login()
    const router = buildRouter()
    await router.push('/')
    expect(router.currentRoute.value.name).toBe('pda-home')
  })

  it('显式访问三端路由不自动改写', async () => {
    stubViewport(700, false)
    await login(['platform.home.view'])
    const router = buildRouter()
    await router.push('/pc/home')
    expect(router.currentRoute.value.name).toBe('pc-home')
  })

  it('未知路径 → not-found(公共)', async () => {
    stubViewport(1280)
    const router = buildRouter()
    await router.push('/no-such-page')
    expect(router.currentRoute.value.name).toBe('not-found')
  })

  it('重定向循环:根路由重复导航稳定落到生效终端首页', async () => {
    stubViewport(1280)
    await login()
    const router = buildRouter()
    await router.push('/')
    expect(router.currentRoute.value.name).toBe('pc-home')
    await router.push('/')
    expect(router.currentRoute.value.name).toBe('pc-home')
  })
})

describe('路由守卫 — 历史与标题', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    setAuthGateway(createMockAuthGateway({ delayMs: 0 }))
    sessionStorage.clear()
    localStorage.clear()
    setCurrentSession(null)
    document.title = ''
  })

  afterEach(() => {
    vi.restoreAllMocks()
  })

  it('前进/后退保持守卫决策', async () => {
    stubViewport(1280)
    await login(['platform.home.view', 'platform.pda.view'])
    const router = buildRouter()
    await router.push('/pc/home')
    await router.push('/pda/home')
    expect(router.currentRoute.value.name).toBe('pda-home')
    await go(router, -1)
    await go(router, -1)
    expect(router.currentRoute.value.name).toBe('pc-home')
    await go(router, 1)
    await go(router, 1)
    expect(router.currentRoute.value.name).toBe('pda-home')
  })

  it('导航后设置页面标题', async () => {
    stubViewport(1280)
    const router = buildRouter()
    await router.push('/login')
    expect(document.title).toBe('登录 · Industrial Platform')
  })
})
