import { describe, expect, it, vi } from 'vitest'

import { bootstrapRuntime } from '@/platform/runtime/runtimeBootstrap'
import type { RuntimeBridge, RuntimeCapabilitiesV1 } from '@/platform/runtime/runtimeBridge'

function bridgeWithUpdate(): RuntimeBridge & { calls: string[] } {
  const calls: string[] = []
  return {
    calls,
    capabilities: {
      get: vi.fn(async (): Promise<RuntimeCapabilitiesV1> => ({
        contractVersion: 1,
        container: 'capacitor' as const,
        os: 'android',
        osVersion: '14',
        nativeVersion: '1.0.0',
        webVersion: '1.0.0',
        bridgeContractVersion: 1,
        capabilities: [],
        subjectEpoch: 'android-epoch',
        snapshotSequence: '1',
      })),
    },
    updater: {
      check: vi.fn(async () => {
        calls.push('check')
        return {
          releaseNId: 'release-2',
          version: '1.0.1',
          packageKind: 'web' as const,
          artifactHash: 'a'.repeat(64),
        }
      }),
      download: vi.fn(async () => {
        calls.push('download')
      }),
      verify: vi.fn(async () => {
        calls.push('verify')
      }),
      activate: vi.fn(async () => {
        calls.push('activate')
      }),
      rollback: vi.fn(async () => {
        calls.push('rollback')
      }),
    },
  }
}

describe('runtime bootstrap', () => {
  it('requires the newly loaded bundle to finish host health confirmation', async () => {
    const confirmHealthy = vi.fn(async () => ({
      pending: true,
      accepted: false,
      targetVersion: '1.0.1',
    }))

    await expect(
      bootstrapRuntime({
        bridge: { updater: { check: vi.fn(async () => null), confirmHealthy } },
        currentWebVersion: '1.0.0',
      }),
    ).rejects.toMatchObject({ code: 'UPDATE_HEALTH_REJECTED', targetVersion: '1.0.1' })
    expect(confirmHealthy).toHaveBeenCalledWith({ webVersion: '1.0.0' })
  })

  it('does not report a web update healthy from the old page before reload', async () => {
    const bridge = bridgeWithUpdate()

    await expect(bootstrapRuntime({ bridge, currentWebVersion: '1.0.0' })).rejects.toMatchObject({
      code: 'UPDATE_RELOAD_REQUIRED',
      targetVersion: '1.0.1',
    })

    expect((bridge as RuntimeBridge & { calls: string[] }).calls).toEqual([
      'check',
      'download',
      'verify',
      'activate',
    ])
  })

  it('keeps web entry available only after a successful no-update check', async () => {
    const bridge: RuntimeBridge = {
      updater: { check: vi.fn(async () => null) },
    }

    const runtime = await bootstrapRuntime({ bridge })

    expect(runtime.status.state).toBe('Idle')
    expect(runtime.enterBusiness()).toBe(true)
  })

  it('reports every startup phase so the gate can show progress before app mount', async () => {
    const states: string[] = []
    await bootstrapRuntime({
      bridge: { updater: { check: vi.fn(async () => null) } },
      onStatus: (status) => states.push(status.state),
    })

    expect(states).toEqual(['Idle', 'Checking', 'Idle'])
  })
})
