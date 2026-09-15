import { nextTick, reactive } from 'vue'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

const { authState, realtime, chatSession, runtimeConfig } = vi.hoisted(() => ({
  authState: {
    value: null as {
      user: { userId: string; tenantId?: string } | null
      isAuthenticated: boolean
      session: {
        accessToken?: string
        transport?: string
        embeddedSessionToken?: string
        embeddedSessionBinding?: string
        expiresAt?: string
      } | null
      keepAliveEmbeddedSession?: () => Promise<unknown>
      clearLocalSession?: () => void
    } | null,
  },
  realtime: {
    status: 'Disconnected' as 'Disconnected' | 'Connected',
    start: vi.fn<() => Promise<void>>(),
    stop: vi.fn<() => Promise<void>>(),
    setPresence: vi.fn<() => Promise<unknown>>(),
  },
  chatSession: {
    selectedConversation: null as { conversationNId: string } | null,
    resetSession: vi.fn(),
  },
  runtimeConfig: { authMode: 'http' as 'http' | 'embedded' },
}))

vi.mock('@/api/collaborationHub', () => ({ getCollaborationRealtime: () => realtime }))
vi.mock('@/stores/authStore', () => ({ useAuthStore: () => authState.value }))
vi.mock('@/stores/collaborationChatStore', () => ({
  useCollaborationChatStore: () => chatSession,
}))
vi.mock('@/config/runtimeConfig', () => ({
  loadRuntimeConfig: () => runtimeConfig,
}))

import { createCollaborationRuntimePlugin } from '@/systemData/runtime/collaborationRuntime'

