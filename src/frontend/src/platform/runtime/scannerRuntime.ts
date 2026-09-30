import { getRuntimeBridge } from './runtimeBridge'

export type ScannerSource = 'keyboard' | 'broadcast' | 'camera'

export interface ScanResultV1 {
  contractVersion: 1
  eventNId: string
  sessionNId: string
  value: string
  source: ScannerSource
  receivedOn: string
  subjectEpoch: string
  symbology?: string
}

export interface ScannerSession {
  sessionNId: string
  ownerPageId: string
  source: ScannerSource
  subjectEpoch: string
  acquiredOn: string
}

export interface ScannerError {
  code: string
  retryable: boolean
}

export interface ScannerBridge {
  scanner?: {
    scanOnce?: (request: {
      sessionNId: string
      ownerPageId: string
      formats: string[]
    }) => Promise<{ value: string; symbology?: string } | ScanResultV1 | null>
    subscribe?: (listener: (result: ScanResultV1) => void) => () => void
    release?: (sessionNId: string) => Promise<void> | void
  }
}

export interface ScannerRuntimeOptions {
  subjectEpoch?: string
  idFactory?: () => string
  now?: () => Date
  bridge?: ScannerBridge | null
}

export interface ScannerRuntime {
  readonly subjectEpoch: string
  readonly cameraAvailable: boolean
  readonly nativeCameraAvailable: boolean
  acquire(input: { ownerPageId: string; source: ScannerSource; subjectEpoch?: string }): ScannerSession
  subscribe(sessionNId: string, listener: (result: ScanResultV1) => void): () => void
  publish(input: {
    sessionNId: string
    subjectEpoch: string
    source: ScannerSource
    value: string
    eventNId?: string
    receivedOn?: string
    symbology?: string
  }): boolean
  release(sessionNId: string): void
  reset(
    sessionNId: string,
    input: { ownerPageId: string; source: ScannerSource; subjectEpoch: string },
  ): Promise<ScannerSession>
  scanNativeOnce(session: ScannerSession): Promise<boolean>
  scanImage(session: ScannerSession, file: File): Promise<boolean>
}

interface BarcodeDetectorLike {
  detect(source: ImageBitmap): Promise<readonly { rawValue?: string }[]>
}

interface BarcodeDetectorConstructor {
  new (options?: { formats?: string[] }): BarcodeDetectorLike
}

function randomId(): string {
  const uuid = globalThis.crypto?.randomUUID?.()
  if (uuid !== undefined) return uuid.replaceAll('-', '')
  return `${Date.now().toString(16)}${Math.random().toString(16).slice(2)}`.padEnd(32, '0').slice(0, 32)
}

function barcodeDetector(): BarcodeDetectorConstructor | null {
  const constructor = (globalThis as typeof globalThis & { BarcodeDetector?: BarcodeDetectorConstructor })
    .BarcodeDetector
  return constructor ?? null
}

function hasImageDecodeSupport(): boolean {
  return barcodeDetector() !== null && typeof globalThis.createImageBitmap === 'function'
}

function isValidValue(value: string): boolean {
  return value.length > 0 && value.length <= 4096
}

function normalizeBridgeResult(
  value: {
    value: string
    eventNId?: string
    symbology?: string
    receivedOn?: string
  },
): { value: string; eventNId?: string; symbology?: string; receivedOn?: string } {
  return {
    value: value.value,
    ...(value.eventNId === undefined ? {} : { eventNId: value.eventNId }),
    ...(value.symbology === undefined ? {} : { symbology: value.symbology }),
    ...(value.receivedOn === undefined ? {} : { receivedOn: value.receivedOn }),
  }
}

