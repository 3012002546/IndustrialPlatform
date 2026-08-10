/**
 * 统一 HTTP 客户端:唯一网络入口。
 * 职责:correlationId、Authorization(可选注入)、ApiResult 信封解包、
 * 错误分类、TraceId 提取、敏感日志脱敏。页面/Store 不得直接使用 Axios。
 */

import axios, { type AxiosInstance } from 'axios'

import { createCorrelationId, extractTraceId, type ResponseHeadersLike } from './correlation'
import { createApiError, DEFAULT_ERROR_MESSAGES, normalizeError } from './errors'
import { parseEnvelope } from './envelope'
import { redactHeaders } from './redact'

export interface RequestOptions {
  headers?: Record<string, string>
  signal?: AbortSignal
}

export interface HttpClient {
  get<T>(path: string, options?: RequestOptions): Promise<T>
  post<T>(path: string, body?: unknown, options?: RequestOptions): Promise<T>
  put<T>(path: string, body?: unknown, options?: RequestOptions): Promise<T>
  delete<T>(path: string, options?: RequestOptions): Promise<T>
}

export interface HttpClientLogger {
  debug(message: string): void
  warn(message: string): void
}

export interface HttpClientDeps {
  baseUrl: string
  timeoutMs: number
  /** 可选:返回会话令牌用于 Authorization 头(FE-004 接入)。 */
  getToken?: () => string | null
  /** 可选:自定义 correlationId(测试注入固定值)。 */
  getCorrelationId?: () => string
  /** 可选:日志器,默认 console。 */
  logger?: HttpClientLogger
}

export function createHttpClient(deps: HttpClientDeps): HttpClient {
  const client: AxiosInstance = axios.create({
    baseURL: deps.baseUrl,
    timeout: deps.timeoutMs,
  })
  const logger = deps.logger ?? console

  async function request<T>(
    method: 'GET' | 'POST' | 'PUT' | 'DELETE',
    path: string,
    body: unknown,
    options: RequestOptions,
  ): Promise<T> {
    const headers: Record<string, string> = { ...(options.headers ?? {}) }
    const correlationId =
      headers['X-Correlation-Id'] ?? deps.getCorrelationId?.() ?? createCorrelationId()
    if (headers['X-Correlation-Id'] === undefined) {
      headers['X-Correlation-Id'] = correlationId
    }

    const token = deps.getToken?.()
    if (token && headers['Authorization'] === undefined) {
      headers['Authorization'] = `Bearer ${token}`
    }

    try {
      const response = await client.request<unknown>({
        method,
        url: path,
        data: body,
        headers,
        ...(options.signal === undefined ? {} : { signal: options.signal }),
      })
      const data = unwrap<T>(response.status, response.data, correlationId)
      logger.debug(`[api] ${method} ${path} → ${response.status} corr=${correlationId}`)
      return data
    } catch (error) {
      const apiError = normalizeError(error, correlationId)
      logger.warn(
        `[api] ${method} ${path} failed kind=${apiError.kind} status=${apiError.details.status ?? '-'} traceId=${apiError.details.traceId ?? '-'} corr=${correlationId}`,
      )
      throw apiError
    }
  }

  /** 2xx 信封解包;非法信封与 success=false 抛对应 ApiError。 */
  function unwrap<T>(status: number, data: unknown, correlationId: string): T {
    if (status >= 200 && status < 300) {
      const envelope = parseEnvelope(data)
      if (envelope.valid && envelope.success) {
        return envelope.data as T
      }
      if (envelope.valid) {
        throw createApiError('business', envelope.message, correlationId, {
          status,
          code: envelope.code,
          ...(envelope.traceId === undefined ? {} : { traceId: envelope.traceId }),
        })
      }
      throw createApiError(
        'invalidResponse',
        DEFAULT_ERROR_MESSAGES.invalidResponse,
        correlationId,
        {
          status,
        },
      )
    }
    // 非 2xx 由 Axios 以错误抛出,经 normalizeError 分类,此处为兜底。
    throw createApiError('unknown', DEFAULT_ERROR_MESSAGES.unknown, correlationId, { status })
  }

  return {
    get: <T>(path: string, options: RequestOptions = {}) =>
      request<T>('GET', path, undefined, options),
    post: <T>(path: string, body?: unknown, options: RequestOptions = {}) =>
      request<T>('POST', path, body, options),
    put: <T>(path: string, body?: unknown, options: RequestOptions = {}) =>
      request<T>('PUT', path, body, options),
    delete: <T>(path: string, options: RequestOptions = {}) =>
      request<T>('DELETE', path, undefined, options),
  }
}

export { extractTraceId, redactHeaders, type ResponseHeadersLike }
