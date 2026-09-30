import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import { createRuntimeStartupGate } from '@/platform/runtime/runtimeStartupGate'

describe('runtime startup gate', () => {
  beforeEach(() => {
    const host = document.createElement('div')
    host.id = 'app'
    document.body.append(host)
  })

  afterEach(() => {
    document.body.replaceChildren()
  })

  it('renders download progress before the business app is mounted', () => {
    const gate = createRuntimeStartupGate({
      document,
      onRetry: vi.fn(),
      onExit: vi.fn(),
    })

    gate.update({
      contractVersion: 1,
      updateEpoch: 'epoch',
      stateRevision: '2',
      state: 'Downloading',
      currentNativeVersion: '1.0.0',
      currentWebVersion: '1.0.0',
      targetReleaseNId: 'release-2',
      targetVersion: '2.0.0',
      targetPackageKind: 'web',
      downloadedBytes: '50',
      totalBytes: '100',
    })

    expect(document.querySelector('[data-runtime-startup-gate]')?.textContent).toContain('正在下载更新')
    expect(document.querySelector<HTMLProgressElement>('[data-runtime-startup-progress]')?.value).toBe(50)
    expect(document.querySelector<HTMLButtonElement>('[data-runtime-startup-retry]')?.hidden).toBe(true)
  })

  it('offers retry and exit actions after a startup failure', () => {
    const onRetry = vi.fn()
    const onExit = vi.fn()
    const gate = createRuntimeStartupGate({ document, onRetry, onExit })

    gate.showError({ code: 'UPDATE_DOWNLOAD_FAILED', message: 'network unavailable' })

    document.querySelector<HTMLButtonElement>('[data-runtime-startup-retry]')?.click()
    document.querySelector<HTMLButtonElement>('[data-runtime-startup-exit]')?.click()

    expect(onRetry).toHaveBeenCalledOnce()
    expect(onExit).toHaveBeenCalledOnce()
    expect(document.querySelector('[data-runtime-startup-error]')?.textContent).toContain('network unavailable')
  })
})
