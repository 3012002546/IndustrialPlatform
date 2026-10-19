import { describe, expect, it, vi } from 'vitest'

import {
  createRuntimeAuthStorage,
  createRuntimeCapabilities,
  getRuntimeBridge,
  type RuntimeBridge,
  type RuntimeCapabilitiesV1,
} from '@/platform/runtime/runtimeBridge'

describe('runtime bridge contract', () => {
  it('uses a safe web capability snapshot when no native bridge is present', async () => {
    const capabilities = await createRuntimeCapabilities(null, {
      webVersion: 'web-test',
      subjectEpoch: 'epoch-test',
    })

    expect(capabilities.container).toBe('web')
    expect(capabilities.nativeVersion).toBeUndefined()
    expect(capabilities.webVersion).toBe('web-test')
    expect(capabilities.subjectEpoch).toBe('epoch-test')
    expect(capabilities.capabilities.find((item) => item.name === 'scanner.camera')?.availability).toBe(
      'Unsupported',
    )
  })

  it('delegates capabilities and secure session storage to the host bridge', async () => {
    const load = vi.fn(async (key: string) => (key === 'session' ? 'payload' : null))
    const save = vi.fn(async () => undefined)
    const clear = vi.fn(async () => undefined)
    const bridge: RuntimeBridge = {
      capabilities: {
        get: vi.fn(async (): Promise<RuntimeCapabilitiesV1> => ({
          contractVersion: 1,
          container: 'electron' as const,
          os: 'windows',
          osVersion: '11',
          nativeVersion: '1.0.0',
          webVersion: '1.0.0',
          bridgeContractVersion: 1,
          capabilities: [],
          subjectEpoch: 'host-epoch',
          snapshotSequence: '1',
        })),
      },
      authStorage: { load, save, clear },
    }

    expect(getRuntimeBridge(bridge)).toBe(bridge)
    const capabilities = await createRuntimeCapabilities(bridge, { webVersion: 'fallback' })
    expect(capabilities.container).toBe('electron')
    expect(capabilities.nativeVersion).toBe('1.0.0')

    const storage = createRuntimeAuthStorage(bridge)
    await expect(storage.load('session')).resolves.toBe('payload')
    await storage.save('session', 'next')
    await storage.clear('session')
    expect(load).toHaveBeenCalledWith('session')
    expect(save).toHaveBeenCalledWith('session', 'next')
    expect(clear).toHaveBeenCalledWith('session')
  })
})
