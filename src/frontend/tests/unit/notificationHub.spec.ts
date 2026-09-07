import { afterEach, describe, expect, it, vi } from 'vitest'

const { connections } = vi.hoisted(() => ({ connections: [] as FakeConnection[] }))

class FakeConnection {
  readonly handlers = new Map<string, () => void>()
  readonly start = vi.fn(async () => undefined)
  readonly stop = vi.fn(async () => undefined)
  on(event: string, handler: () => void): void { this.handlers.set(event, handler) }
  onreconnected(handler: () => void): void { this.handlers.set('reconnected', handler) }
  trigger(event: string): void { this.handlers.get(event)?.() }
}

vi.mock('@microsoft/signalr', () => ({
  HubConnectionBuilder: class {
    withUrl(): this { return this }
    withAutomaticReconnect(): this { return this }
    configureLogging(): this { return this }
    build(): FakeConnection { const connection = new FakeConnection(); connections.push(connection); return connection }
  },
  LogLevel: { Warning: 3 },
}))

import { createNotificationRealtime } from '@/api/systemData/notificationHub'

describe('notification realtime', () => {
  afterEach(() => {
    connections.length = 0
    vi.unstubAllEnvs()
  })

  it('coalesces duplicate events and refreshes after reconnect', async () => {
    vi.stubEnv('VITE_API_BASE_URL', 'http://localhost:5041')
    const refresh = vi.fn()
    const realtime = createNotificationRealtime({ onRefresh: refresh })

    await realtime.start()
    refresh.mockClear()
    connections[0]!.trigger('notification.changed')
    connections[0]!.trigger('notification.changed')
    await Promise.resolve()
    expect(refresh).toHaveBeenCalledTimes(1)

    connections[0]!.trigger('reconnected')
    await Promise.resolve()
    expect(refresh).toHaveBeenCalledTimes(2)
    await realtime.stop()
    expect(connections[0]!.stop).toHaveBeenCalledOnce()
  })

  it('refreshes immediately when the document returns to the foreground', async () => {
    vi.stubEnv('VITE_API_BASE_URL', 'http://localhost:5041')
    const refresh = vi.fn()
    const realtime = createNotificationRealtime({ onRefresh: refresh })

    await realtime.start()
    refresh.mockClear()
    document.dispatchEvent(new Event('visibilitychange'))
    await Promise.resolve()
    expect(refresh).toHaveBeenCalledOnce()
    await realtime.stop()
  })
})
