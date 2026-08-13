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

/** 401 刷新协调(Phase 3):认证失败时单飞刷新后重试原请求一次。 */
export interface HttpAuthRefresh {
  /** 认证专用路径(登录/刷新/登出):401 不触发刷新重试,避免无谓循环。 */
  isAuthPath(path: string): boolean
  /** 单飞刷新(内部可复用 AuthStore.refresh);失败抛出,调用方负责清理会话。 */
  refreshSession(): Promise<void>
  /** 刷新失败后的会话失效处理(清理本地会话并回到登录页,尽力而为)。 */
  onSessionExpired(): void
}

export interface HttpClientDeps {
  baseUrl: string
  timeoutMs: number
  /** 可选:返回会话令牌用于 Authorization 头(FE-004 接入)。 */
  getToken?: () => string | null
  /** 可选:跨源请求携带/接受凭据 Cookie(SSO 浏览器会话,§26.4)。默认 false。 */
  withCredentials?: boolean
  /** 可选:自定义 correlationId(测试注入固定值)。 */
  getCorrelationId?: () => string
  /** 可选:日志器,默认 console。 */
  logger?: HttpClientLogger
  /** 可选:401 单飞刷新 + 原请求重试一次(认证路径除外)。 */
  authRefresh?: HttpAuthRefresh
}

export function createHttpClient(deps: HttpClientDeps): HttpClient {
  const client: AxiosInstance = axios.create({
    baseURL: deps.baseUrl,
    timeout: deps.timeoutMs,
    withCredentials: deps.withCredentials ?? false,
  })
  const logger = deps.logger ?? console

  // 401 单飞刷新:同一时刻多个 401 请求共享一次 refreshSession;失败通知只发一次。
  let refreshPromise: Promise<void> | null = null
  let expiredNotified = false

  async function request<T>(
    method: 'GET' | 'POST' | 'PUT' | 'DELETE',
    path: string,
    body: unknown,
    options: RequestOptions,
  ): Promise<T> {
    const correlationId =
      options.headers?.['X-Correlation-Id'] ?? deps.getCorrelationId?.() ?? createCorrelationId()

    // 单飞刷新 + 原请求最多重试一次;认证路径或刷新失败不再重试(防循环)。
    let retried = false

    async function perform(): Promise<T> {
      const headers: Record<string, string> = { ...(options.headers ?? {}) }
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
        const auth = deps.authRefresh
        const canRetry =
          auth !== undefined &&
          apiError.kind === 'unauthorized' &&
          !auth.isAuthPath(path) &&
          !retried
        if (canRetry) {
          retried = true
          if (refreshPromise === null) {
            refreshPromise = auth
              .refreshSession()
              .then(
                () => {
                  // 刷新成功:复位失效标记,下一轮失败仍会通知。
                  expiredNotified = false
                },
                (refreshError: unknown) => {
                  if (!expiredNotified) {
                    expiredNotified = true
                    auth.onSessionExpired()
                  }
                  throw refreshError
                },
              )
              .finally(() => {
                refreshPromise = null
              })
          }
          try {
            await refreshPromise
          } catch {
            // 刷新失败:清理已由 onSessionExpired/Store 完成,抛出原始 401。
            throw apiError
          }
          // 刷新成功:携带新 token 重试原请求(重试失败原样抛出)。
          return await perform()
        }
        logger.warn(
          `[api] ${method} ${path} failed kind=${apiError.kind} status=${apiError.details.status ?? '-'} traceId=${apiError.details.traceId ?? '-'} corr=${correlationId}`,
        )
        throw apiError
      }
    }

    return perform()
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
