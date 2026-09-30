import type { UpdateStatusV1 } from './updateCoordinator'

export interface RuntimeStartupError {
  code: string
  message: string
}

export interface RuntimeStartupGateOptions {
  document: Document
  onRetry: () => void
  onExit: () => void
}

export interface RuntimeStartupGate {
  update: (status: UpdateStatusV1) => void
  showError: (error: RuntimeStartupError) => void
  destroy: () => void
}

const STYLE_ID = 'industrial-platform-runtime-startup-gate-style'

function formatBytes(value: string | undefined): string {
  if (value === undefined) return '未知大小'
  try {
    const bytes = BigInt(value)
    if (bytes < 1024n) return `${bytes} B`
    if (bytes < 1024n * 1024n) return `${(Number(bytes) / 1024).toFixed(1)} KB`
    if (bytes < 1024n * 1024n * 1024n) return `${(Number(bytes) / (1024 * 1024)).toFixed(1)} MB`
    return `${(Number(bytes) / (1024 * 1024 * 1024)).toFixed(1)} GB`
  } catch {
    return '未知大小'
  }
}

function ensureStyle(document: Document): void {
  if (document.getElementById(STYLE_ID) !== null) return
  const style = document.createElement('style')
  style.id = STYLE_ID
  style.textContent = `
    [data-runtime-startup-gate] { align-items: center; background: #f4f7fb; color: #172033; display: flex; font-family: Inter, "Segoe UI", sans-serif; height: 100vh; justify-content: center; padding: 24px; box-sizing: border-box; }
    [data-runtime-startup-card] { background: #fff; border: 1px solid #dbe3ef; border-radius: 16px; box-shadow: 0 18px 50px rgba(30, 56, 93, .12); max-width: 560px; padding: 32px; width: 100%; }
    [data-runtime-startup-eyebrow] { color: #526581; font-size: 12px; font-weight: 700; letter-spacing: .08em; text-transform: uppercase; }
    [data-runtime-startup-title] { font-size: 24px; line-height: 1.3; margin: 10px 0 8px; }
    [data-runtime-startup-status] { color: #526581; line-height: 1.6; margin: 0 0 20px; white-space: pre-wrap; }
    [data-runtime-startup-progress] { accent-color: #2563eb; height: 8px; margin: 0 0 8px; width: 100%; }
    [data-runtime-startup-progress-label] { color: #526581; font-size: 13px; margin-bottom: 18px; }
    [data-runtime-startup-error] { background: #fff4f2; border: 1px solid #fecaca; border-radius: 10px; color: #991b1b; display: none; line-height: 1.5; margin: 16px 0; padding: 12px; white-space: pre-wrap; }
    [data-runtime-startup-actions] { display: flex; gap: 10px; margin-top: 20px; }
    [data-runtime-startup-actions] button { border: 0; border-radius: 8px; cursor: pointer; font: inherit; min-height: 40px; padding: 0 16px; }
    [data-runtime-startup-retry] { background: #2563eb; color: #fff; }
    [data-runtime-startup-exit] { background: #e8edf5; color: #172033; }
    [data-runtime-startup-actions] button:disabled { cursor: wait; opacity: .65; }
  `
  document.head.append(style)
}

function statusText(status: UpdateStatusV1): string {
  const target = status.targetVersion === undefined ? '' : `（目标 ${status.targetVersion}）`
  switch (status.state) {
    case 'Checking':
      return '正在检查启动更新…'
    case 'Available':
      return `发现可用更新${target}`
    case 'Downloading':
      return `正在下载更新${target}`
    case 'Verifying':
      return '正在校验更新完整性…'
    case 'Staged':
      return '更新已校验，准备切换…'
    case 'Activating':
      return '正在切换版本，期间不会进入业务页面…'
    case 'Healthy':
      return '更新已完成，正在进入终端…'
    case 'RollingBack':
      return '更新失败，正在回滚到上一版本…'
    case 'RecoveryRequired':
      return '更新未能安全完成，需要重试或退出终端。'
    case 'Failed':
      return '启动更新失败，需要重试或退出终端。'
    case 'Idle':
      return '正在准备终端…'
  }
}

