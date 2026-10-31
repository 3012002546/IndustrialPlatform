import { describe, expect, it, vi } from 'vitest'
import { createCustomBridge } from '@/components/studio/shared/customBridge'

describe('custom page bridge', () => {
  it('rejects foreign frames and wrong channels, deduplicates writes and discards late responses after closure', async () => {
    const postMessage = vi.fn(),
      target = { postMessage } as unknown as Window
    let finish!: (result: unknown) => void
    const execute = vi.fn(
      () =>
        new Promise((resolve) => {
          finish = resolve
        }),
    )
    const bridge = createCustomBridge(() => target, 'document-a', execute)
    const data = {
      bridgeId: 'document-a',
      type: 'request',
      id: '1',
      method: 'executeAction',
      args: { actionId: 'save' },
    }
    await bridge.receive(new MessageEvent('message', { data, source: window }))
    await bridge.receive(
      new MessageEvent('message', { data: { ...data, bridgeId: 'old' }, source: target }),
    )
    expect(execute).not.toHaveBeenCalled()
    const first = bridge.receive(new MessageEvent('message', { data, source: target }))
    const repeated = bridge.receive(new MessageEvent('message', { data, source: target }))
    await Promise.resolve()
    expect(execute).toHaveBeenCalledTimes(1)
    bridge.close()
    finish({ status: 'Succeeded' })
    await Promise.all([first, repeated])
    expect(postMessage).not.toHaveBeenCalled()
  })
  it('replies only to registered methods and refuses changed arguments for an existing request id', async () => {
    const postMessage = vi.fn(),
      target = { postMessage } as unknown as Window
    const execute = vi.fn(async () => ({ rows: [] }))
    const bridge = createCustomBridge(() => target, 'document-a', execute)
    const data = {
      bridgeId: 'document-a',
      type: 'request',
      id: '1',
      method: 'query',
      args: { instanceId: 'batches' },
    }
    await bridge.receive(new MessageEvent('message', { data, source: target }))
    await bridge.receive(
      new MessageEvent('message', {
        data: { ...data, args: { instanceId: 'other' } },
        source: target,
      }),
    )
    expect(execute).toHaveBeenCalledTimes(1)
    expect(postMessage.mock.calls.at(-1)?.[0].error).toBe('STUDIO_IDEMPOTENCY_CONFLICT')
    await bridge.receive(
      new MessageEvent('message', { data: { ...data, id: '2', method: 'fetch' }, source: target }),
    )
    expect(postMessage.mock.calls.at(-1)?.[0].error).toBe('STUDIO_VALIDATION')
    const cyclic: Record<string, unknown> = {}
    cyclic.self = cyclic
    await bridge.receive(
      new MessageEvent('message', { data: { ...data, id: '3', args: cyclic }, source: target }),
    )
    expect(postMessage.mock.calls.at(-1)?.[0].error).toBe('STUDIO_VALIDATION')
    expect(execute).toHaveBeenCalledTimes(1)
  })
})
