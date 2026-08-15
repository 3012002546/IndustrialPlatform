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
import { PERMISSIONS } from '@/permissions'
import { installRouterGuards } from '@/router/guards'
import { routes } from '@/router/routes'
import { useAuthStore } from '@/stores/authStore'
import { useWorkspaceTabsStore } from '@/stores/workspaceTabsStore'
import { createFixedWorkbench, MAX_BUSINESS_TABS } from '@/workspace/identity'
import { writeTabsSnapshot } from '@/workspace/persistence'
import type { WorkspaceRouteCandidate, WorkspaceTab } from '@/workspace'

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
      mustChangePassword: false,
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

function sandboxCandidate(slot: number): WorkspaceRouteCandidate {
  return {
    id: `sandbox:${slot}`,
    title: `沙箱 ${slot}`,
    kind: 'business',
    route: { name: 'workspace-tabs-sandbox', params: {}, query: { slot: String(slot) } },
  }
}

function persistedBusinessTab(slot: number, reloadVersion = 1): WorkspaceTab {
  return { ...sandboxCandidate(slot), kind: 'business' as const, reloadVersion }
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

describe('路由守卫 — 主题绑定(PF-01 §7.4)', () => {
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

  it('已登录导航后绑定用户作用域并应用根节点外观', async () => {
    stubViewport(1280)
    await login(['platform.home.view'])
    const router = buildRouter()
    await router.push('/pc/home')
    expect(router.currentRoute.value.name).toBe('pc-home')
    expect(document.documentElement.getAttribute('data-ip-palette')).toBe('industrial-cyan')
    expect(document.documentElement.getAttribute('data-ip-theme-mode')).toBe('system')
    // 用户偏好快照写入(作用域 u1/t1)
    expect(localStorage.getItem('industrial-platform.ui.preferences.v1:t1:u1')).not.toBeNull()
    expect(localStorage.getItem('industrial-platform.ui.bootstrap.v1')).not.toBeNull()
  })

  it('未登录访问公共路由也完成设备级主题初始化', async () => {
    stubViewport(1280)
    const router = buildRouter()
    await router.push('/login')
    expect(document.documentElement.getAttribute('data-ip-color-mode')).not.toBeNull()
  })
})

describe('路由守卫 — 工作区标签治理(PF-01 §7.9)', () => {
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

  it('固定工作台标签始终存在且激活', async () => {
    stubViewport(1280)
    await login(['platform.home.view'])
    const router = buildRouter()
    await router.push('/pc/home')
    const store = useWorkspaceTabsStore()
    expect(store.tabs.some((t) => t.kind === 'fixed')).toBe(true)
    expect(store.activeTabId).toBe('pc-home')
  })

  it('访问业务路由登记业务标签并激活', async () => {
    stubViewport(1280)
    await login(['platform.home.view'])
    const router = buildRouter()
    await router.push('/pc/dev/workspace-tabs?slot=3')
    const store = useWorkspaceTabsStore()
    // 守卫按 路由名+query 生成稳定标签 id
    expect(store.activeTab?.id).toBe('workspace-tabs-sandbox&q=slot=3')
    expect(store.activeTab?.kind).toBe('business')
    expect(store.activeTab?.route.query.slot).toBe('3')
  })

  it('第 13 个业务标签导航被阻断,pending 保留', async () => {
    stubViewport(1280)
    await login(['platform.home.view'])
    const router = buildRouter()
    await router.push('/pc/home')
    const store = useWorkspaceTabsStore()
    for (let i = 0; i < MAX_BUSINESS_TABS; i += 1) store.requestOpen(sandboxCandidate(i))
    expect(store.businessTabs).toHaveLength(MAX_BUSINESS_TABS)
    await router.push('/pc/dev/workspace-tabs?slot=12')
    // 导航被阻断:仍停留 pc-home,不渲染第 13 个页面
    expect(router.currentRoute.value.name).toBe('pc-home')
    expect(store.businessTabs).toHaveLength(MAX_BUSINESS_TABS)
    expect(store.pending).not.toBeNull()
  })

  it('整页直达被阻断业务路由:兜底固定工作台并保留 pending', async () => {
    stubViewport(1280)
    await login(['platform.home.view'])
    const store = useWorkspaceTabsStore()
    store.bindUser({ tenantId: 't1', userId: 'u1' })
    for (let i = 0; i < MAX_BUSINESS_TABS; i += 1) store.requestOpen(sandboxCandidate(i))
    const router = buildRouter()
    // 首次导航即被阻断:currentRoute 为 START_LOCATION(matched 空),兜底 pc-home 而非空白壳
    await router.push('/pc/dev/workspace-tabs?slot=12')
    expect(router.currentRoute.value.name).toBe('pc-home')
    expect(store.pending).not.toBeNull()
  })

  it('无权限业务标签在导航时被 prune 丢弃', async () => {
    stubViewport(1280)
    await login(['platform.home.view']) // 无 identity.user.view
    const router = buildRouter()
    await router.push('/pc/home')
    const store = useWorkspaceTabsStore()
    store.requestOpen({
      id: 'identity-users',
      title: '用户管理',
      kind: 'business',
      route: { name: 'identity-users', params: {}, query: {} },
      permission: PERMISSIONS.userView,
    })
    expect(store.tabs.some((t) => t.route.name === 'identity-users')).toBe(true)
    await router.push('/pc/dev/workspace-tabs?slot=1')
    expect(store.tabs.some((t) => t.route.name === 'identity-users')).toBe(false)
  })

  it('刷新后从存储恢复业务标签(守卫 bindUser)', async () => {
    stubViewport(1280)
    writeTabsSnapshot(
      localStorage,
      { tenantId: 't1', userId: 'u1' },
      {
        version: 1,
        tabs: [createFixedWorkbench(), persistedBusinessTab(9, 2)],
        activeTabId: 'sandbox:9',
        updatedAt: '2026-08-12T00:00:00.000Z',
      },
    )
    await login(['platform.home.view'])
    const router = buildRouter()
    await router.push('/pc/dev/workspace-tabs?slot=10')
    const store = useWorkspaceTabsStore()
    // 恢复保留 reloadVersion=2 的快照标签(activeTab 已被新导航改写为新开标签)
    const restored = store.tabs.find((t) => t.route.query.slot === '9')
    expect(restored).toBeDefined()
    expect(restored?.reloadVersion).toBe(2)
  })
})
