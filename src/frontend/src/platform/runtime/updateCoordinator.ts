export type UpdateState =
  | 'Idle'
  | 'Checking'
  | 'Available'
  | 'Downloading'
  | 'Verifying'
  | 'Staged'
  | 'Activating'
  | 'Healthy'
  | 'Failed'
  | 'RollingBack'
  | 'RecoveryRequired'

export type UpdatePackageKind = 'web' | 'native'

export interface UpdateManifest {
  releaseNId: string
  version: string
  packageKind: UpdatePackageKind
  artifactHash: string
}

export interface UpdateStatusV1 {
  contractVersion: 1
  updateEpoch: string
  stateRevision: string
  state: UpdateState
  currentNativeVersion: string
  currentWebVersion: string
  targetReleaseNId?: string
  targetVersion?: string
  targetPackageKind?: UpdatePackageKind
  downloadedBytes: string
  totalBytes?: string
  activationNId?: string
  lastErrorCode?: string
}

export interface UpdateAdapter {
  check(): Promise<UpdateManifest | null>
  download(target: UpdateManifest, onProgress: (downloadedBytes: string, totalBytes?: string) => void): Promise<void>
  verify(target: UpdateManifest): Promise<void>
  activate(target: UpdateManifest, activationNId: string): Promise<void>
  rollback(target: UpdateManifest): Promise<void>
}

export interface UpdateCoordinatorOptions {
  currentNativeVersion: string
  currentWebVersion: string
  adapter: UpdateAdapter
  idFactory?: () => string
}

export interface UpdateCoordinator {
  readonly status: UpdateStatusV1
  startupCheck(): Promise<UpdateStatusV1>
  businessCheck(): Promise<UpdateStatusV1>
  download(): Promise<void>
  verify(): Promise<void>
  activate(): Promise<void>
  confirmHealthy(input: {
    activationNId: string
    releaseNId: string
    webVersion?: string
    nativeVersion?: string
  }): boolean
  enterBusiness(): boolean
  canEnterBusiness(): boolean
  subscribe(listener: (status: UpdateStatusV1) => void): () => void
}

export class UpdateCoordinatorError extends Error {
  constructor(
    readonly code: string,
    readonly retryable: boolean,
  ) {
    super(code)
    this.name = 'UpdateCoordinatorError'
  }
}

function randomId(): string {
  const uuid = globalThis.crypto?.randomUUID?.()
  if (uuid !== undefined) return uuid.replaceAll('-', '')
  return `${Date.now().toString(16)}${Math.random().toString(16).slice(2)}`.padEnd(32, '0').slice(0, 32)
}

function validManifest(manifest: UpdateManifest | null): manifest is UpdateManifest {
  return (
    manifest !== null &&
    manifest.releaseNId.length > 0 &&
    manifest.version.length > 0 &&
    manifest.artifactHash.length === 64 &&
    /^[0-9a-f]+$/u.test(manifest.artifactHash) &&
    (manifest.packageKind === 'web' || manifest.packageKind === 'native')
  )
}

function cloneStatus(status: UpdateStatusV1): UpdateStatusV1 {
  return { ...status }
}

