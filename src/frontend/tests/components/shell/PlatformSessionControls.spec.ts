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
  })

  it('keeps Notification as a disabled no-op and gates online sessions independently', async () => {
    const wrapper = await mountControls([])

    expect(wrapper.get('[data-testid="shell-notifications"]').attributes('disabled')).toBeDefined()
    expect(wrapper.get('[data-testid="shell-notifications"]').attributes('title')).toContain(
      '待实现',
    )
    expect(wrapper.find('[data-testid="online-users-button"]').exists()).toBe(false)
    expect(fakeApi.listActiveSessions).not.toHaveBeenCalled()
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
})
