import type {
  RuntimeCapabilitiesV1,
  RuntimeDeviceStatus,
  RuntimeFileHandle,
  RuntimeLifecycleEvent,
} from './runtimeBridge'
import type { UpdateManifest } from './updateCoordinator'

type CapacitorLike = {
  isNativePlatform?: () => boolean
  getPlatform?: () => string
  Plugins?: Record<string, unknown>
}

type NativeRuntimePlugin = {
  addListener?: <T>(
    eventName: string,
    listener: (event: T) => void,
  ) => Promise<{ remove: () => Promise<void> }>
  startScreenCapture?: () => Promise<void>
  stopScreenCapture?: () => Promise<void>
  getCapabilities?: () => Promise<RuntimeCapabilitiesV1>
  loadAuth?: (options: { key: string }) => Promise<{ value?: string }>
  saveAuth?: (options: { key: string; value: string }) => Promise<void>
  clearAuth?: (options: { key: string }) => Promise<void>
  listDevices?: () => Promise<{ items: RuntimeDeviceStatus[] }>
  checkUpdate?: () => Promise<Record<string, unknown> | undefined>
  confirmUpdateHealthy?: (options: { webVersion: string }) => Promise<{
    pending: boolean
    accepted: boolean
    targetVersion?: string
  }>
  downloadUpdate?: (options: { target: unknown }) => Promise<void>
  verifyUpdate?: (options: { target: unknown }) => Promise<void>
  activateUpdate?: (options: { target: unknown; activationNId: string }) => Promise<void>
  rollbackUpdate?: (options: { target: unknown }) => Promise<void>
  pickFile?: (options: {
    mimeTypes: string[]
    maxBytes: string
  }) => Promise<RuntimeFileHandle | undefined>
  saveFile?: (options: {
    artifactHandle: string
    displayName: string
  }) => Promise<{ saved: boolean }>
  openSettings?: (options: { capability: string }) => Promise<{ opened: boolean }>
  getPrinterCapabilities?: (options: { profileNId: string }) => Promise<{
    protocol: string
    receiptLevel: 'SystemOnly'
    supportedFormats: string[]
  }>
  printTest?: (options: { profileNId: string }) => Promise<{
    submission: 'SubmittedToSystem' | 'Cancelled'
    receiptLevel: 'SystemOnly'
  }>
  exportDiagnostics?: (options: { consent: true }) => Promise<RuntimeFileHandle>
  showNotification?: (options: {
    eventNId: string
    titleKey: 'newMessage' | 'collaborationInvite'
    target: { kind: 'conversation'; nId: string }
  }) => Promise<{ accepted: boolean }>
  exit?: () => Promise<void>
}

let releaseAndroidScreen: (() => void) | null = null

type BarcodeScannerPlugin = {
  isGoogleBarcodeScannerModuleAvailable?: () => Promise<{ available: boolean }>
  installGoogleBarcodeScannerModule?: () => Promise<void>
  scan?: () => Promise<{ barcodes: Array<{ rawValue?: string; displayValue: string; format?: string }> }>
}

async function prepareBarcodeScanner(scanner: BarcodeScannerPlugin): Promise<void> {
  const check = scanner.isGoogleBarcodeScannerModuleAvailable
  if (check === undefined || scanner.installGoogleBarcodeScannerModule === undefined)
    throw new Error('RUNTIME_UNSUPPORTED')
  if ((await check()).available) return
  try {
    await scanner.installGoogleBarcodeScannerModule()
  } catch {
    throw new Error('SCANNER_MODULE_UNAVAILABLE')
  }
  for (let attempt = 0; attempt < 60; attempt += 1) {
    if ((await check()).available) return
    await new Promise((resolve) => setTimeout(resolve, 1000))
  }
  throw new Error('SCANNER_MODULE_UNAVAILABLE')
}

