import { afterEach, describe, expect, it, vi } from 'vitest'

import { installCapacitorRuntimeBridge } from '@/platform/runtime/capacitorBridge'

const originalCapacitor = Object.getOwnPropertyDescriptor(window, 'Capacitor')

afterEach(() => {
  if (originalCapacitor === undefined)
    delete (window as unknown as { Capacitor?: unknown }).Capacitor
  else Object.defineProperty(window, 'Capacitor', originalCapacitor)
  delete window.industrialPlatformRuntime
})

describe('Capacitor update bridge', () => {
  it('forwards native download bytes and removes its progress listener', async () => {
    let progressListener:
      ((event: { downloadedBytes: string; totalBytes?: string }) => void) | null = null
    const remove = vi.fn(async () => undefined)
    const native = {
      addListener: vi.fn(async (_name: string, listener: typeof progressListener) => {
        progressListener = listener
        return { remove }
      }),
      downloadUpdate: vi.fn(async () => {
        progressListener?.({ downloadedBytes: '524288', totalBytes: '1048576' })
      }),
    }
    Object.defineProperty(window, 'Capacitor', {
      configurable: true,
      value: {
        isNativePlatform: () => true,
        getPlatform: () => 'android',
        Plugins: { IndustrialRuntime: native },
      },
    })

    installCapacitorRuntimeBridge()
    const onProgress = vi.fn()
    await window.industrialPlatformRuntime?.updater?.download?.(
      { releaseNId: 'w2', version: '1.0.1', packageKind: 'web', artifactHash: 'a'.repeat(64) },
      onProgress,
    )

    expect(native.addListener).toHaveBeenCalledWith('updateProgress', expect.any(Function))
    expect(onProgress).toHaveBeenCalledExactlyOnceWith('524288', '1048576')
    expect(remove).toHaveBeenCalledOnce()
  })
})

describe('Capacitor scanner bridge', () => {
  it('installs the missing Google scanner module before opening the camera', async () => {
    let available = false
    const scan = vi.fn(async () => ({ barcodes: [{ rawValue: '000123ABC', displayValue: '123ABC', format: 'QR_CODE' }] }))
    const installGoogleBarcodeScannerModule = vi.fn(async () => { available = true })
    Object.defineProperty(window, 'Capacitor', {
      configurable: true,
      value: {
        isNativePlatform: () => true,
        getPlatform: () => 'android',
        Plugins: {
          IndustrialRuntime: {},
          BarcodeScanner: {
            isGoogleBarcodeScannerModuleAvailable: async () => ({ available }),
            installGoogleBarcodeScannerModule,
            scan,
          },
        },
      },
    })

    installCapacitorRuntimeBridge()
    const result = await window.industrialPlatformRuntime?.scanner?.scanOnce?.({
      sessionNId: 'scan-1', ownerPageId: 'pda.runtime.scanner-test', formats: ['qr_code'],
    })

    expect(installGoogleBarcodeScannerModule).toHaveBeenCalledOnce()
    expect(scan).toHaveBeenCalledOnce()
    expect(result).toEqual({ value: '000123ABC', symbology: 'QR_CODE' })
  })

  it('does not open the camera after its scanner session is released during installation', async () => {
    let finishInstall: (() => void) | undefined
    let available = false
    const scan = vi.fn(async () => ({ barcodes: [] }))
    Object.defineProperty(window, 'Capacitor', {
      configurable: true,
      value: {
        isNativePlatform: () => true,
        getPlatform: () => 'android',
        Plugins: {
          IndustrialRuntime: {},
          BarcodeScanner: {
            isGoogleBarcodeScannerModuleAvailable: async () => ({ available }),
            installGoogleBarcodeScannerModule: () => new Promise<void>((resolve) => {
              finishInstall = () => { available = true; resolve() }
            }),
            scan,
          },
        },
      },
    })

    installCapacitorRuntimeBridge()
    const request = window.industrialPlatformRuntime?.scanner?.scanOnce?.({
      sessionNId: 'scan-2', ownerPageId: 'pda.runtime.scanner-test', formats: ['code_128'],
    })
    await vi.waitFor(() => expect(finishInstall).toBeDefined())
    window.industrialPlatformRuntime?.scanner?.release?.('scan-2')
    finishInstall?.()

    expect(await request).toBeNull()
    expect(scan).not.toHaveBeenCalled()
  })
})

describe('Capacitor screen bridge', () => {
  it('turns an approved Android frame into a WebRTC track and explicitly releases native capture', async () => {
    const listeners = new Map<string, (event: unknown) => void>()
    const remove = vi.fn(async () => undefined)
    let ended: (() => void) | undefined
    const track = {
      addEventListener: vi.fn((_name: string, listener: () => void) => { ended = listener }),
      requestFrame: vi.fn(),
      stop: vi.fn(),
    }
    const stream = { getVideoTracks: () => [track], getTracks: () => [track] } as unknown as MediaStream
    const drawImage = vi.fn()
    vi.spyOn(HTMLCanvasElement.prototype, 'getContext').mockReturnValue({ drawImage } as unknown as CanvasRenderingContext2D)
    const originalCaptureStream = Object.getOwnPropertyDescriptor(HTMLCanvasElement.prototype, 'captureStream')
    Object.defineProperty(HTMLCanvasElement.prototype, 'captureStream', {
      configurable: true, value: vi.fn(() => stream),
    })
    const originalImage = globalThis.Image
    class LoadedImage {
      onload: (() => void) | null = null
      onerror: (() => void) | null = null
      set src(_value: string) { queueMicrotask(() => this.onload?.()) }
    }
    Object.defineProperty(globalThis, 'Image', { configurable: true, value: LoadedImage })
    const stopScreenCapture = vi.fn(async () => undefined)
    const native = {
      addListener: vi.fn(async (name: string, listener: (event: unknown) => void) => {
        listeners.set(name, listener)
        return { remove }
      }),
      startScreenCapture: vi.fn(async () => {
        listeners.get('screenFrame')?.({ jpeg: 'frame', width: 720, height: 1280 })
      }),
      stopScreenCapture,
    }
    try {
      Object.defineProperty(window, 'Capacitor', {
        configurable: true,
        value: {
          isNativePlatform: () => true,
          getPlatform: () => 'android',
          Plugins: { IndustrialRuntime: native },
        },
      })
      installCapacitorRuntimeBridge()
      const captured = await window.industrialPlatformRuntime?.media?.captureScreen?.({
        operationNId: 'screen-1', subjectEpoch: 'android-runtime',
      })
      expect(captured).toBe(stream)
      expect(native.startScreenCapture).toHaveBeenCalledOnce()
      expect(drawImage).toHaveBeenCalledOnce()
      expect(track.requestFrame).toHaveBeenCalledOnce()
      await window.industrialPlatformRuntime?.media?.releaseScreenCapture?.()
      ended?.()
      await vi.waitFor(() => expect(stopScreenCapture).toHaveBeenCalledOnce())
      expect(track.stop).toHaveBeenCalledOnce()
      expect(remove).toHaveBeenCalledTimes(2)
    } finally {
      Object.defineProperty(globalThis, 'Image', { configurable: true, value: originalImage })
      if (originalCaptureStream) Object.defineProperty(HTMLCanvasElement.prototype, 'captureStream', originalCaptureStream)
      else delete (HTMLCanvasElement.prototype as Partial<HTMLCanvasElement>).captureStream
      vi.restoreAllMocks()
    }
  })
})
