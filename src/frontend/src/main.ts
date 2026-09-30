import { bootstrapRuntime, RuntimeBootstrapError } from '@/platform/runtime/runtimeBootstrap'
import { installCapacitorRuntimeBridge } from '@/platform/runtime/capacitorBridge'
import { createRuntimeStartupGate } from '@/platform/runtime/runtimeStartupGate'

const RELOAD_TARGET_KEY = 'industrial-platform.runtime-reload-target'

function reloadForWebVersion(targetVersion: string): boolean {
  try {
    if (sessionStorage.getItem(RELOAD_TARGET_KEY) === targetVersion) return false
    sessionStorage.setItem(RELOAD_TARGET_KEY, targetVersion)
  } catch {
    return false
  }
  window.location.reload()
  return true
}

function startupErrorCode(error: unknown): string {
  if (
    error !== null &&
    typeof error === 'object' &&
    'code' in error &&
    typeof error.code === 'string'
  )
    return error.code
  return error instanceof Error ? error.name : 'RUNTIME_STARTUP_FAILED'
}

function startupErrorMessage(error: unknown): string {
  if (error instanceof Error && error.message.length > 0) return error.message
  return '启动检查未完成，请重试或退出终端。'
}

function exitApplication(): void {
  const exit = window.industrialPlatformRuntime?.lifecycle?.exit
  if (exit !== undefined) {
    void Promise.resolve(exit()).catch(() => window.close())
    return
  }
  window.close()
}

// 构建时选择入口，使独立产物不打包平台的路由和管理页面。
async function startApplication(): Promise<void> {
  const gate = createRuntimeStartupGate({
    document,
    onRetry: () => void startApplication(),
    onExit: exitApplication,
  })
  try {
    installCapacitorRuntimeBridge()
    // 启动更新门禁必须早于Vue/Router/Auth装配，避免业务页面或旧资源先行可见。
    const runtime = await bootstrapRuntime({ onStatus: gate.update })
    const loadedWebVersion = import.meta.env.VITE_WEB_VERSION ?? '0.1.0'
    if (runtime.bridge !== null && runtime.capabilities.webVersion !== loadedWebVersion) {
      if (!reloadForWebVersion(runtime.capabilities.webVersion)) {
        gate.showError({
          code: 'UPDATE_HEALTH_REJECTED',
          message: '新版本页面未能加载，请重试或联系管理员。',
        })
      }
      return
    }
    try {
      sessionStorage.removeItem(RELOAD_TARGET_KEY)
    } catch {
      // The updater does not rely on browser storage for its authority.
    }
    if (
      import.meta.env.MODE === 'collaboration' ||
      import.meta.env.MODE === 'lan-https-collaboration'
    ) {
      const { createStandaloneCollaborationApp } =
        await import('@/app/createStandaloneCollaborationApp')
      createStandaloneCollaborationApp().mount('#app')
    } else if (new URLSearchParams(window.location.search).getAll('mode').includes('standalone')) {
      document.querySelector('#app')!.textContent = '独立协作页面需要使用独立部署入口。'
    } else {
      const { createIndustrialApp } = await import('@/app/createIndustrialApp')
      createIndustrialApp().mount('#app')
    }
    gate.destroy()
  } catch (error: unknown) {
    if (error instanceof RuntimeBootstrapError && error.code === 'UPDATE_RELOAD_REQUIRED') {
      if (error.targetVersion !== undefined && reloadForWebVersion(error.targetVersion)) return
      gate.showError({
        code: 'UPDATE_HEALTH_REJECTED',
        message: '新版本页面未能加载，请重试或联系管理员。',
      })
      return
    }
    gate.showError({ code: startupErrorCode(error), message: startupErrorMessage(error) })
  }
}

void startApplication()
