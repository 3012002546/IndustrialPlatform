import type { ScanResultV1 } from './scannerRuntime'
import type { UpdateManifest, UpdateStatusV1 } from './updateCoordinator'

export type RuntimeContainer = 'web' | 'electron' | 'capacitor'
export type RuntimeAvailability = 'Supported' | 'Unsupported' | 'PermissionRequired' | 'Degraded'

export interface RuntimeCapabilityEntry {
  name: string
  availability: RuntimeAvailability
  reasonCode?: string
}

export interface RuntimeCapabilitiesV1 {
  contractVersion: 1
  container: RuntimeContainer
  os: string
  osVersion: string
  nativeVersion?: string
  webVersion: string
  bridgeContractVersion: number
  capabilities: RuntimeCapabilityEntry[]
  subjectEpoch: string
  snapshotSequence: string
}

export interface RuntimeScannerBridge {
  scanOnce?: (request: {
    sessionNId: string
    ownerPageId: string
    formats: string[]
  }) => Promise<{ value: string; symbology?: string } | ScanResultV1 | null>
  subscribe?: (listener: (result: ScanResultV1) => void) => () => void
  release?: (sessionNId: string) => Promise<void> | void
}

export interface RuntimeDeviceBridge {
  list?: () => Promise<readonly RuntimeDeviceStatus[]>
  requestDevice?: (profileNId: string) => Promise<RuntimeDeviceHandle>
  connect?: (deviceHandle: string) => Promise<RuntimeConnection>
  disconnect?: (connectionNId: string) => Promise<{ disconnected: boolean }>
  exchange?: (request: {
    connectionNId: string
    operation: string
    payload: unknown
    requestNId: string
  }) => Promise<unknown>
}

export interface RuntimeConfigSnapshot {
  configRevision: string
  terminal: string
  channel: string
  scannerMode: 'input' | 'broadcast'
  suffix: 'Enter' | 'Tab' | 'None'
  diagnosticConsent: boolean
  environmentNId: string
  deploymentEnvironment: string
}

export interface RuntimeFileHandle {
  artifactHandle: string
  name: string
  sizeBytes: string
}

export interface RuntimeFileBridge {
  pick?: (input: {
    purpose: 'collaborationAttachment'
    mimeTypes: string[]
    maxBytes: string
  }) => Promise<RuntimeFileHandle | null>
  save?: (input: { artifactHandle: string; displayName: string }) => Promise<{ saved: boolean }>
}

export interface RuntimePrinterBridge {
  getCapabilities?: (profileNId: string) => Promise<{
    protocol: string
    receiptLevel: 'SystemOnly'
    supportedFormats: string[]
  }>
  printTest?: (profileNId: string) => Promise<{
    submission: 'SubmittedToSystem' | 'Cancelled'
    receiptLevel: 'SystemOnly'
  }>
}

export interface RuntimeNotificationBridge {
  show?: (input: {
    eventNId: string
    titleKey: 'newMessage' | 'collaborationInvite'
    target: { kind: 'conversation'; nId: string }
  }) => Promise<{ accepted: boolean }>
}

export type RuntimeLifecycleState =
  | 'foreground'
  | 'background'
  | 'resume'
  | 'identityChanged'
  | 'locked'
  | 'suspending'
  | 'terminating'

export interface RuntimeLifecycleEvent {
  state: RuntimeLifecycleState
  subjectEpoch: string
  lifecycleSequence: string
}

export interface RuntimeLifecycleBridge {
  subscribe?: (listener: (event: RuntimeLifecycleEvent) => void) => () => void
  exit?: () => Promise<void> | void
}

export interface RuntimeDiagnosticsBridge {
  export?: (input: { consent: true }) => Promise<RuntimeFileHandle>
}

export interface RuntimeDeviceStatus {
  profileNId: string
  displayName: string
  kind: 'ble' | 'printer' | 'scanner'
  availability: RuntimeAvailability
  connectionState:
    'Disconnected' | 'Connecting' | 'Connected' | 'Disconnecting' | 'Busy' | 'Unknown'
  reasonCode?: string
}

export interface RuntimeDeviceHandle {
  deviceHandle: string
  capabilities: string[]
}

export interface RuntimeConnection {
  connectionNId: string
  state: 'Connected'
}

export interface RuntimeMediaBridge {
  captureMicrophone?: (input: {
    operationNId: string
    subjectEpoch: string
  }) => Promise<MediaStream>
  captureScreen?: (input: { operationNId: string; subjectEpoch: string }) => Promise<MediaStream>
  releaseScreenCapture?: () => Promise<void> | void
  getScreenSources?: () => Promise<readonly RuntimeScreenSource[]>
  releaseScreenSource?: (sourceNId: string) => Promise<void> | void
}

export interface RuntimeScreenSource {
  sourceNId: string
  title: string
  thumbnailDataUrl?: string
}

export interface RuntimeAuthStorageBridge {
  load: (key: string) => Promise<string | null>
  save: (key: string, value: string) => Promise<void>
  clear: (key: string) => Promise<void>
}

export interface RuntimeUpdaterBridge {
  check: () => Promise<UpdateManifest | null>
  confirmHealthy?: (input: { webVersion: string }) => Promise<{
    pending: boolean
    accepted: boolean
    targetVersion?: string
  }>
  download?: (
    target: UpdateManifest,
    onProgress?: (downloadedBytes: string, totalBytes?: string) => void,
  ) => Promise<void>
  verify?: (target: UpdateManifest) => Promise<void>
  activate?: (target: UpdateManifest, activationNId: string) => Promise<void>
  rollback?: (target: UpdateManifest) => Promise<void>
  status?: () => Promise<UpdateStatusV1>
  subscribe?: (listener: (status: UpdateStatusV1) => void) => () => void
}

