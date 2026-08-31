/**
 * 统一 API 错误:ApiError + 分类映射。
 * 页面与 Store 只接触 ApiError;Axios 错误对象在此封闭。
 */

import { AxiosError, isAxiosError } from 'axios'

import type { ApiErrorDetails, ApiErrorKind } from '@/types/api'
import { localeMessages, platformI18n, type SupportedLocale } from '@/localization/i18n'

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

function defaultErrorMessage(kind: ApiErrorKind): string {
  const locale = (platformI18n.global.locale as unknown as { value?: unknown }).value
  const key = kind as keyof typeof localeMessages['zh-CN']['common']['errors']
  return localeMessages[locale === 'en-US' ? 'en-US' : 'zh-CN'].common.errors[key]
}

const STABLE_ERROR_MESSAGES: Record<string, Record<SupportedLocale, string>> = {
  PLATFORM_QUERY_INVALID: {
    'zh-CN': '查询条件无效。',
    'en-US': 'The query is invalid.',
  },
  PLATFORM_QUERY_OPTION_NOT_ALLOWED: {
    'zh-CN': '查询选项未获允许。',
    'en-US': 'The query option is not allowed.',
  },
  PLATFORM_QUERY_FIELD_NOT_ALLOWED: {
    'zh-CN': '查询字段未获允许。',
    'en-US': 'The query field is not allowed.',
  },
  PLATFORM_QUERY_LIMIT_EXCEEDED: {
    'zh-CN': '查询结果超出限制。',
    'en-US': 'The query limit was exceeded.',
  },
  PLATFORM_QUERY_PAGING_ALIGNMENT_REQUIRED: {
    'zh-CN': '分页参数必须与页大小对齐。',
    'en-US': 'Paging parameters must align with the page size.',
  },
}

/** 按稳定错误码本地化；未登记的旧错误回退服务端 message。 */
export function localizeApiErrorMessage(code: string | undefined, fallback: string): string {
  if (code === undefined) return fallback
  const messages = STABLE_ERROR_MESSAGES[code]
  if (messages === undefined) return fallback
  const locale = (platformI18n.global.locale as unknown as { value?: unknown }).value
  return messages[locale === 'en-US' ? 'en-US' : 'zh-CN']
}

/** 构造 ApiError;仅当可选字段有值时赋值,满足 exactOptionalPropertyTypes。 */
export function createApiError(
  kind: ApiErrorKind,
  message: string,
  correlationId: string,
  options: {
    status?: number
    code?: string
    parameters?: Record<string, unknown>
    traceId?: string
  } = {},
): ApiError {
  const details: ApiErrorDetails = { kind, message, correlationId }
  if (options.status !== undefined) details.status = options.status
  if (options.code !== undefined) details.code = options.code
  if (options.parameters !== undefined) details.parameters = options.parameters
  if (options.traceId !== undefined) details.traceId = options.traceId
  return new ApiError(details)
}

/** 统一错误归一化:已是 ApiError 原样返回,否则按 Axios 错误分类映射。 */
export function normalizeError(error: unknown, correlationId: string): ApiError {
  if (error instanceof ApiError) return error
  if (isAxiosError(error)) return mapAxiosError(error, correlationId)
  return createApiError('unknown', defaultErrorMessage('unknown'), correlationId)
}

function mapAxiosError(error: AxiosError, correlationId: string): ApiError {
  // 主动取消
  if (error.code === AxiosError.ERR_CANCELED) {
    return createApiError('cancelled', defaultErrorMessage('cancelled'), correlationId)
  }
  // 超时
  if (error.code === AxiosError.ETIMEDOUT || error.code === 'ECONNABORTED') {
    return createApiError('timeout', defaultErrorMessage('timeout'), correlationId)
  }
  // 无响应 → 网络层
  if (!error.response) {
    return createApiError('network', defaultErrorMessage('network'), correlationId)
  }

  const { status, data } = error.response
  const headers = error.response.headers as ResponseHeadersLike | undefined
  const traceId = extractTraceId(data, headers)

  if (status === 401 || status === 403) {
    const kind = status === 401 ? 'unauthorized' : 'forbidden'
    // 优先保留后端信封的 code/message(如 ID_AUTH_INVALID_CREDENTIALS「用户名或密码错误」、
    // ID_PERMISSION_DENIED),便于页面展示准确原因;非法信封退回通用文案。
    const envelope = parseEnvelope(data)
    if (envelope.valid) {
      return createApiError(kind, localizeApiErrorMessage(envelope.code, envelope.message), correlationId, {
        status,
        code: envelope.code,
        ...(envelope.parameters === undefined ? {} : { parameters: envelope.parameters }),
        ...(traceId === undefined ? {} : { traceId }),
      })
    }
    return createApiError(kind, defaultErrorMessage(kind), correlationId, {
      status,
      ...(traceId === undefined ? {} : { traceId }),
    })
  }
  if (status === 404) {
    return createApiError('notFound', defaultErrorMessage('notFound'), correlationId, {
      status,
      ...(traceId === undefined ? {} : { traceId }),
    })
  }
  if (status >= 500) {
    return createApiError('server', defaultErrorMessage('server'), correlationId, {
      status,
      ...(traceId === undefined ? {} : { traceId }),
    })
  }

  // 非 2xx 业务信封:保留 code 与 message
  const envelope = parseEnvelope(data)
  if (envelope.valid) {
    return createApiError(
      'business',
      localizeApiErrorMessage(envelope.code, envelope.message),
      correlationId,
      {
      status,
      code: envelope.code,
      ...(envelope.parameters === undefined ? {} : { parameters: envelope.parameters }),
      ...(traceId === undefined ? {} : { traceId }),
      },
    )
  }

  return createApiError('invalidResponse', defaultErrorMessage('invalidResponse'), correlationId, {
    status,
    ...(traceId === undefined ? {} : { traceId }),
  })
}