export function createScannerRuntime(options: ScannerRuntimeOptions = {}): ScannerRuntime {
  const idFactory = options.idFactory ?? randomId
  const now = options.now ?? (() => new Date())
  const subjectEpoch = options.subjectEpoch ?? idFactory()
  const bridge = options.bridge ?? getRuntimeBridge()
  const listeners = new Map<string, Set<(result: ScanResultV1) => void>>()
  const seenEventNIds = new Set<string>()
  let activeSession: ScannerSession | null = null
  let bridgeUnsubscribe: (() => void) | null = null

  function clearBridgeSubscription(): void {
    bridgeUnsubscribe?.()
    bridgeUnsubscribe = null
  }

  function ensureBridgeSubscription(session: ScannerSession): void {
    if (session.source !== 'broadcast' || bridgeUnsubscribe !== null) return
    const subscribe = bridge?.scanner?.subscribe
    if (subscribe === undefined) return
    bridgeUnsubscribe = subscribe((result) => {
      publish({
        sessionNId: result.sessionNId,
        subjectEpoch: result.subjectEpoch,
        source: result.source,
        value: result.value,
        eventNId: result.eventNId,
        receivedOn: result.receivedOn,
        ...(result.symbology === undefined ? {} : { symbology: result.symbology }),
      })
    })
  }

  function acquire(input: {
    ownerPageId: string
    source: ScannerSource
    subjectEpoch?: string
  }): ScannerSession {
    if (activeSession !== null) release(activeSession.sessionNId)
    const session: ScannerSession = {
      sessionNId: idFactory(),
      ownerPageId: input.ownerPageId,
      source: input.source,
      subjectEpoch: input.subjectEpoch ?? subjectEpoch,
      acquiredOn: now().toISOString(),
    }
    activeSession = session
    listeners.set(session.sessionNId, new Set())
    ensureBridgeSubscription(session)
    return session
  }

  function subscribe(sessionNId: string, listener: (result: ScanResultV1) => void): () => void {
    const sessionListeners = listeners.get(sessionNId)
    if (sessionListeners === undefined) return () => undefined
    sessionListeners.add(listener)
    return () => sessionListeners.delete(listener)
  }

  function publish(input: {
    sessionNId: string
    subjectEpoch: string
    source: ScannerSource
    value: string
    eventNId?: string
    receivedOn?: string
    symbology?: string
  }): boolean {
    const session = activeSession
    if (
      session === null ||
      session.sessionNId !== input.sessionNId ||
      session.subjectEpoch !== input.subjectEpoch ||
      !(
        session.source === input.source ||
        (session.source === 'broadcast' && input.source === 'keyboard')
      ) ||
      !isValidValue(input.value)
    )
      return false

    const eventNId = input.eventNId ?? idFactory()
    if (seenEventNIds.has(eventNId)) return false
    seenEventNIds.add(eventNId)
    const result: ScanResultV1 = {
      contractVersion: 1,
      eventNId,
      sessionNId: session.sessionNId,
      value: input.value,
      source: input.source,
      receivedOn: input.receivedOn ?? now().toISOString(),
      subjectEpoch: session.subjectEpoch,
      ...(input.symbology === undefined ? {} : { symbology: input.symbology }),
    }
    for (const listener of listeners.get(session.sessionNId) ?? []) listener(result)
    return true
  }

  function release(sessionNId: string): void {
    if (activeSession?.sessionNId !== sessionNId) return
    activeSession = null
    clearBridgeSubscription()
    const releaseNative = bridge?.scanner?.release
    if (releaseNative !== undefined) void releaseNative(sessionNId)
    listeners.delete(sessionNId)
  }

  async function reset(
    sessionNId: string,
    input: { ownerPageId: string; source: ScannerSource; subjectEpoch: string },
  ): Promise<ScannerSession> {
    release(sessionNId)
    return acquire(input)
  }

  async function scanNativeOnce(session: ScannerSession): Promise<boolean> {
    if (activeSession?.sessionNId !== session.sessionNId || bridge?.scanner?.scanOnce === undefined)
      return false
    const result = await bridge.scanner.scanOnce({
      sessionNId: session.sessionNId,
      ownerPageId: session.ownerPageId,
      formats: ['qr_code', 'code_128', 'ean_13', 'ean_8', 'upc_a', 'upc_e'],
    })
    if (result === null) return false
    return publish({
      sessionNId: session.sessionNId,
      subjectEpoch: session.subjectEpoch,
      source: 'camera',
      ...normalizeBridgeResult(result),
    })
  }

  async function scanImage(session: ScannerSession, file: File): Promise<boolean> {
    if (activeSession?.sessionNId !== session.sessionNId || !hasImageDecodeSupport()) return false
    const detector = new (barcodeDetector() as BarcodeDetectorConstructor)({
      formats: ['qr_code', 'code_128', 'ean_13', 'ean_8', 'upc_a', 'upc_e'],
    })
    const bitmap = await globalThis.createImageBitmap(file)
    try {
      const detections = await detector.detect(bitmap)
      const value = detections.find((item) => isValidValue(item.rawValue ?? ''))?.rawValue
      if (value === undefined) return false
      return publish({
        sessionNId: session.sessionNId,
        subjectEpoch: session.subjectEpoch,
        source: 'camera',
        value,
      })
    } finally {
      bitmap.close()
    }
  }

  return {
    subjectEpoch,
    cameraAvailable: bridge?.scanner?.scanOnce !== undefined || hasImageDecodeSupport(),
    nativeCameraAvailable: bridge?.scanner?.scanOnce !== undefined,
    acquire,
    subscribe,
    publish,
    release,
    reset,
    scanNativeOnce,
    scanImage,
  }
}

let defaultRuntime: ScannerRuntime | null = null

export function getScannerRuntime(): ScannerRuntime {
  defaultRuntime ??= createScannerRuntime()
  return defaultRuntime
}
