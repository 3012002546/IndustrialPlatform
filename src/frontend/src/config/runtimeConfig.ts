/**
 * 类型安全运行配置:唯一解析入口,业务代码不得直接读取 import.meta.env。
 *
 * 允许的环境变量(见 .env.example):
 *   VITE_API_BASE_URL         API 统一入口,Development 默认 UnifiedHost,必须是合法 http/https URL
 *   VITE_AUTH_MODE            http(默认,本地真实 Identity)| mock(仅测试/显式配置)| embedded(HttpOnly宿主会话)
 *   VITE_REQUEST_TIMEOUT_MS   正整数,HTTP 超时毫秒数
 *   VITE_DEPLOYMENT_ENVIRONMENT DEV|TEST|UAT|PROD;生产构建必须显式提供
 *
 * 生产构建启用 mock 必须失败,不得静默切换。
 */

export const DEFAULT_API_BASE_URL = 'http://localhost:5041'
export const DEFAULT_AUTH_MODE = 'http'
export const DEFAULT_REQUEST_TIMEOUT_MS = 10000
export const DEFAULT_DEPLOYMENT_ENVIRONMENT = 'DEV' as const

export type AuthMode = 'mock' | 'http' | 'embedded'
export type DeploymentEnvironment = 'DEV' | 'TEST' | 'UAT' | 'PROD'

export interface RuntimeConfig {
  apiBaseUrl: string
  authMode: AuthMode
  requestTimeoutMs: number
  deploymentEnvironment: DeploymentEnvironment
  /** 仅独立协作开发命令启用：无会话时请求固定 A 演示登录。 */
  embeddedDemoAutoLogin?: boolean
  /** 独立协作演示页级账户；不作为正式认证凭据。 */
  embeddedDemoAccount?: string
  /** Optional MES account selector, forwarded to the server-side current-user adapter. */
  embeddedAccount?: string
}

export interface RuntimeConfigSource {
  /** 是否生产构建(import.meta.env.PROD)。 */
  isProduction: boolean
  /** 原始环境变量记录。 */
  raw: Record<string, string | undefined>
  /** 当前页面Origin；用于显式同源API路径，不从访问者推断后端主机。 */
  pageOrigin?: string
  /** 当前页面完整URL；仅用于独立演示 account 参数解析。 */
  pageUrl?: string
}

export class RuntimeConfigError extends Error {
  constructor(message: string) {
    super(message)
    this.name = 'RuntimeConfigError'
  }
}

function parseBaseUrl(raw: string | undefined, pageOrigin?: string): string {
  const value = raw === undefined || raw === '' ? DEFAULT_API_BASE_URL : raw
  let url: URL
  try {
    url =
      value.startsWith('/') && !value.startsWith('//') && !value.includes('\\')
        ? new URL(value, pageOrigin)
        : new URL(value)
  } catch {
    throw new RuntimeConfigError(`VITE_API_BASE_URL 必须是合法 URL,当前值: ${value}`)
  }
  if (url.protocol !== 'http:' && url.protocol !== 'https:') {
    throw new RuntimeConfigError(`VITE_API_BASE_URL 仅支持 http/https,当前值: ${value}`)
  }
  return value.startsWith('/') ? url.href.replace(/\/$/, '') : value
}

function parseAuthMode(raw: string | undefined): AuthMode {
  const value = raw === undefined || raw === '' ? DEFAULT_AUTH_MODE : raw
  if (value === 'mock' || value === 'http' || value === 'embedded') return value
  throw new RuntimeConfigError(`VITE_AUTH_MODE 仅支持 mock/http/embedded,当前值: ${value}`)
}

function parsePositiveInt(raw: string | undefined, fallback: number, field: string): number {
  if (raw === undefined || raw === '') return fallback
  const n = Number(raw)
  if (!Number.isInteger(n) || n <= 0) {
    throw new RuntimeConfigError(`${field} 必须为正整数,当前值: ${raw}`)
  }
  return n
}

