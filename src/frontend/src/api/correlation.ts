/**
 * 请求关联:correlationId(请求头 X-Correlation-Id)与 TraceId 提取。
 */

/** 生成请求级 UUID(每次请求一个)。 */
export function createCorrelationId(): string {
  const cryptoApi = globalThis.crypto
  if (typeof cryptoApi?.randomUUID === 'function') return cryptoApi.randomUUID()
  if (typeof cryptoApi?.getRandomValues !== 'function')
    throw new Error('Secure random UUID generation is unavailable.')
  const bytes = new Uint8Array(16)
  cryptoApi.getRandomValues(bytes)
  bytes[6] = (bytes[6]! & 0x0f) | 0x40
  bytes[8] = (bytes[8]! & 0x3f) | 0x80
  const hex = Array.from(bytes, (value) => value.toString(16).padStart(2, '0'))
  return `${hex.slice(0, 4).join('')}-${hex.slice(4, 6).join('')}-${hex.slice(6, 8).join('')}-${hex.slice(8, 10).join('')}-${hex.slice(10).join('')}`
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
