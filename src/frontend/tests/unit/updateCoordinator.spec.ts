import { describe, expect, it } from 'vitest'

import {
  createUpdateCoordinator,
  type UpdateAdapter,
  type UpdateManifest,
} from '@/platform/runtime/updateCoordinator'

const manifest: UpdateManifest = {
  releaseNId: 'release-w2',
  version: '1.0.1',
  packageKind: 'web',
  artifactHash: 'a'.repeat(64),
}

function adapterFor(nextManifest: UpdateManifest | null = manifest) {
  const calls: string[] = []
  const adapter: UpdateAdapter = {
    check: async () => {
      calls.push('check')
      return nextManifest
    },
    download: async (_target, onProgress) => {
      calls.push('download')
      onProgress('10', '10')
    },
    verify: async () => {
      calls.push('verify')
    },
    activate: async () => {
      calls.push('activate')
    },
    rollback: async () => {
      calls.push('rollback')
    },
  }
  return { adapter, calls }
}

describe('update coordinator', () => {
  it('keeps the startup gate closed until a checked target is healthy', async () => {
    const { adapter, calls } = adapterFor()
    const coordinator = createUpdateCoordinator({
      currentNativeVersion: '1.0.0',
      currentWebVersion: '1.0.0',
      adapter,
      idFactory: (() => {
        const ids = ['epoch-a', 'activation-a']
        return () => ids.shift() ?? 'generated-id'
      })(),
    })

    expect(coordinator.canEnterBusiness()).toBe(false)
    expect((await coordinator.startupCheck()).state).toBe('Available')
    expect(coordinator.canEnterBusiness()).toBe(false)

    await coordinator.download()
    expect(coordinator.status.state).toBe('Verifying')
    await coordinator.verify()
    expect(coordinator.status.state).toBe('Staged')
    await coordinator.activate()
    expect(coordinator.status.state).toBe('Activating')

    expect(
      coordinator.confirmHealthy({
        activationNId: coordinator.status.activationNId ?? '',
        releaseNId: manifest.releaseNId,
        webVersion: '1.0.0',
      }),
    ).toBe(false)
    expect(
      coordinator.confirmHealthy({
        activationNId: coordinator.status.activationNId ?? '',
        releaseNId: manifest.releaseNId,
      }),
    ).toBe(false)
    expect(coordinator.canEnterBusiness()).toBe(false)

    const healthy = coordinator.confirmHealthy({
      activationNId: coordinator.status.activationNId ?? '',
      releaseNId: manifest.releaseNId,
      webVersion: manifest.version,
    })
    expect(healthy).toBe(true)
    expect(coordinator.status.state).toBe('Healthy')
    expect(coordinator.canEnterBusiness()).toBe(true)
    expect(calls).toEqual(['check', 'download', 'verify', 'activate'])
  })

  it('allows a business check but rejects download and activation in business phase', async () => {
    const { adapter, calls } = adapterFor()
    const coordinator = createUpdateCoordinator({
      currentNativeVersion: '1.0.0',
      currentWebVersion: '1.0.0',
      adapter,
    })

    expect((await coordinator.startupCheck()).state).toBe('Available')
    await expect(coordinator.download()).resolves.toBeUndefined()
    await coordinator.verify()
    await coordinator.activate()
    expect(
      coordinator.confirmHealthy({
        activationNId: coordinator.status.activationNId ?? '',
        releaseNId: manifest.releaseNId,
        webVersion: manifest.version,
      }),
    ).toBe(true)
    expect(coordinator.enterBusiness()).toBe(true)

    expect((await coordinator.businessCheck()).state).toBe('Available')
    expect(coordinator.canEnterBusiness()).toBe(true)
    await expect(coordinator.download()).rejects.toMatchObject({ code: 'UPDATE_BUSY' })
    await expect(coordinator.activate()).rejects.toMatchObject({ code: 'UPDATE_BUSY' })
    expect(calls).toEqual(['check', 'download', 'verify', 'activate', 'check'])
  })

  it('rejects stale health confirmation and turns activation failure into recovery', async () => {
    const { adapter } = adapterFor()
    const coordinator = createUpdateCoordinator({
      currentNativeVersion: '1.0.0',
      currentWebVersion: '1.0.0',
      adapter,
    })
    await coordinator.startupCheck()
    await coordinator.download()
    await coordinator.verify()
    await coordinator.activate()

    expect(
      coordinator.confirmHealthy({
        activationNId: 'stale-activation',
        releaseNId: manifest.releaseNId,
        webVersion: manifest.version,
      }),
    ).toBe(false)
    expect(coordinator.status.state).toBe('Activating')

    const failingAdapter: UpdateAdapter = {
      ...adapter,
      activate: async () => {
        throw new Error('health never starts')
      },
    }
    const failing = createUpdateCoordinator({
      currentNativeVersion: '1.0.0',
      currentWebVersion: '1.0.0',
      adapter: failingAdapter,
    })
    await failing.startupCheck()
    await failing.download()
    await failing.verify()
    await expect(failing.activate()).rejects.toMatchObject({ code: 'UPDATE_ACTIVATE_FAILED' })
    expect(failing.status.state).toBe('RecoveryRequired')
    expect(failing.canEnterBusiness()).toBe(false)
  })
})