describe('collaboration runtime', () => {
  beforeEach(() => {
    vi.useFakeTimers()
  })

  afterEach(() => {
    vi.clearAllTimers()
    vi.useRealTimers()
    vi.clearAllMocks()
    runtimeConfig.authMode = 'http'
  })

  it('recovers the sync chain and waits for Connected before setting presence', async () => {
    const auth = reactive({
      user: { userId: 'U-1' },
      isAuthenticated: true,
      session: { accessToken: 'token-1' },
    })
    authState.value = auth
    realtime.status = 'Disconnected'
    realtime.start
      .mockRejectedValueOnce(new Error('initial connection failed'))
      .mockImplementationOnce(async () => {
        realtime.status = 'Connected'
      })
    realtime.stop.mockResolvedValue(undefined)
    realtime.setPresence.mockResolvedValue({})

    const plugin = createCollaborationRuntimePlugin({} as never)
    plugin.install?.({} as never)
    await vi.waitFor(() => expect(realtime.start).toHaveBeenCalledOnce())
    expect(realtime.setPresence).not.toHaveBeenCalled()

    auth.session = { accessToken: 'token-2' }
    await nextTick()
    await vi.waitFor(() => expect(realtime.start).toHaveBeenCalledTimes(2))
    await vi.waitFor(() => expect(realtime.setPresence).toHaveBeenCalledWith('Online'))
  })

  it('stops the old identity before starting after an authenticated user switch', async () => {
    const auth = reactive({
      user: { userId: 'U-1' },
      isAuthenticated: true,
      session: { accessToken: 'token-1' },
    })
    authState.value = auth
    realtime.status = 'Connected'
    realtime.start.mockResolvedValue(undefined)
    realtime.stop.mockResolvedValue(undefined)
    realtime.setPresence.mockResolvedValue({})

    const plugin = createCollaborationRuntimePlugin({} as never)
    plugin.install?.({} as never)
    await vi.waitFor(() => expect(realtime.start).toHaveBeenCalledOnce())

    auth.user = { userId: 'U-2' }
    auth.session = { accessToken: 'token-2' }
    await nextTick()
    await vi.waitFor(() => expect(realtime.stop).toHaveBeenCalledOnce())
    await vi.waitFor(() => expect(realtime.start).toHaveBeenCalledTimes(2))

    expect(realtime.stop.mock.invocationCallOrder[0]).toBeLessThan(
      realtime.start.mock.invocationCallOrder[1]!,
    )
    expect(realtime.setPresence).toHaveBeenCalledWith('Online')
  })

  it('owns one heartbeat and clears it when the session ends', async () => {
    const auth = reactive({
      user: { userId: 'U-1' } as { userId: string } | null,
      isAuthenticated: true,
      session: { accessToken: 'token-1' } as { accessToken: string } | null,
    })
    authState.value = auth
    realtime.status = 'Connected'
    realtime.start.mockResolvedValue(undefined)
    realtime.stop.mockResolvedValue(undefined)
    realtime.setPresence.mockResolvedValue({})
    const setIntervalSpy = vi.spyOn(globalThis, 'setInterval')
    const clearIntervalSpy = vi.spyOn(globalThis, 'clearInterval')

    const plugin = createCollaborationRuntimePlugin({} as never)
    plugin.install?.({} as never)
    await vi.waitFor(() => expect(realtime.start).toHaveBeenCalledOnce())

    expect(setIntervalSpy).toHaveBeenCalledTimes(1)
    vi.advanceTimersByTime(20_000)
    expect(realtime.setPresence).toHaveBeenCalledTimes(2)

    auth.user = null
    auth.session = null
    auth.isAuthenticated = false
    await nextTick()
    await vi.waitFor(() => expect(realtime.stop).toHaveBeenCalledOnce())

    expect(clearIntervalSpy).toHaveBeenCalledOnce()
    vi.advanceTimersByTime(20_000)
    expect(realtime.setPresence).toHaveBeenCalledTimes(2)
  })

  it('does not restart the page connection when an embedded heartbeat renews expiry', async () => {
    runtimeConfig.authMode = 'embedded'
    const auth = reactive({
      user: { userId: 'U-1', tenantId: 'T-1' },
      isAuthenticated: true,
      session: {
        transport: 'embedded-cookie' as const,
        embeddedSessionToken: 'page-token',
        embeddedSessionBinding: 'page-binding',
        expiresAt: '2026-09-14T12:00:00.000Z',
      },
      keepAliveEmbeddedSession: vi.fn(async () => auth.session),
      clearLocalSession: vi.fn(),
    })
    authState.value = auth
    realtime.status = 'Connected'
    realtime.start.mockResolvedValue(undefined)
    realtime.stop.mockResolvedValue(undefined)
    realtime.setPresence.mockResolvedValue({})

    const plugin = createCollaborationRuntimePlugin({} as never)
    plugin.install?.({} as never)
    await vi.waitFor(() => expect(realtime.start).toHaveBeenCalledOnce())

    vi.advanceTimersByTime(80_000)
    await nextTick()
    auth.session.expiresAt = '2026-09-14T12:02:00.000Z'
    await nextTick()

    expect(realtime.stop).not.toHaveBeenCalled()
    expect(realtime.start).toHaveBeenCalledOnce()
    expect(chatSession.resetSession).not.toHaveBeenCalled()
  })

  it('stops and clears the page when embedded heartbeat or Hub authorization expires', async () => {
    runtimeConfig.authMode = 'embedded'
    const clearLocalSession = vi.fn()
    const auth = reactive({
      user: { userId: 'U-1', tenantId: 'T-1' },
      isAuthenticated: true,
      session: {
        transport: 'embedded-cookie' as const,
        embeddedSessionToken: 'page-token',
        embeddedSessionBinding: 'page-binding',
        expiresAt: '2026-09-14T12:00:00.000Z',
      },
      keepAliveEmbeddedSession: vi.fn(async () => {
        throw new Error('unauthorized')
      }),
      clearLocalSession,
    })
    authState.value = auth
    realtime.status = 'Connected'
    realtime.start.mockResolvedValue(undefined)
    realtime.stop.mockResolvedValue(undefined)
    realtime.setPresence.mockResolvedValue({})

    const plugin = createCollaborationRuntimePlugin({} as never)
    plugin.install?.({} as never)
    await vi.waitFor(() => expect(realtime.start).toHaveBeenCalledOnce())
    await vi.waitFor(() => expect(clearLocalSession).toHaveBeenCalledOnce())

    expect(realtime.stop).toHaveBeenCalledOnce()
    expect(chatSession.resetSession).toHaveBeenCalledOnce()
  })

  it('stops on embedded collaboration route exit, then restarts on re-entry', async () => {
    runtimeConfig.authMode = 'embedded'
    const route = reactive({ name: 'collaboration-chat' })
    const auth = reactive({
      user: { userId: 'U-1', tenantId: 'T-1' },
      isAuthenticated: true,
      session: {
        transport: 'embedded-cookie' as const,
        embeddedSessionToken: 'page-token',
        embeddedSessionBinding: 'page-binding',
      },
      keepAliveEmbeddedSession: vi.fn(async () => auth.session),
      clearLocalSession: vi.fn(),
    })
    authState.value = auth
    realtime.status = 'Connected'
    realtime.start.mockResolvedValue(undefined)
    realtime.stop.mockResolvedValue(undefined)
    realtime.setPresence.mockResolvedValue({})
    const router = { currentRoute: { value: route } }

    const plugin = createCollaborationRuntimePlugin({} as never, { router: router as never })
    plugin.install?.({} as never)
    await vi.waitFor(() => expect(realtime.start).toHaveBeenCalledOnce())

    route.name = 'pc-home'
    await nextTick()
    await vi.waitFor(() => expect(realtime.stop).toHaveBeenCalledOnce())
    expect(chatSession.resetSession).toHaveBeenCalledOnce()

    route.name = 'collaboration-chat'
    await nextTick()
    await vi.waitFor(() => expect(realtime.start).toHaveBeenCalledTimes(2))
  })

  it('keeps the connection on HTTP route changes', async () => {
    runtimeConfig.authMode = 'http'
    const route = reactive({ name: 'collaboration-chat' })
    const auth = reactive({
      user: { userId: 'U-1', tenantId: 'T-1' },
      isAuthenticated: true,
      session: { accessToken: 'token-1' },
    })
    authState.value = auth
    realtime.status = 'Connected'
    realtime.start.mockResolvedValue(undefined)
    realtime.stop.mockResolvedValue(undefined)
    realtime.setPresence.mockResolvedValue({})

    const plugin = createCollaborationRuntimePlugin({} as never, { router: { currentRoute: { value: route } } as never })
    plugin.install?.({} as never)
    await vi.waitFor(() => expect(realtime.start).toHaveBeenCalledOnce())
    route.name = 'pc-home'
    await nextTick()
    await Promise.resolve()
    await Promise.resolve()

    expect(realtime.stop).not.toHaveBeenCalled()
  })
})
