import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { createMemoryHistory, createRouter } from 'vue-router'

import NotificationCenterPage from '@/pages/mobile/NotificationCenterPage.vue'
import { registerPf04Api } from '@/api/systemData/pf04Registry'
import type { Pf04Api } from '@/api/systemData/pf04Types'
import { PERMISSIONS } from '@/permissions'
import { useAuthStore } from '@/stores/authStore'
import { persistAuthSession } from '../fixtures/session'

const { fakeRealtime, realtimeState } = vi.hoisted(() => ({
  fakeRealtime: { start: vi.fn(async () => undefined), stop: vi.fn(async () => undefined) },
  realtimeState: { onRefresh: null as (() => Promise<void>) | null },
}))

vi.mock('@/api/systemData/notificationHub', () => ({
  createNotificationRealtime: (options: { onRefresh: () => Promise<void> }) => {
    realtimeState.onRefresh = options.onRefresh
    return fakeRealtime
  },
}))

async function mountPage(permissions: string[]) {
  const pinia = createPinia()
  setActivePinia(pinia)
  sessionStorage.clear()
  persistAuthSession(permissions)
  await useAuthStore().restore()
  const router = createRouter({ history: createMemoryHistory(), routes: [{ path: '/:pathMatch(.*)*', component: { template: '<div />' } }] })
  await router.push('/')
  await router.isReady()
  return { wrapper: mount(NotificationCenterPage, { global: { plugins: [pinia, router] } }), router }
}

describe('NotificationCenterPage', () => {
  let api: Record<string, ReturnType<typeof vi.fn>>
  const page = {
    items: [
      { notificationNId: 'N-1', kind: 'System', title: '安全通知', body: '正文', senderUserNId: 'u1', isRead: false, deliveredOn: '2026-08-30T01:00:00Z', readOn: null, expiresOn: null, targetRoute: '/safe' },
      { notificationNId: 'N-2', kind: 'System', title: '危险通知', body: '正文', senderUserNId: 'u1', isRead: true, deliveredOn: '2026-08-30T02:00:00Z', readOn: '2026-08-30T03:00:00Z', expiresOn: null, targetRoute: '/bad\u0000route' },
    ], page: 1, pageSize: 50, total: 2, unreadCount: 1,
  }

  beforeEach(() => {
    vi.stubEnv('VITE_AUTH_MODE', 'mock')
    vi.useFakeTimers()
    realtimeState.onRefresh = null
    fakeRealtime.start.mockClear()
    fakeRealtime.stop.mockClear()
    api = { getInbox: vi.fn().mockResolvedValue(page), markRead: vi.fn().mockResolvedValue(undefined), batchRead: vi.fn().mockResolvedValue(undefined) }
    registerPf04Api(api as unknown as Pf04Api)
  })

  afterEach(() => { vi.useRealTimers(); vi.unstubAllEnvs() })

  it('does not call inbox API or realtime without permission', async () => {
    const { wrapper } = await mountPage([])
    await flushPromises()
    expect(api.getInbox).not.toHaveBeenCalled()
    expect(fakeRealtime.start).not.toHaveBeenCalled()
    wrapper.unmount()
  })

  it('loads, marks one/batch read, polls, refreshes through realtime, and hides every unsafe route', async () => {
    const { wrapper, router } = await mountPage([PERMISSIONS.systemDataNotificationInboxRead])
    await flushPromises()
    expect(api.getInbox).toHaveBeenCalledWith(1, 50)
    expect(wrapper.findAll('button').filter((button) => button.text().includes('打开'))).toHaveLength(1)
    await wrapper.findAll('button').find((button) => button.text().includes('打开'))!.trigger('click')
    await flushPromises()
    expect(router.currentRoute.value.path).toBe('/safe')

    await wrapper.findAll('button').find((button) => button.text().includes('标记已读'))!.trigger('click')
    await flushPromises()
    expect(api.markRead).toHaveBeenCalledWith('N-1')
    await wrapper.findAll('button').find((button) => button.text().includes('全部'))!.trigger('click')
    await flushPromises()
    expect(api.batchRead).toHaveBeenCalledWith(['N-1'])

    await vi.advanceTimersByTimeAsync(15000)
    expect(api.getInbox!.mock.calls.length).toBeGreaterThan(1)
    await realtimeState.onRefresh?.()
    expect(api.getInbox!.mock.calls.length).toBeGreaterThan(2)
    wrapper.unmount()
    expect(fakeRealtime.stop).toHaveBeenCalledTimes(1)
    expect(vi.getTimerCount()).toBe(0)
  })

  it('shows a load error and retries the inbox request', async () => {
    api.getInbox!.mockRejectedValueOnce(new Error('inbox down')).mockResolvedValueOnce(page)
    const { wrapper } = await mountPage([PERMISSIONS.systemDataNotificationInboxRead])
    await flushPromises()
    expect(wrapper.get('[role="alert"]').text()).toContain('inbox down')
    await wrapper.get('[role="alert"] button').trigger('click')
    await flushPromises()
    expect(api.getInbox!).toHaveBeenCalledTimes(2)
  })
})