export interface RuntimeConfigBridge {
  get?: () => Promise<RuntimeConfigSnapshot>
  update?: (input: {
    expectedRevision: string
    scannerMode: 'input' | 'broadcast'
    suffix: 'Enter' | 'Tab' | 'None'
    diagnosticConsent: boolean
  }) => Promise<RuntimeConfigSnapshot>
}

export interface RuntimePermissionBridge {
  openSettings?: (capability: string) => Promise<{ opened: boolean }>
}

export interface RuntimeBridge {
  capabilities?: { get: () => Promise<RuntimeCapabilitiesV1> }
  scanner?: RuntimeScannerBridge
  devices?: RuntimeDeviceBridge
  media?: RuntimeMediaBridge
  config?: RuntimeConfigBridge
  permissions?: RuntimePermissionBridge
  files?: RuntimeFileBridge
  printer?: RuntimePrinterBridge
  notifications?: RuntimeNotificationBridge
  lifecycle?: RuntimeLifecycleBridge
  diagnostics?: RuntimeDiagnosticsBridge
  authStorage?: RuntimeAuthStorageBridge
  updater?: RuntimeUpdaterBridge
}

declare global {
  interface Window {
    industrialPlatformRuntime?: RuntimeBridge
  }
}

export interface RuntimeCapabilityDefaults {
  webVersion: string
  subjectEpoch?: string
}

function randomId(): string {
  const uuid = globalThis.crypto?.randomUUID?.()
  if (uuid !== undefined) return uuid.replaceAll('-', '')
  return `${Date.now().toString(16)}${Math.random().toString(16).slice(2)}`
    .padEnd(32, '0')
    .slice(0, 32)
}

function webCapabilities(defaults: RuntimeCapabilityDefaults): RuntimeCapabilitiesV1 {
  return {
    contractVersion: 1,
    container: 'web',
    os: 'browser',
    osVersion: 'unknown',
    webVersion: defaults.webVersion,
    bridgeContractVersion: 1,
    capabilities: [
      { name: 'scanner.keyboard', availability: 'Supported' },
      {
        name: 'scanner.broadcast',
        availability: 'Unsupported',
        reasonCode: 'CAPABILITY_NOT_VERIFIED',
      },
      {
        name: 'scanner.camera',
        availability: 'Unsupported',
        reasonCode: 'CAPABILITY_NOT_VERIFIED',
      },
      { name: 'bluetooth.ble', availability: 'Unsupported', reasonCode: 'CAPABILITY_NOT_VERIFIED' },
      { name: 'files.pick', availability: 'Supported' },
      { name: 'files.save', availability: 'Supported' },
      { name: 'media.microphone', availability: 'Supported' },
      {
        name: 'media.screenCapture',
        availability: 'Unsupported',
        reasonCode: 'CAPABILITY_NOT_VERIFIED',
      },
      { name: 'notifications.local', availability: 'Supported' },
      {
        name: 'printer.systemTest',
        availability: 'Unsupported',
        reasonCode: 'CAPABILITY_NOT_VERIFIED',
      },
      { name: 'diagnostics.export', availability: 'Supported' },
      {
        name: 'updates.installer',
        availability: 'Unsupported',
        reasonCode: 'CAPABILITY_NOT_VERIFIED',
      },
      {
        name: 'updates.webBundle',
        availability: 'Unsupported',
        reasonCode: 'CAPABILITY_NOT_VERIFIED',
      },
    ],
    subjectEpoch: defaults.subjectEpoch ?? randomId(),
    snapshotSequence: '0',
  }
}

export function getRuntimeBridge(explicit?: RuntimeBridge | null): RuntimeBridge | null {
  return explicit ?? globalThis.window?.industrialPlatformRuntime ?? null
}

export async function createRuntimeCapabilities(
  bridge: RuntimeBridge | null,
  defaults: RuntimeCapabilityDefaults,
): Promise<RuntimeCapabilitiesV1> {
  const get = bridge?.capabilities?.get
  if (get === undefined) return webCapabilities(defaults)
  try {
    return await get()
  } catch {
    return webCapabilities(defaults)
  }
}

const memoryStorage = new Map<string, string>()

export interface RuntimeAuthStorage {
  load(key: string): Promise<string | null>
  save(key: string, value: string): Promise<void>
  clear(key: string): Promise<void>
}

export function createRuntimeAuthStorage(bridge: RuntimeBridge | null): RuntimeAuthStorage {
  if (bridge?.authStorage !== undefined) return bridge.authStorage

  return {
    async load(key) {
      try {
        return globalThis.sessionStorage?.getItem(key) ?? memoryStorage.get(key) ?? null
      } catch {
        return memoryStorage.get(key) ?? null
      }
    },
    async save(key, value) {
      try {
        if (globalThis.sessionStorage !== undefined) {
          globalThis.sessionStorage.setItem(key, value)
          return
        }
      } catch {
        // A restricted browser context falls back to memory for this instance.
      }
      memoryStorage.set(key, value)
    },
    async clear(key) {
      try {
        globalThis.sessionStorage?.removeItem(key)
      } catch {
        // The memory entry is still cleared below.
      }
      memoryStorage.delete(key)
    },
  }
}
