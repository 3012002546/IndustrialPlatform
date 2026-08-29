/**
 * Gateway 统一信封(ApiResult)解析。
 * 规则:2xx 且 success=true 才返回类型化 data;2xx 但信封非法按 invalidResponse;
 * 非 2xx 业务信封保留 code/message。
 */

import type { ApiResult } from '@/types/api'

export function isApiResult(value: unknown): value is ApiResult<unknown> {
  if (typeof value !== 'object' || value === null) return false
  const record = value as Record<string, unknown>
  return (
    typeof record['success'] === 'boolean' &&
    typeof record['code'] === 'string' &&
    typeof record['message'] === 'string' &&
    'data' in record
  )
}

export interface ParsedEnvelope {
  valid: boolean
  success: boolean
  code: string
  message: string
  data: unknown
  parameters?: Record<string, unknown>
  traceId?: string
}

/** 尽力解析信封;非法信封返回 valid=false。 */
export function parseEnvelope(value: unknown): ParsedEnvelope {
  if (!isApiResult(value)) {
    return { valid: false, success: false, code: '', message: '', data: null }
  }
  const bodyRecord = value as unknown as Record<string, unknown>
  const parameters =
    typeof bodyRecord['parameters'] === 'object' && bodyRecord['parameters'] !== null
      ? (bodyRecord['parameters'] as Record<string, unknown>)
      : undefined
  const traceId = typeof bodyRecord['traceId'] === 'string' ? bodyRecord['traceId'] : undefined
  return {
    valid: true,
    success: value.success,
    code: value.code,
    message: value.message,
    data: value.data,
    ...(parameters === undefined ? {} : { parameters }),
    ...(traceId === undefined ? {} : { traceId }),
  }
}
