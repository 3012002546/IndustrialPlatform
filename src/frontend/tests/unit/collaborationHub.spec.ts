import { afterEach, describe, expect, it, vi } from 'vitest'

const { connections, withUrl } = vi.hoisted(() => ({
  connections: [] as FakeConnection[],
  withUrl: vi.fn(),
}))

class FakeConnection {
  readonly handlers = new Map<string, (...args: never[]) => void>()
  readonly invocations: Array<{ method: string; args: unknown[] }> = []
  readonly start = vi.fn(async () => undefined)
  readonly stop = vi.fn(async () => {
    this.state = 'Disconnected'
  })
  state = 'Connected'

  on(event: string, handler: (...args: never[]) => void): void {
    this.handlers.set(event, handler)
  }

  onreconnected(handler: () => void): void {
    this.handlers.set('reconnected', handler)
  }

  onclose(handler: () => void): void {
    this.handlers.set('close', handler)
  }

  async invoke<T>(method: string, ...args: unknown[]): Promise<T> {
    this.invocations.push({ method, args })
    return { messageNId: 'M-1' } as T
  }

  trigger(event: string, ...args: never[]): void {
    this.handlers.get(event)?.(...args)
  }
}

vi.mock('@microsoft/signalr', () => ({
  HubConnectionBuilder: class {
    withUrl(...args: unknown[]): this {
      withUrl(...args)
      return this
    }
    withAutomaticReconnect(): this {
      return this
    }
    configureLogging(): this {
      return this
    }
    build(): FakeConnection {
      const connection = new FakeConnection()
      connections.push(connection)
      return connection
    }
  },
  LogLevel: { Warning: 3 },
}))

import { createCollaborationRealtime } from '@/api/collaborationHub'

describe('collaboration realtime', () => {
  afterEach(() => {
    connections.length = 0
    withUrl.mockClear()
    vi.unstubAllEnvs()
  })

  it('delivers messages, presence, typing, and reconnect backfill', async () => {
    vi.stubEnv('VITE_API_BASE_URL', 'http://localhost:5041')
    const onMessage = vi.fn()
    const onAck = vi.fn()
    const onPresence = vi.fn()
    const onTyping = vi.fn()
    const onReconnected = vi.fn()
    const realtime = createCollaborationRealtime({
      getAccessToken: () => 'access-token',
      onMessage,
      onAck,
      onPresence,
      onTyping,
      onReconnected,
      initialRetryDelaysMs: [0],
    })
    const connection = connections[0]!
    expect(withUrl).toHaveBeenCalledWith(
      'http://localhost:5041/collaboration/hubs/collaboration-v1',
      { accessTokenFactory: expect.any(Function) },
    )

    await realtime.start()
    const message = { messageNId: 'M-1' }
    const presence = { userNId: 'U-2', state: 'Online' }
    connection.trigger('message.accepted', message as never)
    connection.trigger('message.ack', {
      conversationNId: 'C-1',
      clientMessageNId: 'M-1',
      messageNId: 'M-1',
      sequence: 1,
    } as never)
    connection.trigger('presence.changed', presence as never)
    connection.trigger('typing', {
      conversationNId: 'C-1',
      userNId: 'U-2',
      isTyping: true,
    } as never)
    connection.trigger('reconnected')
    await Promise.resolve()

    expect(onMessage).toHaveBeenCalledWith(message)
    expect(onAck).toHaveBeenCalledWith({
      conversationNId: 'C-1',
      clientMessageNId: 'M-1',
      messageNId: 'M-1',
      sequence: 1,
    })
    expect(onPresence).toHaveBeenCalledWith(presence)
    expect(onTyping).toHaveBeenCalledWith({
      conversationNId: 'C-1',
      userNId: 'U-2',
      isTyping: true,
    })
    expect(onReconnected).toHaveBeenCalledOnce()

    await realtime.joinConversation('C-1')
    await realtime.leaveConversation('C-1')
    await realtime.setPresence('Online')
    await realtime.sendMessage('C-1', { clientMessageNId: 'M-1', messageType: 'Text' })
    expect(connection.invocations).toEqual([
      { method: 'JoinConversation', args: ['C-1'] },
      { method: 'LeaveConversation', args: ['C-1'] },
      { method: 'SetPresence', args: ['Online'] },
      { method: 'SendMessage', args: ['C-1', { clientMessageNId: 'M-1', messageType: 'Text' }] },
    ])

    await realtime.stop()
    expect(connection.stop).toHaveBeenCalledOnce()
  })

  it('keeps retrying after the initial start fails and exposes recovery status', async () => {
    vi.stubEnv('VITE_API_BASE_URL', 'http://localhost:5041')
    const onReconnected = vi.fn()
    const realtime = createCollaborationRealtime({
      onMessage: vi.fn(),
      onPresence: vi.fn(),
      onReconnected,
      initialRetryDelaysMs: [25, 0],
    })
    const connection = connections[0]!
    connection.state = 'Disconnected'
    let attempts = 0
    connection.start.mockImplementation(async () => {
      attempts += 1
      if (attempts === 1) throw new Error('broker unavailable')
      connection.state = 'Connected'
    })

    await realtime.start()
    expect(realtime.status).toBe('Retrying')
    await vi.waitFor(() => expect(attempts).toBe(2))
    expect(realtime.status).toBe('Connected')
    expect(realtime.lastStartError).toBeNull()
    await vi.waitFor(() => expect(onReconnected).toHaveBeenCalledOnce())
    await realtime.stop()
  })

  it('restarts the same hub with the current access token after a stop', async () => {
    vi.stubEnv('VITE_API_BASE_URL', 'http://localhost:5041')
    let accessToken = 'token-u1'
    const startedTokens: string[] = []
    const realtime = createCollaborationRealtime({
      getAccessToken: () => accessToken,
      onMessage: vi.fn(),
      onPresence: vi.fn(),
      onReconnected: vi.fn(),
      initialRetryDelaysMs: [0],
    })
    const connection = connections[0]!
    const tokenFactory = (withUrl.mock.calls[0]![1] as { accessTokenFactory: () => string })
      .accessTokenFactory
    connection.state = 'Disconnected'
    connection.start.mockImplementation(async () => {
      startedTokens.push(tokenFactory())
      connection.state = 'Connected'
    })

    await realtime.start()
    await realtime.stop()
    accessToken = 'token-u2'
    await realtime.start()

    expect(startedTokens).toEqual(['token-u1', 'token-u2'])
    expect(connection.stop).toHaveBeenCalledOnce()
    expect(realtime.status).toBe('Connected')
    await realtime.stop()
  })

  it('cancels the initial retry when the session is stopped', async () => {
    vi.stubEnv('VITE_API_BASE_URL', 'http://localhost:5041')
    const realtime = createCollaborationRealtime({
      onMessage: vi.fn(),
      onPresence: vi.fn(),
      onReconnected: vi.fn(),
      initialRetryDelaysMs: [1000],
    })
    const connection = connections[0]!
    connection.state = 'Disconnected'
    let attempts = 0
    connection.start.mockImplementation(async () => {
      attempts += 1
      throw new Error('broker unavailable')
    })

    await realtime.start()
    expect(realtime.status).toBe('Retrying')
    await realtime.stop()

    expect(attempts).toBe(1)
    expect(connection.stop).toHaveBeenCalledOnce()
    expect(realtime.status).toBe('Disconnected')
  })
})
