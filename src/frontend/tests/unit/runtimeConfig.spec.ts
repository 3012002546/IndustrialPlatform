import { describe, expect, it } from 'vitest'

import {
  DEFAULT_API_BASE_URL,
  DEFAULT_AUTH_MODE,
  DEFAULT_REQUEST_TIMEOUT_MS,
  loadRuntimeConfig,
  parseRuntimeConfig,
  RuntimeConfigError,
} from '@/config/runtimeConfig'

function parse(raw: Record<string, string | undefined> = {}, isProduction = false) {
  return parseRuntimeConfig({ isProduction, raw })
}

describe('parseRuntimeConfig', () => {
  it('uses defaults when env is empty (authMode defaults to http)', () => {
    const cfg = parse({})
    expect(cfg.apiBaseUrl).toBe(DEFAULT_API_BASE_URL)
    expect(cfg.authMode).toBe(DEFAULT_AUTH_MODE)
    expect(cfg.authMode).toBe('http')
    expect(cfg.requestTimeoutMs).toBe(DEFAULT_REQUEST_TIMEOUT_MS)
  })

  it('explicit mock mode works in non-production', () => {
    const cfg = parse({ VITE_AUTH_MODE: 'mock' })
    expect(cfg.authMode).toBe('mock')
  })

  it('parses custom valid values', () => {
    const cfg = parse({
      VITE_API_BASE_URL: 'https://api.example.com',
      VITE_AUTH_MODE: 'http',
      VITE_REQUEST_TIMEOUT_MS: '3000',
    })
    expect(cfg).toEqual({
      apiBaseUrl: 'https://api.example.com',
      authMode: 'http',
      requestTimeoutMs: 3000,
    })
  })

  it('rejects non-http(s) base url', () => {
    expect(() => parse({ VITE_API_BASE_URL: 'ftp://x' })).toThrow(RuntimeConfigError)
    expect(() => parse({ VITE_API_BASE_URL: 'not-a-url' })).toThrow(RuntimeConfigError)
  })

  it('rejects non-positive or non-numeric timeout', () => {
    expect(() => parse({ VITE_REQUEST_TIMEOUT_MS: '0' })).toThrow(RuntimeConfigError)
    expect(() => parse({ VITE_REQUEST_TIMEOUT_MS: '-5' })).toThrow(RuntimeConfigError)
    expect(() => parse({ VITE_REQUEST_TIMEOUT_MS: 'abc' })).toThrow(RuntimeConfigError)
  })

  it('rejects unknown auth mode', () => {
    expect(() => parse({ VITE_AUTH_MODE: 'sso' })).toThrow(RuntimeConfigError)
  })

  it('fails when production explicitly enables mock auth', () => {
    expect(() => parse({ VITE_AUTH_MODE: 'mock' }, true)).toThrow(RuntimeConfigError)
  })

  it('allows production default (authMode=http) and explicit http', () => {
    // 产品默认即 http,空配置与显式 http 在生产的构建均合法;只有显式 mock 被禁止。
    expect(parse({}, true).authMode).toBe('http')
    expect(parse({ VITE_AUTH_MODE: 'http' }, true).authMode).toBe('http')
  })

  it('allows http auth mode in production', () => {
    const cfg = parse({ VITE_AUTH_MODE: 'http' }, true)
    expect(cfg.authMode).toBe('http')
  })
})

describe('loadRuntimeConfig', () => {
  it('loads defaults from the environment in dev/test', () => {
    const cfg = loadRuntimeConfig()
    expect(cfg.apiBaseUrl).toBe(DEFAULT_API_BASE_URL)
    expect(cfg.requestTimeoutMs).toBe(DEFAULT_REQUEST_TIMEOUT_MS)
  })
})
