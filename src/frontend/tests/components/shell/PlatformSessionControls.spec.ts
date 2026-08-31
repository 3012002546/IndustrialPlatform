import { flushPromises, mount, type VueWrapper } from '@vue/test-utils'
import ElementPlus, { ElMessageBox } from 'element-plus'
import { createPinia, setActivePinia } from 'pinia'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { createMemoryHistory, createRouter } from 'vue-router'

import PlatformSessionControls from '@/components/shell/PlatformSessionControls.vue'
import { PERMISSIONS } from '@/permissions'
import { useAuthStore } from '@/stores/authStore'
import { persistAuthSession } from '../../fixtures/session'

const { fakeApi } = vi.hoisted(() => ({
  fakeApi: {
    listActiveSessions: vi.fn(),
    revokeSession: vi.fn(),
  },
}))

vi.mock('@/api/identity/managementRegistry', () => ({
  getManagementApi: () => fakeApi,
}))

const wrappers: VueWrapper[] = []
let activeRouter: ReturnType<typeof createRouter> | undefined

async function mountControls(permissions: string[]): Promise<VueWrapper> {
  const pinia = createPinia()
  setActivePinia(pinia)
  sessionStorage.clear()
  persistAuthSession(permissions)
  await useAuthStore().restore()
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/', name: 'home', component: { template: '<div />' } },
      { path: '/login', name: 'login', component: { template: '<div />' } },
    ],
  })
  await router.push('/')
  await router.isReady()
  activeRouter = router
  const wrapper = mount(PlatformSessionControls, {
    attachTo: document.body,
    global: { plugins: [pinia, router, ElementPlus] },
  })
  wrappers.push(wrapper)
  return wrapper
}

