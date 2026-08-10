/**
 * 统一错误分类单元测试:用真实 AxiosError 实例确定性覆盖 normalizeError
 * 的全部映射分支(与 httpClient contract 测试互补,后者覆盖完整 HTTP 路径)。
 */

import { AxiosError, type AxiosResponse, type InternalAxiosRequestConfig } from 'axios'
import { describe, expect, it } from 'vitest'

import { createApiError, DEFAULT_ERROR_MESSAGES, normalizeError } from '@/api/errors'

const CORR = 'corr-errors-001'

function responseOf(
  status: number,
  data: unknown,
  headers: Record<string, string> = {},
): AxiosResponse {
  return {
    status,
    statusText: String(status),
    headers,
    config: { headers: {} } as InternalAxiosRequestConfig,
    data,
  }
}

describe('createApiError', () => {
  it('only assigns optional fields that have a value', () => {
    const error = createApiError('business', 'm', CORR, { code: 'X_1' })
    expect(error.kind).toBe('business')
    expect(error.details.message).toBe('m')
    expect(error.details.correlationId).toBe(CORR)
    expect(error.details.code).toBe('X_1')
    expect(error.details.status).toBeUndefined()
    expect(error.details.traceId).toBeUndefined()
  })

  it('is an Error with ApiError name', () => {
    const error = createApiError('network', 'n', CORR)
    expect(error).toBeInstanceOf(Error)
    expect(error.name).toBe('ApiError')
  })
})

describe('normalizeError', () => {
  it('passes ApiError through unchanged', () => {
    const original = createApiError('forbidden', 'denied', CORR)
    expect(normalizeError(original, CORR)).toBe(original)
  })

  it('maps a plain unknown error to unknown', () => {
    const error = normalizeError(new Error('boom'), CORR)
    expect(error.kind).toBe('unknown')
    expect(error.details.message).toBe(DEFAULT_ERROR_MESSAGES.unknown)
    expect(error.details.correlationId).toBe(CORR)
  })

  it('maps a cancelled axios error to cancelled', () => {
    const axiosError = new AxiosError('canceled', AxiosError.ERR_CANCELED)
    expect(normalizeError(axiosError, CORR).kind).toBe('cancelled')
  })

  it('maps an ETIMEDOUT axios error to timeout', () => {
    const axiosError = new AxiosError('timeout of 50ms exceeded', AxiosError.ETIMEDOUT)
    expect(normalizeError(axiosError, CORR).kind).toBe('timeout')
  })

  it('maps an ECONNABORTED axios error to timeout', () => {
    const axiosError = new AxiosError('timeout', 'ECONNABORTED')
    expect(normalizeError(axiosError, CORR).kind).toBe('timeout')
  })

  it('maps an axios error without response to network', () => {
    const axiosError = new AxiosError('Network Error')
    expect(normalizeError(axiosError, CORR).kind).toBe('network')
  })

  it.each([
    [401, 'unauthorized'],
    [403, 'forbidden'],
    [404, 'notFound'],
    [500, 'server'],
    [503, 'server'],
  ])('maps HTTP %i to %s', (status, kind) => {
    const axiosError = new AxiosError(
      'Request failed',
      undefined,
      undefined,
      undefined,
      responseOf(status, { success: false, code: String(status), message: 'x', data: null }),
    )
    const error = normalizeError(axiosError, CORR)
    expect(error.kind).toBe(kind)
    expect(error.details.status).toBe(status)
  })

  it('extracts traceId from response headers', () => {
    const axiosError = new AxiosError(
      'Request failed',
      undefined,
      undefined,
      undefined,
      responseOf(
        500,
        { success: false, code: '500', message: 'x', data: null },
        { 'X-Trace-Id': 'trace-err' },
      ),
    )
    const error = normalizeError(axiosError, CORR)
    expect(error.kind).toBe('server')
    expect(error.details.traceId).toBe('trace-err')
    expect(error.details.correlationId).toBe(CORR)
  })

  it('keeps code/message of a non-2xx business envelope', () => {
    const axiosError = new AxiosError(
      'Request failed',
      undefined,
      undefined,
      undefined,
      responseOf(400, { success: false, code: 'VAL_01', message: '参数不合法', data: null }),
    )
    const error = normalizeError(axiosError, CORR)
    expect(error.kind).toBe('business')
    expect(error.details.code).toBe('VAL_01')
    expect(error.details.message).toBe('参数不合法')
    expect(error.details.status).toBe(400)
  })

  it('maps a non-2xx response with an invalid envelope to invalidResponse', () => {
    const axiosError = new AxiosError(
      'Request failed',
      undefined,
      undefined,
      undefined,
      responseOf(400, { foo: 1 }),
    )
    expect(normalizeError(axiosError, CORR).kind).toBe('invalidResponse')
  })
})

describe('ApiError instance', () => {
  it('exposes kind getter matching details.kind', () => {
    const error = createApiError('notFound', 'nope', CORR)
    expect(error.kind).toBe('notFound')
    expect(error.details.kind).toBe('notFound')
  })

  it('keeps the original correlationId across normalization', () => {
    const axiosError = new AxiosError(
      'Request failed',
      undefined,
      undefined,
      undefined,
      responseOf(403, null),
    )
    expect(normalizeError(axiosError, 'corr-keep').details.correlationId).toBe('corr-keep')
  })
})