async function captureAndroidScreen(native: NativeRuntimePlugin): Promise<MediaStream> {
  if (releaseAndroidScreen !== null) throw new Error('MEDIA_BUSY')
  if (native.startScreenCapture === undefined || native.stopScreenCapture === undefined ||
      native.addListener === undefined || HTMLCanvasElement.prototype.captureStream === undefined)
    throw new Error('MEDIA_UNSUPPORTED')

  const canvas = document.createElement('canvas')
  canvas.width = 2
  canvas.height = 2
  const context = canvas.getContext('2d')
  if (context === null) throw new Error('MEDIA_UNSUPPORTED')

  let stream: MediaStream | null = null
  let closed = false
  let drawing = false
  let resolveFirst!: (stream: MediaStream) => void
  let rejectFirst!: (reason: Error) => void
  const firstFrame = new Promise<MediaStream>((resolve, reject) => {
    resolveFirst = resolve
    rejectFirst = reject
  })
  // The OS consent call may reject before the first-frame promise is awaited.
  void firstFrame.catch(() => undefined)
  const subscriptions: Array<{ remove: () => Promise<void> }> = []
  const close = (error?: Error) => {
    if (closed) return
    closed = true
    if (releaseAndroidScreen === close) releaseAndroidScreen = null
    clearTimeout(timeout)
    subscriptions.forEach((subscription) => { void subscription.remove() })
    if (stream === null) rejectFirst(error ?? new Error('MEDIA_CAPTURE_FAILED'))
    stream?.getTracks().forEach((track) => track.stop())
    void native.stopScreenCapture?.()
  }
  const timeout = window.setTimeout(() => close(new Error('MEDIA_CAPTURE_FAILED')), 15000)
  releaseAndroidScreen = close

  try {
    subscriptions.push(await native.addListener('screenFrame', (event: { jpeg: string; width: number; height: number }) => {
      if (closed || drawing || !event.jpeg) return
      drawing = true
      const image = new Image()
      image.onload = () => {
        drawing = false
        if (closed) return
        if (canvas.width !== event.width || canvas.height !== event.height) {
          canvas.width = event.width
          canvas.height = event.height
        }
        context.drawImage(image, 0, 0)
        if (stream === null) {
          stream = canvas.captureStream(5)
          const track = stream.getVideoTracks()[0]
          if (track === undefined) {
            close(new Error('MEDIA_CAPTURE_FAILED'))
            return
          }
          track.addEventListener('ended', () => close(), { once: true })
          clearTimeout(timeout)
          resolveFirst(stream)
        }
        // Explicitly submit each native frame; hidden WebViews throttle timer-driven canvas capture.
        (stream.getVideoTracks()[0] as CanvasCaptureMediaStreamTrack | undefined)?.requestFrame?.()
      }
      image.onerror = () => { drawing = false }
      image.src = `data:image/jpeg;base64,${event.jpeg}`
    }))
    subscriptions.push(await native.addListener('screenCaptureStopped', (event: { errorCode?: string }) => {
      close(new Error(event.errorCode ?? 'MEDIA_CAPTURE_FAILED'))
    }))
    await native.startScreenCapture()
    return await firstFrame
  } catch (error) {
    close(error instanceof Error ? error : new Error('MEDIA_CAPTURE_FAILED'))
    throw error
  }
}

type CapacitorAppPlugin = {
  addListener?: (
    eventName: 'appStateChange',
    listener: (event: { isActive: boolean }) => void,
  ) => Promise<{ remove: () => Promise<void> }>
}

