import { nextTick, reactive } from 'vue'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

const { authState, realtime, chatSession } = vi.hoisted(() => ({
  authState: {
    value: null as {
      user: { userId: string } | null
      isAuthenticated: boolean
      session: { accessToken: string } | null
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
}))

vi.mock('@/api/collaborationHub', () => ({ getCollaborationRealtime: () => realtime }))
vi.mock('@/stores/authStore', () => ({ useAuthStore: () => authState.value }))
vi.mock('@/stores/collaborationChatStore', () => ({
  useCollaborationChatStore: () => chatSession,
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
})
