/**
 * 类型安全运行配置:唯一解析入口,业务代码不得直接读取 import.meta.env。
 *
 * 允许的环境变量(见 .env.example):
 *   VITE_API_BASE_URL         Gateway 统一入口,必须是合法 http/https URL
 *   VITE_AUTH_MODE            mock(默认,仅开发/测试)| http(Phase 3 真实 Identity)
 *   VITE_REQUEST_TIMEOUT_MS   正整数,HTTP 超时毫秒数
 *
 * 生产构建启用 mock 必须失败,不得静默切换。
 */

export const DEFAULT_API_BASE_URL = 'http://localhost:5080'
export const DEFAULT_AUTH_MODE = 'mock'
export const DEFAULT_REQUEST_TIMEOUT_MS = 10000

export type AuthMode = 'mock' | 'http'

export interface RuntimeConfig {
  apiBaseUrl: string
  authMode: AuthMode
  requestTimeoutMs: number
}

export interface RuntimeConfigSource {
  /** 是否生产构建(import.meta.env.PROD)。 */
  isProduction: boolean
  /** 原始环境变量记录。 */
  raw: Record<string, string | undefined>
}

export class RuntimeConfigError extends Error {
  constructor(message: string) {
    super(message)
    this.name = 'RuntimeConfigError'
  }
}

function parseBaseUrl(raw: string | undefined): string {
  const value = raw === undefined || raw === '' ? DEFAULT_API_BASE_URL : raw
  let url: URL
  try {
    url = new URL(value)
  } catch {
    throw new RuntimeConfigError(`VITE_API_BASE_URL 必须是合法 URL,当前值: ${value}`)
  }
  if (url.protocol !== 'http:' && url.protocol !== 'https:') {
    throw new RuntimeConfigError(`VITE_API_BASE_URL 仅支持 http/https,当前值: ${value}`)
  }
  return value
}

function parseAuthMode(raw: string | undefined): AuthMode {
  const value = raw === undefined || raw === '' ? DEFAULT_AUTH_MODE : raw
  if (value === 'mock' || value === 'http') return value
  throw new RuntimeConfigError(`VITE_AUTH_MODE 仅支持 mock/http,当前值: ${value}`)
}

function parsePositiveInt(raw: string | undefined, fallback: number, field: string): number {
  if (raw === undefined || raw === '') return fallback
  const n = Number(raw)
  if (!Number.isInteger(n) || n <= 0) {
    throw new RuntimeConfigError(`${field} 必须为正整数,当前值: ${raw}`)
  }
  return n
}

/** 纯解析函数:任意原始环境记录 → RuntimeConfig,非法输入抛 RuntimeConfigError。 */
export function parseRuntimeConfig(source: RuntimeConfigSource): RuntimeConfig {
  const apiBaseUrl = parseBaseUrl(source.raw.VITE_API_BASE_URL)
  const authMode = parseAuthMode(source.raw.VITE_AUTH_MODE)
  const requestTimeoutMs = parsePositiveInt(
    source.raw.VITE_REQUEST_TIMEOUT_MS,
    DEFAULT_REQUEST_TIMEOUT_MS,
    'VITE_REQUEST_TIMEOUT_MS',
  )

  if (source.isProduction && authMode === 'mock') {
    throw new RuntimeConfigError('生产构建禁止启用 mock 认证(VITE_AUTH_MODE=mock)')
  }

  return { apiBaseUrl, authMode, requestTimeoutMs }
}

/** 应用加载入口:基于 import.meta.env 解析运行配置。 */
export function loadRuntimeConfig(): RuntimeConfig {
  return parseRuntimeConfig({
    isProduction: import.meta.env.PROD,
    raw: import.meta.env as Record<string, string | undefined>,
  })
}
