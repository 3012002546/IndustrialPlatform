import { delay, http, HttpResponse } from 'msw'
import { afterAll, afterEach, beforeAll, describe, expect, it } from 'vitest'

import { ApiError } from '@/api/errors'
import { createHttpClient, type HttpClient, type HttpClientDeps } from '@/api/httpClient'

import { server } from '../fixtures/mswServer'

const BASE = 'http://localhost:5080'

function client(overrides: Partial<HttpClientDeps> = {}): HttpClient {
  return createHttpClient({
    baseUrl: BASE,
    timeoutMs: 1000,
    getCorrelationId: () => 'corr-001',
    ...overrides,
  })
}

function okResult(data: unknown, code = '200') {
  return HttpResponse.json({ success: true, code, message: 'success', data })
}

async function failureKind(promise: Promise<unknown>): Promise<string> {
  const outcome = await promise.then(
    () => null,
    (error: unknown) => error,
  )
  expect(outcome).toBeInstanceOf(ApiError)
  return (outcome as ApiError).kind
}

beforeAll(() => server.listen({ onUnhandledRequest: 'error' }))
afterEach(() => server.resetHandlers())
afterAll(() => server.close())

describe('HttpClient — 成功信封', () => {
  it('unwraps data from a success envelope', async () => {
    server.use(http.get(`${BASE}/api/users`, () => okResult([{ id: 1, name: 'alice' }])))
    const data = await client().get<{ id: number; name: string }[]>('/api/users')
    expect(data).toEqual([{ id: 1, name: 'alice' }])
  })

  it('sends the X-Correlation-Id header on every request', async () => {
    let captured: string | null = null
    server.use(
      http.get(`${BASE}/api/corr`, (info) => {
        captured = info.request.headers.get('X-Correlation-Id')
        return okResult(null)
      }),
    )
    await client().get('/api/corr')
    expect(captured).toBe('corr-001')
  })

  it('injects Authorization Bearer when a token provider is wired', async () => {
    let auth: string | null = null
    server.use(
      http.get(`${BASE}/api/auth`, (info) => {
        auth = info.request.headers.get('Authorization')
        return okResult(null)
      }),
    )
    await client({ getToken: () => 'tok-1' }).get('/api/auth')
    expect(auth).toBe('Bearer tok-1')
  })
})

describe('HttpClient — 统一错误分类', () => {
  it('maps 2xx success=false to business and preserves code/message', async () => {
    server.use(
      http.get(`${BASE}/api/biz`, () =>
        HttpResponse.json({
          success: false,
          code: 'WO_001',
          message: '工单不存在',
          data: null,
        }),
      ),
    )
    const outcome = await client()
      .get('/api/biz')
      .then(
        () => null,
        (error: unknown) => error,
      )
    expect(outcome).toBeInstanceOf(ApiError)
    const apiError = outcome as ApiError
    expect(apiError.kind).toBe('business')
    expect(apiError.details.code).toBe('WO_001')
    expect(apiError.details.message).toBe('工单不存在')
  })

  it('maps 2xx with an invalid envelope to invalidResponse', async () => {
    server.use(http.get(`${BASE}/api/bad-envelope`, () => HttpResponse.json({ foo: 1 })))
    expect(await failureKind(client().get('/api/bad-envelope'))).toBe('invalidResponse')
  })

  it('maps 401 to unauthorized', async () => {
    server.use(
      http.get(`${BASE}/api/unauthorized`, () =>
        HttpResponse.json(
          { success: false, code: '401', message: 'unauthorized', data: null },
          { status: 401 },
        ),
      ),
    )
    expect(await failureKind(client().get('/api/unauthorized'))).toBe('unauthorized')
  })

  it('maps 403 to forbidden', async () => {
    server.use(
      http.get(`${BASE}/api/forbidden`, () =>
        HttpResponse.json(
          { success: false, code: '403', message: 'forbidden', data: null },
          { status: 403 },
        ),
      ),
    )
    expect(await failureKind(client().get('/api/forbidden'))).toBe('forbidden')
  })

  it('maps 404 to notFound', async () => {
    server.use(
      http.get(`${BASE}/api/missing`, () =>
        HttpResponse.json(
          { success: false, code: '404', message: 'not found', data: null },
          { status: 404 },
        ),
      ),
    )
    expect(await failureKind(client().get('/api/missing'))).toBe('notFound')
  })

  it('maps 5xx to server', async () => {
    server.use(
      http.get(`${BASE}/api/server-error`, () =>
        HttpResponse.json(
          { success: false, code: '500', message: 'boom', data: null },
          { status: 500 },
        ),
      ),
    )
    expect(await failureKind(client().get('/api/server-error'))).toBe('server')
  })

  it('preserves business code/message on a non-2xx business envelope', async () => {
    server.use(
      http.get(`${BASE}/api/validation`, () =>
        HttpResponse.json(
          { success: false, code: '400', message: '参数不合法', data: null },
          { status: 400 },
        ),
      ),
    )
    const outcome = await client()
      .get('/api/validation')
      .then(
        () => null,
        (error: unknown) => error,
      )
    const apiError = outcome as ApiError
    expect(apiError.kind).toBe('business')
    expect(apiError.details.code).toBe('400')
    expect(apiError.details.message).toBe('参数不合法')
  })

  it('maps network failure to network', async () => {
    server.use(http.get(`${BASE}/api/network`, () => HttpResponse.error()))
    expect(await failureKind(client().get('/api/network'))).toBe('network')
  })

  // 真实 socket 超时在 MSW mock 传输层下不触发(axios 仅对原生传输启用
  // connect-phase 定时器,否则退化为 socket idle 事件),timeout/cancelled 分类
  // 由 tests/unit/errors.spec.ts 用真实 AxiosError 实例确定性覆盖。

  it('maps an aborted request to cancelled', async () => {
    server.use(
      http.get(`${BASE}/api/cancel`, async () => {
        await delay(300)
        return okResult(null)
      }),
    )
    const controller = new AbortController()
    const pending = client().get('/api/cancel', { signal: controller.signal })
    setTimeout(() => controller.abort(), 20)
    expect(await failureKind(pending)).toBe('cancelled')
  })
})

describe('HttpClient — TraceId 与敏感日志', () => {
  it('extracts traceId from the X-Trace-Id header on errors', async () => {
    server.use(
      http.get(`${BASE}/api/trace`, () =>
        HttpResponse.json(
          { success: false, code: '500', message: 'boom', data: null },
          { status: 500, headers: { 'X-Trace-Id': 'trace-xyz' } },
        ),
      ),
    )
    const outcome = await client()
      .get('/api/trace')
      .then(
        () => null,
        (error: unknown) => error,
      )
    const apiError = outcome as ApiError
    expect(apiError.details.traceId).toBe('trace-xyz')
    expect(apiError.details.correlationId).toBe('corr-001')
  })

  it('never logs tokens or Authorization header', async () => {
    const logs: string[] = []
    const logger = {
      debug: (message: string) => logs.push(message),
      warn: (message: string) => logs.push(message),
    }
    server.use(
      http.get(`${BASE}/api/secret`, () =>
        HttpResponse.json(
          { success: false, code: '500', message: 'boom', data: null },
          { status: 500 },
        ),
      ),
    )
    await client({ logger, getToken: () => 'super-secret-token' })
      .get('/api/secret')
      .catch(() => undefined)
    const joined = logs.join('\n')
    expect(joined).not.toContain('super-secret-token')
    expect(joined).not.toContain('Authorization')
    expect(joined).not.toContain('Bearer')
  })
})
