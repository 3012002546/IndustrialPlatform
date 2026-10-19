import { describe, expect, it } from 'vitest'

import { createScannerRuntime } from '@/platform/runtime/scannerRuntime'

describe('scanner runtime session ownership', () => {
  it('delivers valid results only to the active session', () => {
    const runtime = createScannerRuntime({
      subjectEpoch: 'epoch-a',
      idFactory: (() => {
        const ids = ['session-a', 'event-a']
        return () => ids.shift() ?? 'generated-id'
      })(),
    })
    const session = runtime.acquire({ ownerPageId: 'pda.runtime.scanner-test', source: 'broadcast' })
    const results: string[] = []
    runtime.subscribe(session.sessionNId, (result) => results.push(result.value))

    expect(
      runtime.publish({
        sessionNId: session.sessionNId,
        subjectEpoch: session.subjectEpoch,
        source: 'broadcast',
        value: '000123ABC',
      }),
    ).toBe(true)
    expect(results).toEqual(['000123ABC'])

    runtime.release(session.sessionNId)
    expect(
      runtime.publish({
        sessionNId: session.sessionNId,
        subjectEpoch: session.subjectEpoch,
        source: 'broadcast',
        value: 'stale',
      }),
    ).toBe(false)
    expect(results).toEqual(['000123ABC'])
  })

  it('invalidates old callbacks when a session is reset', async () => {
    const runtime = createScannerRuntime({ subjectEpoch: 'epoch-a' })
    const first = runtime.acquire({ ownerPageId: 'pda.runtime.scanner-test', source: 'keyboard' })
    const results: string[] = []
    runtime.subscribe(first.sessionNId, (result) => results.push(result.value))

    const second = await runtime.reset(first.sessionNId, {
      ownerPageId: first.ownerPageId,
      source: 'keyboard',
      subjectEpoch: first.subjectEpoch,
    })
    runtime.subscribe(second.sessionNId, (result) => results.push(result.value))

    expect(
      runtime.publish({
        sessionNId: first.sessionNId,
        subjectEpoch: first.subjectEpoch,
      source: 'keyboard',
        value: 'old',
      }),
    ).toBe(false)
    expect(
      runtime.publish({
        sessionNId: second.sessionNId,
        subjectEpoch: second.subjectEpoch,
        source: 'keyboard',
        value: 'new',
      }),
    ).toBe(true)
    expect(results).toEqual(['new'])
  })

  it('keeps broadcast ownership while allowing an explicit keyboard confirmation', () => {
    const runtime = createScannerRuntime({ subjectEpoch: 'epoch-a' })
    const session = runtime.acquire({ ownerPageId: 'pda.runtime.scanner-test', source: 'broadcast' })
    const results: string[] = []
    runtime.subscribe(session.sessionNId, (result) => results.push(`${result.source}:${result.value}`))

    expect(
      runtime.publish({
        sessionNId: session.sessionNId,
        subjectEpoch: session.subjectEpoch,
        source: 'keyboard',
        value: 'manual-confirmation',
      }),
    ).toBe(true)
    expect(results).toEqual(['keyboard:manual-confirmation'])
  })

  it('rejects empty and overlong values without creating results', () => {
    const runtime = createScannerRuntime({ subjectEpoch: 'epoch-a' })
    const session = runtime.acquire({ ownerPageId: 'pda.runtime.scanner-test', source: 'keyboard' })
    const results: string[] = []
    runtime.subscribe(session.sessionNId, (result) => results.push(result.value))

    expect(
      runtime.publish({
        sessionNId: session.sessionNId,
        subjectEpoch: session.subjectEpoch,
      source: 'keyboard',
        value: '',
      }),
    ).toBe(false)
    expect(
      runtime.publish({
        sessionNId: session.sessionNId,
        subjectEpoch: session.subjectEpoch,
      source: 'keyboard',
        value: 'x'.repeat(4097),
      }),
    ).toBe(false)
    expect(results).toEqual([])
  })
})
