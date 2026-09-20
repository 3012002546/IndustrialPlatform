import { describe, expect, it } from 'vitest'

import {
  DEFAULT_API_BASE_URL,
  DEFAULT_AUTH_MODE,
  DEFAULT_REQUEST_TIMEOUT_MS,
  DEFAULT_DEPLOYMENT_ENVIRONMENT,
  loadRuntimeConfig,
  parseRuntimeConfig,
  RuntimeConfigError,
} from '@/config/runtimeConfig'

function parse(
  raw: Record<string, string | undefined> = {},
  isProduction = false,
  pageUrl?: string,
) {
  return parseRuntimeConfig({ isProduction, raw, ...(pageUrl === undefined ? {} : { pageUrl }) })
}

describe('parseRuntimeConfig', () => {
  it('uses defaults when env is empty (authMode defaults to http)', () => {
    const cfg = parse({})
    expect(DEFAULT_API_BASE_URL).toBe('http://localhost:5041')
    expect(cfg.apiBaseUrl).toBe('http://localhost:5041')
    expect(cfg.authMode).toBe(DEFAULT_AUTH_MODE)
    expect(cfg.authMode).toBe('http')
    expect(cfg.requestTimeoutMs).toBe(DEFAULT_REQUEST_TIMEOUT_MS)
    expect(cfg.deploymentEnvironment).toBe('DEV')
  })

  it('explicit mock mode works in non-production', () => {
    const cfg = parse({ VITE_AUTH_MODE: 'mock' })
    expect(cfg.authMode).toBe('mock')
  })

  it('accepts embedded cookie-session mode without changing the http/mock modes', () => {
    const cfg = parse({ VITE_AUTH_MODE: 'embedded' })
    expect(cfg.authMode).toBe('embedded')
  })

  it('keeps the independent build target separate from display mode and platform authentication', () => {
    const standalone = parse({ VITE_AUTH_MODE: 'embedded', MODE: 'collaboration' })
    expect(standalone.standaloneCollaboration).toBe(true)
    expect(parse({ VITE_AUTH_MODE: 'http' }).standaloneCollaboration).toBeUndefined()
    expect(() => parse({ VITE_AUTH_MODE: 'http', MODE: 'collaboration' })).toThrow(RuntimeConfigError)
  })

  it('独立调试和正式构建使用相同的 Standalone 入口', () => {
    const raw = {
      MODE: 'lan-https-collaboration',
      VITE_AUTH_MODE: 'embedded',
      VITE_DEPLOYMENT_ENVIRONMENT: 'PROD',
    }
    expect(parse(raw).standaloneCollaboration).toBe(true)
    expect(parse(raw, true).standaloneCollaboration).toBe(true)
    expect(parse({ ...raw, MODE: 'lan-https' }).standaloneCollaboration).toBeUndefined()
    expect(() => parse({ ...raw, VITE_AUTH_MODE: 'http' })).toThrow(RuntimeConfigError)
  })

  it('从页面 account 参数解析独立演示账户，未提供时不指定用户', () => {
    const raw = {
      MODE: 'lan-https-collaboration',
      VITE_AUTH_MODE: 'embedded',
      VITE_DEPLOYMENT_ENVIRONMENT: 'DEV',
    }
    expect(parse(raw, false, 'https://localhost:5173/pc/collaboration?account=operator-2').embeddedAccount).toBe('operator-2')
    expect(parse(raw, false, 'https://localhost:5173/pc/collaboration').embeddedAccount).toBeUndefined()
  })

  it('只拒绝格式无效或重复的独立演示账户参数', () => {
    const raw = {
      MODE: 'lan-https-collaboration',
      VITE_AUTH_MODE: 'embedded',
      VITE_DEPLOYMENT_ENVIRONMENT: 'DEV',
    }
    expect(parse(raw, false, 'https://localhost:5173/pc/collaboration?account=operator-42').embeddedAccount).toBe('operator-42')
    expect(() => parse(raw, false, 'https://localhost:5173/pc/collaboration?account=operator-1&account=operator-2')).toThrow(
      RuntimeConfigError,
    )
  })

  it('forwards one non-demo embedded account without treating it as a fixed whitelist', () => {
    const config = parse(
      { VITE_AUTH_MODE: 'embedded', VITE_DEPLOYMENT_ENVIRONMENT: 'TEST' },
      false,
      'https://mes.example.test/collaboration?account=mes-user-42',
    )
    expect(config.embeddedAccount).toBe('mes-user-42')
    expect(() => parse(
      { VITE_AUTH_MODE: 'embedded', VITE_DEPLOYMENT_ENVIRONMENT: 'TEST' },
      false,
      'https://mes.example.test/collaboration?account=',
    )).toThrow(RuntimeConfigError)
  })

  it.each(['', 'account=', 'account=operator-1&account=operator-2', 'account=%20operator-1', 'account=operator-42'])(
    'keeps demo and formal account rules separate for query "%s"',
    (query) => {
      const pageUrl = `https://localhost:5173/pc/collaboration?${query}`
      const raw = { VITE_AUTH_MODE: 'embedded', MODE: 'lan-https-collaboration' }
      if (query === '') {
        const config = parse(raw, false, pageUrl)
        expect(config.embeddedAccount).toBeUndefined()
        expect(config.embeddedAccount).toBeUndefined()
      } else if (query === 'account=operator-42') {
        expect(parse(raw, false, pageUrl).embeddedAccount).toBe('operator-42')
      } else {
        expect(() => parse(raw, false, pageUrl)).toThrow(RuntimeConfigError)
      }
      if (query === 'account=operator-42') {
        expect(parse({ ...raw, MODE: 'production', VITE_DEPLOYMENT_ENVIRONMENT: 'PROD' }, true, pageUrl).embeddedAccount).toBe('operator-42')
      }
      // 平台模式不消费嵌入账户，不能因其格式或演示白名单影响原有入口。
      expect(parse({ VITE_AUTH_MODE: 'http' }, false, pageUrl).embeddedAccount).toBeUndefined()
    },
  )

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
      deploymentEnvironment: 'DEV',
    })
  })

  it('rejects non-http(s) base url', () => {
    expect(() => parse({ VITE_API_BASE_URL: 'ftp://x' })).toThrow(RuntimeConfigError)
    expect(() => parse({ VITE_API_BASE_URL: 'not-a-url' })).toThrow(RuntimeConfigError)
  })

  it('resolves LAN API paths against the visiting browser origin for HTTP and SignalR', () => {
    for (const pageOrigin of ['http://192.168.1.20:5173', 'https://debug.example.test:5173']) {
      const config = parseRuntimeConfig({
        isProduction: false,
        pageOrigin,
        raw: { VITE_API_BASE_URL: '/_backend' },
      })
      expect(config.apiBaseUrl).toBe(`${pageOrigin}/_backend`)
      expect(`${config.apiBaseUrl}/collaboration/hubs/collaboration-v1`).toBe(
        `${pageOrigin}/_backend/collaboration/hubs/collaboration-v1`,
      )
    }
  })

  it('rejects protocol-relative and backslash LAN API paths', () => {
    for (const value of ['//untrusted.example/api', '/\\untrusted.example/api']) {
      expect(() =>
        parseRuntimeConfig({
          isProduction: false,
          pageOrigin: 'https://debug.example.test',
          raw: { VITE_API_BASE_URL: value },
        }),
      ).toThrow(RuntimeConfigError)
    }
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

  it('allows production http only with an explicit deployment environment', () => {
    expect(parse({ VITE_DEPLOYMENT_ENVIRONMENT: 'PROD' }, true).authMode).toBe('http')
    expect(
      parse({ VITE_AUTH_MODE: 'http', VITE_DEPLOYMENT_ENVIRONMENT: 'PROD' }, true).authMode,
    ).toBe('http')
  })

  it('allows http auth mode in production', () => {
    const cfg = parse({ VITE_AUTH_MODE: 'http', VITE_DEPLOYMENT_ENVIRONMENT: 'PROD' }, true)
    expect(cfg.authMode).toBe('http')
  })

  it('parses only controlled deployment environments', () => {
    expect(parse({}).deploymentEnvironment).toBe(DEFAULT_DEPLOYMENT_ENVIRONMENT)
    expect(parse({ VITE_DEPLOYMENT_ENVIRONMENT: 'UAT' }).deploymentEnvironment).toBe('UAT')
    expect(() => parse({ VITE_DEPLOYMENT_ENVIRONMENT: 'LOCAL' })).toThrow(RuntimeConfigError)
  })

  it('requires an explicit deployment environment for production', () => {
    expect(() => parse({}, true)).toThrow(RuntimeConfigError)
    expect(parse({ VITE_DEPLOYMENT_ENVIRONMENT: 'PROD' }, true).deploymentEnvironment).toBe('PROD')
  })
})

describe('loadRuntimeConfig', () => {
  it('loads defaults from the environment in dev/test', () => {
    const cfg = loadRuntimeConfig()
    expect(cfg.apiBaseUrl).toBe(DEFAULT_API_BASE_URL)
    expect(cfg.requestTimeoutMs).toBe(DEFAULT_REQUEST_TIMEOUT_MS)
  })
})