export function createUpdateCoordinator(options: UpdateCoordinatorOptions): UpdateCoordinator {
  const idFactory = options.idFactory ?? randomId
  const listeners = new Set<(status: UpdateStatusV1) => void>()
  let stateRevision = 0
  let updateEpoch = idFactory()
  let target: UpdateManifest | null = null
  let businessAllowed = false
  let phase: 'startup' | 'business' = 'startup'
  let currentStatus: UpdateStatusV1 = {
    contractVersion: 1,
    updateEpoch,
    stateRevision: '0',
    state: 'Idle',
    currentNativeVersion: options.currentNativeVersion,
    currentWebVersion: options.currentWebVersion,
    downloadedBytes: '0',
  }

  function notify(): void {
    const snapshot = cloneStatus(currentStatus)
    for (const listener of listeners) listener(snapshot)
  }

  function transition(state: UpdateState, changes: Partial<UpdateStatusV1> = {}): void {
    stateRevision += 1
    currentStatus = {
      ...currentStatus,
      ...changes,
      state,
      stateRevision: String(stateRevision),
    }
    notify()
  }

  function error(code: string, retryable: boolean): UpdateCoordinatorError {
    return new UpdateCoordinatorError(code, retryable)
  }

  function requireStartup(action: string): void {
    if (phase !== 'startup') throw error('UPDATE_BUSY', true)
    if (action === 'download' && currentStatus.state !== 'Available') throw error('UPDATE_NOT_AVAILABLE', false)
    if (action === 'verify' && currentStatus.state !== 'Verifying') throw error('UPDATE_NOT_DOWNLOADED', false)
    if (action === 'activate' && currentStatus.state !== 'Staged') throw error('UPDATE_NOT_STAGED', false)
  }

  function resetStartupEpoch(): void {
    updateEpoch = idFactory()
    stateRevision = 0
    target = null
    businessAllowed = false
    phase = 'startup'
    currentStatus = {
      contractVersion: 1,
      updateEpoch,
      stateRevision: '0',
      state: 'Idle',
      currentNativeVersion: currentStatus.currentNativeVersion,
      currentWebVersion: currentStatus.currentWebVersion,
      downloadedBytes: '0',
    }
    notify()
  }

  async function check(startup: boolean): Promise<UpdateStatusV1> {
    if (startup) resetStartupEpoch()
    else phase = 'business'
    delete currentStatus.lastErrorCode
    delete currentStatus.targetReleaseNId
    delete currentStatus.targetVersion
    delete currentStatus.targetPackageKind
    delete currentStatus.activationNId
    delete currentStatus.totalBytes
    currentStatus = { ...currentStatus, downloadedBytes: '0' }
    transition('Checking')
    try {
      const manifest = await options.adapter.check()
      if (manifest !== null && !validManifest(manifest)) {
        transition('Failed', { lastErrorCode: 'UPDATE_MANIFEST_INVALID' })
        if (startup) businessAllowed = false
        throw error('UPDATE_MANIFEST_INVALID', false)
      }
      target = manifest
      if (manifest === null) {
        if (startup) businessAllowed = true
        transition('Idle')
      } else {
        if (startup) businessAllowed = false
        transition('Available', {
          targetReleaseNId: manifest.releaseNId,
          targetVersion: manifest.version,
          targetPackageKind: manifest.packageKind,
        })
      }
      return cloneStatus(currentStatus)
    } catch (cause) {
      if (cause instanceof UpdateCoordinatorError) throw cause
      if (startup) businessAllowed = false
      transition('Failed', { lastErrorCode: 'UPDATE_CHECK_FAILED' })
      throw error('UPDATE_CHECK_FAILED', true)
    }
  }

  async function download(): Promise<void> {
    requireStartup('download')
    if (target === null) throw error('UPDATE_NOT_AVAILABLE', false)
    const selectedTarget = target
    delete currentStatus.totalBytes
    delete currentStatus.lastErrorCode
    currentStatus = { ...currentStatus, downloadedBytes: '0' }
    transition('Downloading')
    try {
      await options.adapter.download(selectedTarget, (downloadedBytes, totalBytes) => {
        if (currentStatus.state !== 'Downloading' || target?.releaseNId !== selectedTarget.releaseNId) return
        transition('Downloading', {
          downloadedBytes,
          ...(totalBytes === undefined ? {} : { totalBytes }),
        })
      })
      transition('Verifying')
    } catch {
      businessAllowed = false
      transition('Failed', { lastErrorCode: 'UPDATE_DOWNLOAD_FAILED' })
      throw error('UPDATE_DOWNLOAD_FAILED', true)
    }
  }

  async function verify(): Promise<void> {
    requireStartup('verify')
    if (target === null) throw error('UPDATE_NOT_DOWNLOADED', false)
    const selectedTarget = target
    try {
      await options.adapter.verify(selectedTarget)
      transition('Staged')
    } catch {
      businessAllowed = false
      transition('Failed', { lastErrorCode: 'UPDATE_ARTIFACT_INVALID' })
      throw error('UPDATE_ARTIFACT_INVALID', false)
    }
  }

  async function activate(): Promise<void> {
    requireStartup('activate')
    if (target === null) throw error('UPDATE_NOT_STAGED', false)
    const selectedTarget = target
    const activationNId = idFactory()
    businessAllowed = false
    delete currentStatus.lastErrorCode
    currentStatus = { ...currentStatus, activationNId }
    transition('Activating')
    try {
      await options.adapter.activate(selectedTarget, activationNId)
    } catch {
      transition('RollingBack', { lastErrorCode: 'UPDATE_ACTIVATE_FAILED' })
      try {
        await options.adapter.rollback(selectedTarget)
      } finally {
        transition('RecoveryRequired', { lastErrorCode: 'UPDATE_ACTIVATE_FAILED' })
      }
      throw error('UPDATE_ACTIVATE_FAILED', false)
    }
  }

  function confirmHealthy(input: {
    activationNId: string
    releaseNId: string
    webVersion?: string
    nativeVersion?: string
  }): boolean {
    if (
      currentStatus.state !== 'Activating' ||
      target === null ||
      input.activationNId !== currentStatus.activationNId ||
      input.releaseNId !== target.releaseNId
    )
      return false

    businessAllowed = true
    currentStatus = {
      ...currentStatus,
      ...(input.webVersion === undefined ? {} : { currentWebVersion: input.webVersion }),
      ...(input.nativeVersion === undefined ? {} : { currentNativeVersion: input.nativeVersion }),
    }
    transition('Healthy')
    return true
  }

  return {
    get status() {
      return cloneStatus(currentStatus)
    },
    startupCheck: () => check(true),
    businessCheck: () => check(false),
    download,
    verify,
    activate,
    confirmHealthy,
    enterBusiness(): boolean {
      if (!businessAllowed) return false
      phase = 'business'
      return true
    },
    canEnterBusiness: () => businessAllowed,
    subscribe(listener) {
      listeners.add(listener)
      return () => listeners.delete(listener)
    },
  }
}
