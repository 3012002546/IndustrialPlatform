/**
 * API 统一契约:响应信封与统一错误模型。
 * 与后端 Gateway `ApiResult` 信封对齐(见根 README「前端 API 契约」)。
 */

/** Gateway 统一响应信封。2xx 且 success=true 才返回类型化 data。 */
export interface ApiResult<T> {
  success: boolean
  code: string
  message: string
  data: T | null
  parameters?: Record<string, unknown> | null
  traceId?: string | null
}

/** 统一错误分类;页面与 Store 只接触 ApiError,不接触 Axios 错误对象。 */
export type ApiErrorKind =
  | 'network'
  | 'timeout'
  | 'business'
  | 'unauthorized'
  | 'forbidden'
  | 'notFound'
  | 'server'
  | 'invalidResponse'
  | 'cancelled'
  | 'unknown'

export interface ApiErrorDetails {
  kind: ApiErrorKind
  message: string
  status?: number
  code?: string
  /** 前后端排查关联 ID,从响应体 / X-Trace-Id / traceparent 提取。 */
  traceId?: string
  /** 可供本地化消息模板使用的服务端参数。 */
  parameters?: Record<string, unknown>
  /** 每次请求生成的 UUID,请求头 X-Correlation-Id。 */
  correlationId: string
}