export function createRuntimeStartupGate(options: RuntimeStartupGateOptions): RuntimeStartupGate {
  const host = options.document.querySelector<HTMLElement>('#app')
  if (host === null) throw new Error('RUNTIME_STARTUP_HOST_MISSING')
  ensureStyle(options.document)

  const gate = options.document.createElement('main')
  gate.dataset.runtimeStartupGate = 'true'
  gate.setAttribute('aria-live', 'polite')
  const card = options.document.createElement('section')
  card.dataset.runtimeStartupCard = 'true'
  const eyebrow = options.document.createElement('div')
  eyebrow.dataset.runtimeStartupEyebrow = 'true'
  eyebrow.textContent = 'Industrial Platform'
  const title = options.document.createElement('h1')
  title.dataset.runtimeStartupTitle = 'true'
  title.textContent = '正在安全启动终端'
  const status = options.document.createElement('p')
  status.dataset.runtimeStartupStatus = 'true'
  const progress = options.document.createElement('progress')
  progress.dataset.runtimeStartupProgress = 'true'
  progress.max = 100
  progress.hidden = true
  const progressLabel = options.document.createElement('div')
  progressLabel.dataset.runtimeStartupProgressLabel = 'true'
  progressLabel.hidden = true
  const error = options.document.createElement('div')
  error.dataset.runtimeStartupError = 'true'
  error.setAttribute('role', 'alert')
  const actions = options.document.createElement('div')
  actions.dataset.runtimeStartupActions = 'true'
  const retry = options.document.createElement('button')
  retry.dataset.runtimeStartupRetry = 'true'
  retry.type = 'button'
  retry.textContent = '重试启动检查'
  retry.hidden = true
  retry.addEventListener('click', () => {
    retry.disabled = true
    options.onRetry()
  })
  const exit = options.document.createElement('button')
  exit.dataset.runtimeStartupExit = 'true'
  exit.type = 'button'
  exit.textContent = '退出终端'
  exit.hidden = true
  exit.addEventListener('click', options.onExit)
  actions.append(retry, exit)
  card.append(eyebrow, title, status, progress, progressLabel, error, actions)
  gate.append(card)
  host.textContent = ''
  host.append(gate)

  function showError(current: RuntimeStartupError): void {
    status.textContent = '终端没有进入业务页面，避免使用未确认的版本。'
    progress.hidden = true
    progressLabel.hidden = true
    error.textContent = `${current.code}\n${current.message}`
    error.style.display = 'block'
    retry.hidden = false
    exit.hidden = false
  }

  return {
    update(current) {
      if (current.state === 'Failed' || current.state === 'RecoveryRequired') {
        showError({
          code: current.lastErrorCode ?? 'UPDATE_STARTUP_FAILED',
          message: statusText(current),
        })
        return
      }
      error.style.display = 'none'
      retry.hidden = true
      exit.hidden = true
      status.textContent = statusText(current)
      if (current.state === 'Downloading') {
        const downloaded = formatBytes(current.downloadedBytes)
        const total = formatBytes(current.totalBytes)
        progress.hidden = false
        progressLabel.hidden = false
        progressLabel.textContent = `${downloaded} / ${total}`
        if (current.totalBytes === undefined) {
          progress.removeAttribute('value')
        } else {
          const totalBytes = Number(current.totalBytes)
          const downloadedBytes = Number(current.downloadedBytes)
          progress.value = Number.isFinite(totalBytes) && totalBytes > 0
            ? Math.min(100, Math.max(0, (downloadedBytes / totalBytes) * 100))
            : 0
        }
      } else {
        progress.hidden = true
        progressLabel.hidden = true
      }
    },
    showError,
    destroy() {
      if (host.contains(gate)) gate.remove()
    },
  }
}