export function installCapacitorRuntimeBridge(): void {
  const capacitor = (globalThis.window as unknown as { Capacitor?: CapacitorLike } | undefined)
    ?.Capacitor
  if (capacitor?.isNativePlatform?.() !== true || capacitor.getPlatform?.() !== 'android') return
  const plugins = capacitor.Plugins ?? {}
  const native = plugins.IndustrialRuntime as NativeRuntimePlugin | undefined
  const scanner = plugins.BarcodeScanner as BarcodeScannerPlugin | undefined
  const appPlugin = plugins.App as CapacitorAppPlugin | undefined
  if (native === undefined) return

  let subjectEpoch = 'android-runtime'
  let activeScannerSessionNId: string | null = null
  void native.getCapabilities?.().then((snapshot) => {
    if (snapshot?.subjectEpoch !== undefined) subjectEpoch = snapshot.subjectEpoch
  })

  window.industrialPlatformRuntime = {
    capabilities: {
      get: async () => {
        const snapshot = await native.getCapabilities?.()
        if (snapshot === undefined) throw new Error('RUNTIME_CAPABILITIES_UNAVAILABLE')
        return snapshot
      },
    },
    authStorage: {
      load: async (key: string) => (await native.loadAuth?.({ key }))?.value ?? null,
      save: async (key: string, value: string) => {
        await native.saveAuth?.({ key, value })
      },
      clear: async (key: string) => {
        await native.clearAuth?.({ key })
      },
    },
    devices: {
      list: async () => (await native.listDevices?.())?.items ?? [],
    },
    diagnostics: {
      export: async (input: { consent: true }) => {
        if (native.exportDiagnostics === undefined) throw new Error('RUNTIME_UNSUPPORTED')
        return native.exportDiagnostics(input)
      },
    },
    files: {
      pick: async (input) => {
        if (native.pickFile === undefined) throw new Error('RUNTIME_UNSUPPORTED')
        return (await native.pickFile(input)) ?? null
      },
      save: async (input) => {
        if (native.saveFile === undefined) throw new Error('RUNTIME_UNSUPPORTED')
        return native.saveFile(input)
      },
    },
    permissions: {
      openSettings: async (capability) => {
        if (native.openSettings === undefined) throw new Error('RUNTIME_UNSUPPORTED')
        return native.openSettings({ capability })
      },
    },
    printer: {
      getCapabilities: async (profileNId) => {
        if (native.getPrinterCapabilities === undefined) throw new Error('RUNTIME_UNSUPPORTED')
        return native.getPrinterCapabilities({ profileNId })
      },
      printTest: async (profileNId) => {
        if (native.printTest === undefined) throw new Error('RUNTIME_UNSUPPORTED')
        return native.printTest({ profileNId })
      },
    },
    media: {
      captureMicrophone: async (input) => {
        void input
        if (navigator.mediaDevices?.getUserMedia === undefined)
          throw new Error('RUNTIME_UNSUPPORTED')
        return navigator.mediaDevices.getUserMedia({ audio: true })
      },
      captureScreen: async (input) => {
        void input
        return captureAndroidScreen(native)
      },
      releaseScreenCapture: () => { releaseAndroidScreen?.() },
    },
    notifications: {
      show: async (input: {
        eventNId: string
        titleKey: 'newMessage' | 'collaborationInvite'
        target: { kind: 'conversation'; nId: string }
      }) => {
        if (native.showNotification === undefined) return { accepted: false }
        return native.showNotification(input)
      },
    },
    lifecycle: {
      exit: async () => {
        if (native.exit === undefined) throw new Error('RUNTIME_UNSUPPORTED')
        await native.exit()
      },
      subscribe: (listener: (event: RuntimeLifecycleEvent) => void) => {
        if (appPlugin?.addListener === undefined) return () => undefined
        let sequence = 0
        let subscription: { remove: () => Promise<void> } | null = null
        void appPlugin
          .addListener('appStateChange', (event) => {
            sequence += 1
            listener({
              state: event.isActive ? 'resume' : 'background',
              subjectEpoch,
              lifecycleSequence: String(sequence),
            })
          })
          .then((value) => {
            subscription = value
          })
        return () => {
          const current = subscription
          if (current !== null) void current.remove()
        }
      },
    },
    updater: {
      confirmHealthy: async (input) => {
        if (native.confirmUpdateHealthy === undefined) return { pending: false, accepted: true }
        return native.confirmUpdateHealthy(input)
      },
      check: async () => {
        const result = await native.checkUpdate?.()
        return result?.releaseNId === undefined ? null : (result as unknown as UpdateManifest)
      },
      download: async (target, onProgress) => {
        if (native.downloadUpdate === undefined) throw new Error('RUNTIME_UNSUPPORTED')
        const subscription =
          onProgress === undefined || native.addListener === undefined
            ? null
            : await native.addListener('updateProgress', (event: { downloadedBytes: string; totalBytes?: string }) => {
                onProgress(event.downloadedBytes, event.totalBytes)
              })
        try {
          await native.downloadUpdate({ target })
        } finally {
          await subscription?.remove()
        }
      },
      verify: async (target) => {
        if (native.verifyUpdate === undefined) throw new Error('RUNTIME_UNSUPPORTED')
        await native.verifyUpdate({ target })
      },
      activate: async (target, activationNId) => {
        if (native.activateUpdate === undefined) throw new Error('RUNTIME_UNSUPPORTED')
        await native.activateUpdate({ target, activationNId })
      },
      rollback: async (target) => {
        if (native.rollbackUpdate === undefined) throw new Error('RUNTIME_UNSUPPORTED')
        await native.rollbackUpdate({ target })
      },
    },
    scanner: {
      scanOnce: async (request) => {
        if (scanner?.scan === undefined) throw new Error('RUNTIME_UNSUPPORTED')
        activeScannerSessionNId = request.sessionNId
        // Google Code Scanner downloads its module on first use; scan() needs no camera permission.
        await prepareBarcodeScanner(scanner)
        if (activeScannerSessionNId !== request.sessionNId) return null
        const result = (await scanner.scan()).barcodes[0]
        return result === undefined
          ? null
          : {
              value: result.rawValue ?? result.displayValue,
              ...(result.format === undefined ? {} : { symbology: result.format }),
            }
      },
      release: (sessionNId) => {
        if (activeScannerSessionNId === sessionNId) activeScannerSessionNId = null
      },
    },
  }
}
