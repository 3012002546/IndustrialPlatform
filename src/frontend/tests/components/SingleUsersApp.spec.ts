import { createPinia, setActivePinia } from 'pinia'
import { mount } from '@vue/test-utils'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { defineComponent } from 'vue'
import { PERMISSIONS } from '@/permissions'
import { AUTH_SESSION_HTTP_STORAGE_KEY } from '@/auth/sessionStore'
import { createMockAuthGateway, setAuthGateway } from '@/auth'
import SingleUsersApp from '@/pages/pc/identity/SingleUsersApp.vue'

const { getCurrentUser, exchange } = vi.hoisted(() => ({
  getCurrentUser: vi.fn(),
  exchange: vi.fn(),
}))
vi.mock('@/api/httpClient', () => ({ createHttpClient: () => ({}) }))
vi.mock('@/api/identity/identityApi', () => ({
  createIdentityAuthApi: () => ({ getCurrentUser }),
}))
vi.mock('@/api/identity/sso', () => ({ getSsoApi: () => ({ exchange }) }))
vi.mock('@/stores/themeStore', () => ({
  useThemeStore: () => ({ initialize: async () => {}, bindUser: async () => {} }),
}))
vi.mock('@/pages/pc/identity/IdentityUsersPage.vue', () => ({
  default: defineComponent({ template: '<div data-testid="identity-users-page">用户管理</div>' }),
}))

const user = {
  userNId: 'USR-1',
  loginName: 'admin',
  name: '管理员',
  tenantNId: 'TEN-1',
  roleNIds: [],
  permissionNIds: [PERMISSIONS.userView],
  mustChangePassword: false,
}

function jwt(): string {
  return `header.${btoa(JSON.stringify({ exp: Math.floor(Date.now() / 1000) + 3600 }))}.signature`
}

async function render(query: string) {
  window.history.replaceState({}, '', `/pc/identity/users?mode=single${query}`)
  const wrapper = mount(SingleUsersApp, {
    global: {
      plugins: [createPinia()],
      stubs: { ElConfigProvider: { template: '<div><slot /></div>' } },
    },
  })
  await vi.waitFor(() => expect(wrapper.text()).not.toContain('正在验证登录'))
  return wrapper
}

describe('用户管理单页入口', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    sessionStorage.clear()
    vi.stubEnv('VITE_AUTH_MODE', 'http')
    getCurrentUser.mockResolvedValue(user)
  })
  afterEach(() => {
    vi.unstubAllEnvs()
    vi.clearAllMocks()
  })

  it('validates an access token and renders the original page without storing it in the platform session', async () => {
    const wrapper = await render(`&token=${encodeURIComponent(jwt())}`)
    expect(wrapper.find('[data-testid="identity-users-page"]').exists()).toBe(true)
    expect(window.location.search).toBe('?mode=single')
    expect(sessionStorage.getItem('industrial-platform.auth.single.v1')).toContain('single-access')
    expect(sessionStorage.getItem(AUTH_SESSION_HTTP_STORAGE_KEY)).toBeNull()
    wrapper.unmount()
  })

  it('exchanges a ticket, preserving a separate single-page session', async () => {
    exchange.mockResolvedValue({
      returnUrl: '/pc/identity/users?mode=single',
      session: {
        accessToken: 'access',
        refreshToken: 'refresh',
        expiresAt: new Date(Date.now() + 3600000).toISOString(),
        user,
      },
    })
    const wrapper = await render('&ticket=once')
    expect(wrapper.find('[data-testid="identity-users-page"]').exists()).toBe(true)
    expect(sessionStorage.getItem('industrial-platform.auth.single.v1')).toContain('refresh')
    expect(sessionStorage.getItem(AUTH_SESSION_HTTP_STORAGE_KEY)).toBeNull()
    wrapper.unmount()
  })

  it('rejects conflicting credentials before showing the page', async () => {
    const wrapper = await render('&token=bad&ticket=once')
    expect(wrapper.find('[role="alert"]').text()).toContain('参数无效')
    expect(wrapper.find('[data-testid="identity-users-page"]').exists()).toBe(false)
    expect(exchange).not.toHaveBeenCalled()
    wrapper.unmount()
  })

  it('rejects a ticket issued for another page', async () => {
    exchange.mockResolvedValue({
      returnUrl: '/pc/identity/roles',
      session: {
        accessToken: 'access',
        refreshToken: 'refresh',
        expiresAt: new Date(Date.now() + 3600000).toISOString(),
        user,
      },
    })
    const wrapper = await render('&ticket=wrong-target')
    expect(wrapper.find('[role="alert"]').text()).toContain('目标页面不匹配')
    expect(wrapper.find('[data-testid="identity-users-page"]').exists()).toBe(false)
    wrapper.unmount()
  })

  it('rejects a token without user-management permission', async () => {
    getCurrentUser.mockResolvedValue({ ...user, permissionNIds: [] })
    const wrapper = await render(`&token=${encodeURIComponent(jwt())}`)
    expect(wrapper.find('[role="alert"]').text()).toContain('没有用户管理查看权限')
    expect(wrapper.find('[data-testid="identity-users-page"]').exists()).toBe(false)
    wrapper.unmount()
  })

  it('rechecks permissions when restoring a single-page session', async () => {
    const first = await render(`&token=${encodeURIComponent(jwt())}`)
    expect(first.find('[data-testid="identity-users-page"]').exists()).toBe(true)
    first.unmount()
    setAuthGateway({
      ...createMockAuthGateway({ delayMs: 0 }),
      getCurrentUser: async () => ({
        userId: 'USR-1',
        username: 'admin',
        displayName: '管理员',
        tenantId: 'TEN-1',
        roles: [],
        permissions: [],
        mustChangePassword: false,
      }),
    })
    const restored = await render('')
    expect(restored.find('[role="alert"]').text()).toContain('没有用户管理查看权限')
    expect(restored.find('[data-testid="identity-users-page"]').exists()).toBe(false)
    restored.unmount()
  })
})