function parseDeploymentEnvironment(
  raw: string | undefined,
  isProduction: boolean,
): DeploymentEnvironment {
  if (raw === undefined || raw === '') {
    if (isProduction) {
      throw new RuntimeConfigError(
        '生产构建必须显式设置 VITE_DEPLOYMENT_ENVIRONMENT=PROD|UAT|TEST|DEV',
      )
    }
    return DEFAULT_DEPLOYMENT_ENVIRONMENT
  }
  if (raw === 'DEV' || raw === 'TEST' || raw === 'UAT' || raw === 'PROD') return raw
  throw new RuntimeConfigError(
    `VITE_DEPLOYMENT_ENVIRONMENT 仅支持 DEV/TEST/UAT/PROD,当前值: ${raw}`,
  )
}

const EMBEDDED_DEMO_ACCOUNTS = new Set(['xxA', 'xxB', 'xxC'])

function parseEmbeddedDemoAccount(account?: string): string {
  if (account !== undefined && !EMBEDDED_DEMO_ACCOUNTS.has(account)) {
    throw new RuntimeConfigError('独立协作演示 account 仅支持 xxA、xxB、xxC，且只能出现一次。')
  }
  return account ?? 'xxA'
}

function parseEmbeddedAccount(pageUrl?: string): string | undefined {
  if (pageUrl === undefined) return undefined
  let url: URL
  try {
    url = new URL(pageUrl)
  } catch {
    throw new RuntimeConfigError('嵌入页面地址无效，无法解析 account 参数。')
  }
  const values = url.searchParams.getAll('account')
  if (values.length > 1 || (values.length === 1 && (values[0] ?? '').trim() === '')) {
    throw new RuntimeConfigError('嵌入页面 account 只能出现一次且不能为空。')
  }
  const value = values[0]
  if (value !== undefined && (value.length > 128 || value !== value.trim())) {
    throw new RuntimeConfigError('嵌入页面 account 格式无效。')
  }
  return value
}

/** 纯解析函数:任意原始环境记录 → RuntimeConfig,非法输入抛 RuntimeConfigError。 */
export function parseRuntimeConfig(source: RuntimeConfigSource): RuntimeConfig {
  const apiBaseUrl = parseBaseUrl(source.raw.VITE_API_BASE_URL, source.pageOrigin)
  const authMode = parseAuthMode(source.raw.VITE_AUTH_MODE)
  const requestTimeoutMs = parsePositiveInt(
    source.raw.VITE_REQUEST_TIMEOUT_MS,
    DEFAULT_REQUEST_TIMEOUT_MS,
    'VITE_REQUEST_TIMEOUT_MS',
  )
  const deploymentEnvironment = parseDeploymentEnvironment(
    source.raw.VITE_DEPLOYMENT_ENVIRONMENT,
    source.isProduction,
  )

  if (source.isProduction && authMode === 'mock') {
    throw new RuntimeConfigError('生产构建禁止启用 mock 认证(VITE_AUTH_MODE=mock)')
  }

  const embeddedDemoAutoLogin =
    !source.isProduction &&
    authMode === 'embedded' &&
    source.raw.MODE === 'lan-https-collaboration'
  const embeddedAccount = authMode === 'embedded' ? parseEmbeddedAccount(source.pageUrl) : undefined

  return {
    apiBaseUrl,
    authMode,
    requestTimeoutMs,
    deploymentEnvironment,
    ...(embeddedDemoAutoLogin
      ? { embeddedDemoAutoLogin: true, embeddedDemoAccount: parseEmbeddedDemoAccount(embeddedAccount) }
      : {}),
    ...(embeddedAccount === undefined ? {} : { embeddedAccount }),
  }
}

/** 应用加载入口:基于 import.meta.env 解析运行配置。 */
export function loadRuntimeConfig(): RuntimeConfig {
  return parseRuntimeConfig({
    isProduction: import.meta.env.PROD,
    raw: import.meta.env as Record<string, string | undefined>,
    pageOrigin: window.location.origin,
    pageUrl: window.location.href,
  })
}
