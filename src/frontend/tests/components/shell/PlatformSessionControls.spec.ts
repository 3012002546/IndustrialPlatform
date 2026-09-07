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
    getInbox: vi.fn(),
    markRead: vi.fn(),
    batchRead: vi.fn(),
    sendSystemMessage: vi.fn(),
  },
}))

const { fakeRealtime } = vi.hoisted(() => ({
  fakeRealtime: {
    start: vi.fn(async () => undefined),
    stop: vi.fn(async () => undefined),
  },
}))

const { realtimeState } = vi.hoisted(() => ({ realtimeState: { onRefresh: null as (() => Promise<void>) | null } }))

vi.mock('@/api/identity/managementRegistry', () => ({
  getManagementApi: () => fakeApi,
}))

vi.mock('@/api/systemData/pf04Registry', () => ({
  getPf04Api: () => fakeApi,
}))

vi.mock('@/api/systemData/notificationHub', () => ({
  createNotificationRealtime: (options: { onRefresh: () => Promise<void> }) => {
    realtimeState.onRefresh = options.onRefresh
    return fakeRealtime
  },
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
    fakeApi.getInbox.mockReset()
    fakeApi.markRead.mockReset()
    fakeApi.batchRead.mockReset()
    fakeApi.sendSystemMessage.mockReset()
    fakeRealtime.start.mockClear()
    fakeRealtime.stop.mockClear()
  })

  afterEach(() => {
    wrappers.splice(0).forEach((wrapper) => wrapper.unmount())
    document.body.innerHTML = ''
    vi.restoreAllMocks()
    vi.useRealTimers()
    vi.unstubAllEnvs()
    activeRouter = undefined
  })

  it('does not request notification data or create realtime without inbox permission', async () => {
    const wrapper = await mountControls([])

    expect(wrapper.find('[data-testid="shell-notifications"]').exists()).toBe(false)
    expect(wrapper.find('[data-testid="online-users-button"]').exists()).toBe(false)
    expect(fakeApi.getInbox).not.toHaveBeenCalled()
    expect(fakeRealtime.start).not.toHaveBeenCalled()
  })

  it('loads the real inbox, shows unread count, marks one item and opens safe targets', async () => {
    fakeApi.getInbox.mockResolvedValue({
      items: [
        {
          notificationNId: 'N-1',
          kind: 'System',
          title: '设备告警',
          body: '请检查设备。',
          senderUserNId: 'USR-2',
          isRead: false,
          deliveredOn: '2026-08-30T01:00:00Z',
          readOn: null,
          expiresOn: null,
          targetRoute: '/pc/systemdata/files',
        },
      ],
      page: 1,
      pageSize: 20,
      total: 1,
      unreadCount: 1,
    })
    fakeApi.markRead.mockResolvedValue(undefined)
    const wrapper = await mountControls([PERMISSIONS.systemDataNotificationInboxRead])

    expect(fakeRealtime.start).toHaveBeenCalledTimes(1)
    await wrapper.get('[data-testid="shell-notifications"]').trigger('click')
    await flushPromises()

    expect(fakeApi.getInbox).toHaveBeenCalledWith(1, 20)
    expect(wrapper.get('[data-testid="shell-notification-unread-count"]').text()).toBe('1')
    expect(document.body.textContent).toContain('设备告警')
    expect(document.body.textContent).toContain('未读')

    document.body.querySelector<HTMLButtonElement>('[data-testid="shell-notification-read-N-1"]')?.click()
    await flushPromises()
    expect(fakeApi.markRead).toHaveBeenCalledWith('N-1')

    document.body.querySelector<HTMLButtonElement>('[data-testid="shell-notification-open-N-1"]')?.click()
    await flushPromises()
    expect(activeRouter?.currentRoute.value.path).toBe('/pc/systemdata/files')
  })

  it('marks all visible unread notifications in one batch', async () => {
    fakeApi.getInbox.mockResolvedValue({
      items: [
        { notificationNId: 'N-1', kind: 'System', title: '一', body: '一', senderUserNId: 'U', isRead: false, deliveredOn: '2026-08-30T01:00:00Z', readOn: null, expiresOn: null },
        { notificationNId: 'N-2', kind: 'System', title: '二', body: '二', senderUserNId: 'U', isRead: false, deliveredOn: '2026-08-30T02:00:00Z', readOn: null, expiresOn: null },
      ], page: 1, pageSize: 20, total: 2, unreadCount: 2,
    })
    fakeApi.batchRead.mockResolvedValue(undefined)
    const wrapper = await mountControls([PERMISSIONS.systemDataNotificationInboxRead])
    await wrapper.get('[data-testid="shell-notifications"]').trigger('click')
    await flushPromises()
    document.body.querySelector<HTMLButtonElement>('[data-testid="shell-notification-mark-all-read"]')?.click()
    await flushPromises()
    expect(fakeApi.batchRead).toHaveBeenCalledWith(['N-1', 'N-2'])
  })

  it('does not render a zero unread badge and rejects //, C0, and DEL notification targets', async () => {
    fakeApi.getInbox.mockResolvedValue({
      items: [
        { notificationNId: 'SAFE', kind: 'System', title: 'safe', body: 'body', senderUserNId: 'U', isRead: true, deliveredOn: '2026-08-30T01:00:00Z', readOn: '2026-08-30T02:00:00Z', expiresOn: null, targetRoute: '/safe' },
        { notificationNId: 'DOUBLE', kind: 'System', title: 'double', body: 'body', senderUserNId: 'U', isRead: true, deliveredOn: '2026-08-30T01:00:00Z', readOn: '2026-08-30T02:00:00Z', expiresOn: null, targetRoute: '//unsafe' },
        { notificationNId: 'C0', kind: 'System', title: 'c0', body: 'body', senderUserNId: 'U', isRead: true, deliveredOn: '2026-08-30T01:00:00Z', readOn: '2026-08-30T02:00:00Z', expiresOn: null, targetRoute: '/bad\u0000route' },
        { notificationNId: 'DEL', kind: 'System', title: 'del', body: 'body', senderUserNId: 'U', isRead: true, deliveredOn: '2026-08-30T01:00:00Z', readOn: '2026-08-30T02:00:00Z', expiresOn: null, targetRoute: '/bad\u007froute' },
      ], page: 1, pageSize: 20, total: 4, unreadCount: 0,
    })
    const wrapper = await mountControls([PERMISSIONS.systemDataNotificationInboxRead])
    await wrapper.get('[data-testid="shell-notifications"]').trigger('click')
    await flushPromises()
    expect(wrapper.find('[data-testid="shell-notification-unread-count"]').exists()).toBe(false)
    expect(document.body.querySelector('[data-testid="shell-notification-open-SAFE"]')).not.toBeNull()
    expect(document.body.querySelector('[data-testid="shell-notification-open-DOUBLE"]')).toBeNull()
    expect(document.body.querySelector('[data-testid="shell-notification-open-C0"]')).toBeNull()
    expect(document.body.querySelector('[data-testid="shell-notification-open-DEL"]')).toBeNull()
  })

  it('refreshes through the visible 15 second poll and realtime callback, then clears both on unmount', async () => {
    vi.useFakeTimers()
    fakeApi.getInbox.mockResolvedValue({ items: [], page: 1, pageSize: 20, total: 0, unreadCount: 0 })
    realtimeState.onRefresh = null
    const wrapper = await mountControls([PERMISSIONS.systemDataNotificationInboxRead])
    await flushPromises()
    expect(fakeApi.getInbox).toHaveBeenCalledTimes(1)
    await vi.advanceTimersByTimeAsync(15000)
    expect(fakeApi.getInbox).toHaveBeenCalledTimes(2)
    const refresh = realtimeState.onRefresh as (() => Promise<void>) | null
    expect(refresh).not.toBeNull()
    if (refresh === null) throw new Error('SignalR refresh callback was not registered')
    await refresh()
    expect(fakeApi.getInbox).toHaveBeenCalledTimes(3)
    wrapper.unmount()
    expect(fakeRealtime.stop).toHaveBeenCalledTimes(1)
    const callsAfterUnmount = fakeApi.getInbox.mock.calls.length
    await vi.advanceTimersByTimeAsync(15000)
    expect(fakeApi.getInbox.mock.calls.length).toBe(callsAfterUnmount)
  })

  it('opens an enabled system-message form only with send permission and locks the user target', async () => {
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
    fakeApi.sendSystemMessage.mockResolvedValue({
      notificationNId: 'N-3', kind: 'System', title: '提醒', body: '正文', senderUserNId: 'USR-1', isRead: false,
      deliveredOn: '2026-08-30T01:00:00Z', readOn: null, expiresOn: null,
    })
    const wrapper = await mountControls([PERMISSIONS.sessionView, PERMISSIONS.systemDataNotificationSystemSend])

    await wrapper.get('[data-testid="online-users-button"]').trigger('click')
    await flushPromises()

    await wrapper.get('[data-testid="shell-send-message"]').trigger('click')
    expect(document.body.querySelector('[data-testid="shell-send-message-dialog"]')).not.toBeNull()
    expect(wrapper.find('[data-testid="shell-send-message-recipient"]').attributes('disabled')).toBeDefined()
    await wrapper.get('[data-testid="shell-send-message-title"]').setValue('提醒')
    await wrapper.get('[data-testid="shell-send-message-body"]').setValue('正文')
    await wrapper.get('[data-testid="shell-send-message-submit"]').trigger('click')
    await flushPromises()

    expect(fakeApi.sendSystemMessage).toHaveBeenCalledWith(expect.objectContaining({
      title: '提醒',
      body: '正文',
      recipientUserNIds: ['USR-1'],
      idempotencyKey: expect.any(String),
    }))
    expect(fakeApi.sendSystemMessage.mock.calls[0]?.[0].recipientUserNIds).not.toContain('SES-1')
    await vi.waitFor(() => expect(document.body.querySelector('[data-testid="shell-send-message-dialog"]')).toBeNull())
    await wrapper.get('[data-testid="shell-send-message"]').trigger('click')
    expect(document.body.querySelector<HTMLInputElement>('[data-testid="shell-send-message-title"]')?.value).toBe('')
    expect(document.body.querySelector<HTMLTextAreaElement>('[data-testid="shell-send-message-body"]')?.value).toBe('')
  })

  it('keeps the failed form open for retry and reuses the same idempotency key', async () => {
    fakeApi.listActiveSessions.mockResolvedValue({
      items: [{ sessionNId: 'SES-1', userNId: 'USR-1', loginName: 'operator', name: '操作员', loginOn: '2026-08-30T01:00:00Z', lastRefreshedOn: '2026-08-30T01:30:00Z', expiresOn: '2026-08-31T01:00:00Z', isCurrent: false }],
      total: 1, pageIndex: 1, pageSize: 100,
    })
    fakeApi.sendSystemMessage.mockRejectedValueOnce(new Error('temporary failure')).mockResolvedValueOnce({
      notificationNId: 'N-4', kind: 'System', title: '提醒', body: '正文', senderUserNId: 'USR-1', isRead: false,
      deliveredOn: '2026-08-30T01:00:00Z', readOn: null, expiresOn: null,
    })
    const wrapper = await mountControls([PERMISSIONS.sessionView, PERMISSIONS.systemDataNotificationSystemSend])

    await wrapper.get('[data-testid="online-users-button"]').trigger('click')
    await flushPromises()
    await wrapper.get('[data-testid="shell-send-message"]').trigger('click')
    await wrapper.get('[data-testid="shell-send-message-title"]').setValue('提醒')
    await wrapper.get('[data-testid="shell-send-message-body"]').setValue('正文')
    await wrapper.get('[data-testid="shell-send-message-submit"]').trigger('click')
    await flushPromises()

    const firstKey = fakeApi.sendSystemMessage.mock.calls[0]?.[0].idempotencyKey
    expect(document.body.querySelector('[data-testid="shell-send-message-dialog"]')).not.toBeNull()
    expect(document.body.textContent).toContain('消息发送失败')
    await wrapper.get('[data-testid="shell-send-message-submit"]').trigger('click')
    await flushPromises()

    expect(fakeApi.sendSystemMessage).toHaveBeenCalledTimes(2)
    expect(fakeApi.sendSystemMessage.mock.calls[1]?.[0].idempotencyKey).toBe(firstKey)
    await vi.waitFor(() => expect(document.body.querySelector('[data-testid="shell-send-message-dialog"]')).toBeNull())
  })

  it('hides send-message action without notification.system.send permission', async () => {
    fakeApi.listActiveSessions.mockResolvedValue({
      items: [{ sessionNId: 'SES-1', userNId: 'USR-1', loginName: 'operator', name: '操作员', loginOn: '2026-08-30T01:00:00Z', lastRefreshedOn: '2026-08-30T01:30:00Z', expiresOn: '2026-08-31T01:00:00Z', isCurrent: false }],
      total: 1, pageIndex: 1, pageSize: 100,
    })
    const wrapper = await mountControls([PERMISSIONS.sessionView])
    await wrapper.get('[data-testid="online-users-button"]').trigger('click')
    await flushPromises()
    expect(document.body.querySelector('[data-testid="shell-send-message"]')).toBeNull()
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