describe('PlatformSessionControls', () => {
  beforeEach(() => {
    vi.stubEnv('VITE_AUTH_MODE', 'mock')
    fakeApi.listActiveSessions.mockReset()
    fakeApi.revokeSession.mockReset()
  })

  afterEach(() => {
    wrappers.splice(0).forEach((wrapper) => wrapper.unmount())
    document.body.innerHTML = ''
    vi.restoreAllMocks()
    vi.unstubAllEnvs()
    activeRouter = undefined
  })

  it('opens the notification empty state without a backend request', async () => {
    const wrapper = await mountControls([])

    const trigger = wrapper.get('[data-testid="shell-notifications"]')
    expect(trigger.attributes('disabled')).toBeUndefined()
    expect(wrapper.get('[data-testid="shell-notifications"]').attributes('title')).toContain(
      '待实现',
    )
    expect(wrapper.find('[data-testid="online-users-button"]').exists()).toBe(false)

    await trigger.trigger('click')
    await flushPromises()

    const panel = document.body.querySelector('[data-testid="shell-notification-panel"]')
    expect(panel).not.toBeNull()
    expect(panel?.textContent).toContain('尚未接入')
    expect(fakeApi.listActiveSessions).not.toHaveBeenCalled()

    panel?.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }))
    await flushPromises()
    expect(document.body.querySelector('[data-testid="shell-notification-panel"]')).toBeNull()
    expect(document.activeElement).toBe(trigger.element)
  })

  it('renders a disabled PF04 send-message action that cannot issue a request', async () => {
    fakeApi.listActiveSessions.mockResolvedValue({
      items: [
        {
          sessionNId: 'SES-1',
          userNId: 'USR-1',
          loginName: 'operator',
          name: '操作员',
          loginOn: '2026-08-30T01:00:00Z',
          lastRefreshedOn: '2026-08-30T01:30:00Z',
          expiresOn: '2026-08-31T01:00:00Z',
          isCurrent: false,
        },
      ],
      total: 1,
      pageIndex: 1,
      pageSize: 100,
    })
    const fetchSpy = vi.spyOn(globalThis, 'fetch')
    const wrapper = await mountControls([PERMISSIONS.sessionView])

    await wrapper.get('[data-testid="online-users-button"]').trigger('click')
    await flushPromises()

    const send = document.body.querySelector<HTMLButtonElement>('[data-testid="shell-send-message"]')
    expect(send).not.toBeNull()
    expect(send?.disabled).toBe(true)
    expect(send?.title).toContain('待实现')
    send?.click()
    send?.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true }))
    send?.dispatchEvent(new KeyboardEvent('keydown', { key: ' ', bubbles: true }))
    expect(fetchSpy).not.toHaveBeenCalled()
    expect(fakeApi.revokeSession).not.toHaveBeenCalled()
    fetchSpy.mockRestore()
  })

  it('loads a safe active-session projection in the drawer for identity.session.view', async () => {
    fakeApi.listActiveSessions.mockResolvedValue({
      items: [
        {
          sessionNId: 'SES-1',
          userNId: 'USR-1',
          loginName: 'operator',
          name: '操作员',
          loginOn: '2026-08-30T01:00:00Z',
          lastRefreshedOn: '2026-08-30T01:30:00Z',
          expiresOn: '2026-08-31T01:00:00Z',
          isCurrent: true,
        },
      ],
      total: 1,
      pageIndex: 1,
      pageSize: 100,
    })
    const wrapper = await mountControls([PERMISSIONS.sessionView])

    await wrapper.get('[data-testid="online-users-button"]').trigger('click')
    await flushPromises()

    expect(fakeApi.listActiveSessions).toHaveBeenCalledTimes(1)
    expect(document.body.textContent).toContain('operator')
    expect(document.body.textContent).toContain('操作员')
    expect(document.body.textContent).not.toContain('token')
    expect(document.body.textContent).not.toContain('User-Agent')
    expect(document.body.textContent).not.toContain('IP')
  })

  it('renders the active-session list through the platform AppDataTable contract', async () => {
    fakeApi.listActiveSessions.mockResolvedValue({
      items: [
        {
          sessionNId: 'SES-1',
          userNId: 'USR-1',
          loginName: 'operator',
          name: '操作员',
          loginOn: '2026-08-30T01:00:00Z',
          lastRefreshedOn: '2026-08-30T01:30:00Z',
          expiresOn: '2026-08-31T01:00:00Z',
          isCurrent: true,
        },
      ],
      total: 1,
      pageIndex: 1,
      pageSize: 100,
    })
    const wrapper = await mountControls([PERMISSIONS.sessionView])

    await wrapper.get('[data-testid="online-users-button"]').trigger('click')
    await flushPromises()

    expect(document.body.querySelector('[data-testid="app-data-table"]')).not.toBeNull()
    expect(document.body.querySelector('.el-table')).toBeNull()
  })

  it('shows loading then a real empty state and supports manual refresh', async () => {
    let resolveRequest: ((value: unknown) => void) | undefined
    fakeApi.listActiveSessions.mockReturnValue(
      new Promise((resolve) => {
        resolveRequest = resolve
      }),
    )
    const wrapper = await mountControls([PERMISSIONS.sessionView])

    await wrapper.get('[data-testid="online-users-button"]').trigger('click')
    expect(wrapper.get('[data-testid="online-users-refresh"]').attributes('aria-busy')).toBe('true')
    resolveRequest?.({ items: [], total: 0, pageIndex: 1, pageSize: 100 })
    await flushPromises()

    expect(document.body.textContent).toContain('当前没有有效登录会话')
    fakeApi.listActiveSessions.mockResolvedValue({ items: [], total: 0, pageIndex: 1, pageSize: 100 })
    await wrapper.get('[data-testid="online-users-refresh"]').trigger('click')
    await flushPromises()
    expect(fakeApi.listActiveSessions).toHaveBeenCalledTimes(2)
  })

  it('shows a retry action after a load failure', async () => {
    fakeApi.listActiveSessions.mockRejectedValueOnce(new Error('offline'))
    const wrapper = await mountControls([PERMISSIONS.sessionView])

    await wrapper.get('[data-testid="online-users-button"]').trigger('click')
    await flushPromises()
    expect(document.body.textContent).toContain('加载失败')
    fakeApi.listActiveSessions.mockResolvedValue({ items: [], total: 0, pageIndex: 1, pageSize: 100 })
    await wrapper.get('[data-testid="online-users-retry"]').trigger('click')
    await flushPromises()
    expect(fakeApi.listActiveSessions).toHaveBeenCalledTimes(2)
  })

  it('hides the revoke action without the independent session.revoke permission', async () => {
    fakeApi.listActiveSessions.mockResolvedValue({
      items: [
        {
          sessionNId: 'SES-1',
          userNId: 'USR-1',
          loginName: 'operator',
          name: '操作员',
          loginOn: '2026-08-30T01:00:00Z',
          lastRefreshedOn: '2026-08-30T01:30:00Z',
          expiresOn: '2026-08-31T01:00:00Z',
          isCurrent: false,
        },
      ],
      total: 1,
      pageIndex: 1,
      pageSize: 100,
    })
    fakeApi.revokeSession.mockResolvedValue({ found: true, isCurrent: false })
    const confirm = vi
      .spyOn(ElMessageBox, 'confirm')
      .mockResolvedValue('confirm' as never)
    const wrapper = await mountControls([PERMISSIONS.sessionView])

    await wrapper.get('[data-testid="online-users-button"]').trigger('click')
    await flushPromises()
    expect(document.body.textContent).not.toContain('强制退出')

    // The revoke action is absent without the independent write permission.
    expect(document.body.querySelectorAll('.el-button').length).toBeGreaterThan(0)
    expect(fakeApi.revokeSession).not.toHaveBeenCalled()
    expect(confirm).not.toHaveBeenCalled()
  })

  it('confirms and revokes a non-current session when session.revoke is granted', async () => {
    fakeApi.listActiveSessions.mockResolvedValue({
      items: [
        {
          sessionNId: 'SES-2',
          userNId: 'USR-2',
          loginName: 'operator',
          name: '操作员',
          loginOn: '2026-08-30T01:00:00Z',
          lastRefreshedOn: '2026-08-30T01:30:00Z',
          expiresOn: '2026-08-31T01:00:00Z',
          isCurrent: false,
        },
      ],
      total: 1,
      pageIndex: 1,
      pageSize: 100,
    })
    fakeApi.revokeSession.mockResolvedValue({ found: true, isCurrent: false })
    const confirm = vi
      .spyOn(ElMessageBox, 'confirm')
      .mockResolvedValue('confirm' as never)
    const wrapper = await mountControls([PERMISSIONS.sessionView, PERMISSIONS.sessionRevoke])

    await wrapper.get('[data-testid="online-users-button"]').trigger('click')
    await flushPromises()
    const revoke = Array.from(document.body.querySelectorAll('button')).find((button) =>
      button.textContent?.includes('强制退出'),
    )
    expect(revoke).toBeDefined()
    revoke?.click()
    await flushPromises()

    expect(confirm).toHaveBeenCalledTimes(1)
    expect(fakeApi.revokeSession).toHaveBeenCalledWith('SES-2')
  })

  it('clears the local session and routes to login when the current session is revoked', async () => {
    fakeApi.listActiveSessions.mockResolvedValue({
      items: [
        {
          sessionNId: 'SES-current',
          userNId: 'USR-1',
          loginName: 'operator',
          name: '操作员',
          loginOn: '2026-08-30T01:00:00Z',
          lastRefreshedOn: '2026-08-30T01:30:00Z',
          expiresOn: '2026-08-31T01:00:00Z',
          isCurrent: true,
        },
      ],
      total: 1,
      pageIndex: 1,
      pageSize: 100,
    })
    fakeApi.revokeSession.mockResolvedValue({ found: true, isCurrent: true })
    vi.spyOn(ElMessageBox, 'confirm').mockResolvedValue('confirm' as never)
    const wrapper = await mountControls([PERMISSIONS.sessionView, PERMISSIONS.sessionRevoke])

    await wrapper.get('[data-testid="online-users-button"]').trigger('click')
    await flushPromises()
    const revoke = Array.from(document.body.querySelectorAll('button')).find((button) =>
      button.textContent?.includes('强制退出'),
    )
    revoke?.click()
    await flushPromises()

    expect(fakeApi.revokeSession).toHaveBeenCalledWith('SES-current')
    expect(useAuthStore().isAuthenticated).toBe(false)
    expect(activeRouter?.currentRoute.value.path).toBe('/login')
  })
})
