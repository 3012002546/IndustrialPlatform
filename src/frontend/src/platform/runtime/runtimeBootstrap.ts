import {
  createRuntimeCapabilities,
  getRuntimeBridge,
  type RuntimeBridge,
  type RuntimeCapabilitiesV1,
} from './runtimeBridge'
import {
  createUpdateCoordinator,
  type UpdateAdapter,
  type UpdateCoordinator,
  type UpdateStatusV1,
} from './updateCoordinator'

export interface RuntimeBootstrapOptions {
  bridge?: RuntimeBridge | null
  adapter?: UpdateAdapter
  currentWebVersion?: string
  currentNativeVersion?: string
  subjectEpoch?: string
  onStatus?: (status: UpdateStatusV1) => void
}

export interface RuntimeBootstrapResult {
  bridge: RuntimeBridge | null
  capabilities: RuntimeCapabilitiesV1
  coordinator: UpdateCoordinator
  status: UpdateStatusV1
  enterBusiness(): boolean
}

export class RuntimeBootstrapError extends Error {
  constructor(
    readonly code: string,
    readonly targetVersion?: string,
  ) {
    super(code)
    this.name = 'RuntimeBootstrapError'
  }
}

function currentWebVersion(options: RuntimeBootstrapOptions): string {
  return options.currentWebVersion ?? import.meta.env.VITE_WEB_VERSION ?? '0.1.0'
}

function createBridgeUpdateAdapter(bridge: RuntimeBridge | null): UpdateAdapter {
  const updater = bridge?.updater
  return {
    check: async () => updater?.check() ?? null,
    download: async (target, onProgress) => {
      if (updater?.download === undefined) throw new RuntimeBootstrapError('UPDATE_UNSUPPORTED')
      await updater.download(target, onProgress)
    },
    verify: async (target) => {
      if (updater?.verify === undefined) throw new RuntimeBootstrapError('UPDATE_UNSUPPORTED')
      await updater.verify(target)
    },
    activate: async (target, activationNId) => {
      if (updater?.activate === undefined) throw new RuntimeBootstrapError('UPDATE_UNSUPPORTED')
      await updater.activate(target, activationNId)
    },
    rollback: async (target) => {
      if (updater?.rollback !== undefined) await updater.rollback(target)
    },
  }
}

function targetVersionForHealth(status: UpdateStatusV1): {
  webVersion?: string
  nativeVersion?: string
} {
  if (status.targetVersion === undefined) return {}
  if (status.targetPackageKind === 'native') return { nativeVersion: status.targetVersion }
  return { webVersion: status.targetVersion }
}

export async function bootstrapRuntime(
  options: RuntimeBootstrapOptions = {},
): Promise<RuntimeBootstrapResult> {
  const bridge = getRuntimeBridge(options.bridge)
  let capabilities = await createRuntimeCapabilities(bridge, {
    webVersion: currentWebVersion(options),
    ...(options.subjectEpoch === undefined ? {} : { subjectEpoch: options.subjectEpoch }),
  })
  const loadedWebVersion = currentWebVersion(options)
  const pendingHealth = await bridge?.updater?.confirmHealthy?.({ webVersion: loadedWebVersion })
  if (pendingHealth?.pending === true && !pendingHealth.accepted) {
    throw new RuntimeBootstrapError('UPDATE_HEALTH_REJECTED', pendingHealth.targetVersion)
  }
  const coordinator = createUpdateCoordinator({
    currentNativeVersion: options.currentNativeVersion ?? capabilities.nativeVersion ?? '0.0.0',
    currentWebVersion: options.currentWebVersion ?? capabilities.webVersion,
    adapter: options.adapter ?? createBridgeUpdateAdapter(bridge),
  })
  const unsubscribe =
    options.onStatus === undefined ? undefined : coordinator.subscribe(options.onStatus)

  try {
    let status = await coordinator.startupCheck()
    if (status.state === 'Available') {
      await coordinator.download()
      await coordinator.verify()
      await coordinator.activate()
      status = coordinator.status
      if (status.activationNId === undefined || status.targetReleaseNId === undefined) {
        throw new RuntimeBootstrapError('UPDATE_ACTIVATION_CONTEXT_MISSING')
      }
      // The current JavaScript still belongs to the old bundle. A host version
      // snapshot alone cannot prove that the newly activated page can load.
      if (
        status.targetPackageKind === 'web' &&
        currentWebVersion(options) !== status.targetVersion
      ) {
        throw new RuntimeBootstrapError('UPDATE_RELOAD_REQUIRED', status.targetVersion)
      }
      const refreshedCapabilities = await createRuntimeCapabilities(bridge, {
        webVersion: capabilities.webVersion,
        subjectEpoch: capabilities.subjectEpoch,
      })
      capabilities = refreshedCapabilities
      if (
        !coordinator.confirmHealthy({
          activationNId: status.activationNId,
          releaseNId: status.targetReleaseNId,
          ...targetVersionForHealth(status),
          ...(refreshedCapabilities.nativeVersion === undefined
            ? {}
            : { nativeVersion: refreshedCapabilities.nativeVersion }),
          webVersion: refreshedCapabilities.webVersion,
        })
      ) {
        throw new RuntimeBootstrapError('UPDATE_HEALTH_REJECTED')
      }
      status = coordinator.status
    }

    if (status.state !== 'Idle' && status.state !== 'Healthy') {
      throw new RuntimeBootstrapError(status.lastErrorCode ?? 'UPDATE_STARTUP_FAILED')
    }
    if (!coordinator.enterBusiness()) throw new RuntimeBootstrapError('UPDATE_BUSINESS_GATE_CLOSED')

    return {
      bridge,
      capabilities,
      coordinator,
      status,
      enterBusiness: () => coordinator.enterBusiness(),
    }
  } finally {
    unsubscribe?.()
  }
}
