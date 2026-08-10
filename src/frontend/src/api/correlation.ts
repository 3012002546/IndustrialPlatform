/**
 * 请求关联:correlationId(请求头 X-Correlation-Id)与 TraceId 提取。
 */

/** 生成请求级 UUID(每次请求一个)。 */
export function createCorrelationId(): string {
  if (typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function') {
    return crypto.randomUUID()
  }
  // 兜底:手写 UUIDv4(旧环境)。
  return 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, (c) => {
    const r = (Math.random() * 16) | 0
    const v = c === 'x' ? r : (r & 0x3) | 0x8
    return v.toString(16)
  })
}

export interface ResponseHeadersLike {
  [key: string]: string | string[] | number | boolean | undefined
}

/**
 * TraceId 提取优先级(§8.3):响应体 traceId 字段 → X-Trace-Id 头 → traceparent。
 * traceparent 取 `00-<traceId>-…` 中的 traceId 段。
 */
export function extractTraceId(
  body: unknown,
  headers: ResponseHeadersLike | undefined,
): string | undefined {
  const bodyRecord = (typeof body === 'object' && body !== null ? body : {}) as Record<
    string,
    unknown
  >
  const bodyTraceId = bodyRecord['traceId']
  if (typeof bodyTraceId === 'string' && bodyTraceId) return bodyTraceId

  const header = headers === undefined ? {} : headers
  const traceHeader = header['X-Trace-Id'] ?? header['x-trace-id']
  if (typeof traceHeader === 'string' && traceHeader) return traceHeader

  const traceparent = header['traceparent']
  if (typeof traceparent === 'string') {
    const traceId = traceparent.split('-')[1]
    if (traceId) return traceId
  }

  return undefined
}
