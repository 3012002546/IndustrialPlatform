/**
 * 统一 API 错误:ApiError + 分类映射。
 * 页面与 Store 只接触 ApiError;Axios 错误对象在此封闭。
 */

import { AxiosError, isAxiosError } from 'axios'

import type { ApiErrorDetails, ApiErrorKind } from '@/types/api'

import { extractTraceId, type ResponseHeadersLike } from './correlation'
import { parseEnvelope } from './envelope'

export class ApiError extends Error {
  readonly details: ApiErrorDetails

  constructor(details: ApiErrorDetails) {
    super(details.message)
    this.name = 'ApiError'
    this.details = details
  }

  get kind(): ApiErrorKind {
    return this.details.kind
  }
}

export const DEFAULT_ERROR_MESSAGES: Record<ApiErrorKind, string> = {
  network: '网络不可用,请检查网络连接后重试',
  timeout: '请求超时,请稍后重试',
  business: '操作未成功',
  unauthorized: '登录已失效,请重新登录',
  forbidden: '没有访问该资源的权限',
  notFound: '请求的资源或接口不存在',
  server: '服务暂时不可用,请稍后重试',
  invalidResponse: '服务返回格式异常',
  cancelled: '请求已取消',
  unknown: '发生未知错误,请稍后重试',
}

/** 构造 ApiError;仅当可选字段有值时赋值,满足 exactOptionalPropertyTypes。 */
export function createApiError(
  kind: ApiErrorKind,
  message: string,
  correlationId: string,
  options: { status?: number; code?: string; traceId?: string } = {},
): ApiError {
  const details: ApiErrorDetails = { kind, message, correlationId }
  if (options.status !== undefined) details.status = options.status
  if (options.code !== undefined) details.code = options.code
  if (options.traceId !== undefined) details.traceId = options.traceId
  return new ApiError(details)
}

/** 统一错误归一化:已是 ApiError 原样返回,否则按 Axios 错误分类映射。 */
export function normalizeError(error: unknown, correlationId: string): ApiError {
  if (error instanceof ApiError) return error
  if (isAxiosError(error)) return mapAxiosError(error, correlationId)
  return createApiError('unknown', DEFAULT_ERROR_MESSAGES.unknown, correlationId)
}

function mapAxiosError(error: AxiosError, correlationId: string): ApiError {
  // 主动取消
  if (error.code === AxiosError.ERR_CANCELED) {
    return createApiError('cancelled', DEFAULT_ERROR_MESSAGES.cancelled, correlationId)
  }
  // 超时
  if (error.code === AxiosError.ETIMEDOUT || error.code === 'ECONNABORTED') {
    return createApiError('timeout', DEFAULT_ERROR_MESSAGES.timeout, correlationId)
  }
  // 无响应 → 网络层
  if (!error.response) {
    return createApiError('network', DEFAULT_ERROR_MESSAGES.network, correlationId)
  }

  const { status, data } = error.response
  const headers = error.response.headers as ResponseHeadersLike | undefined
  const traceId = extractTraceId(data, headers)

  if (status === 401) {
    return createApiError('unauthorized', DEFAULT_ERROR_MESSAGES.unauthorized, correlationId, {
      status,
      ...(traceId === undefined ? {} : { traceId }),
    })
  }
  if (status === 403) {
    return createApiError('forbidden', DEFAULT_ERROR_MESSAGES.forbidden, correlationId, {
      status,
      ...(traceId === undefined ? {} : { traceId }),
    })
  }
  if (status === 404) {
    return createApiError('notFound', DEFAULT_ERROR_MESSAGES.notFound, correlationId, {
      status,
      ...(traceId === undefined ? {} : { traceId }),
    })
  }
  if (status >= 500) {
    return createApiError('server', DEFAULT_ERROR_MESSAGES.server, correlationId, {
      status,
      ...(traceId === undefined ? {} : { traceId }),
    })
  }

  // 非 2xx 业务信封:保留 code 与 message
  const envelope = parseEnvelope(data)
  if (envelope.valid) {
    return createApiError('business', envelope.message, correlationId, {
      status,
      code: envelope.code,
      ...(traceId === undefined ? {} : { traceId }),
    })
  }

  return createApiError('invalidResponse', DEFAULT_ERROR_MESSAGES.invalidResponse, correlationId, {
    status,
    ...(traceId === undefined ? {} : { traceId }),
  })
}
