/**
 * 敏感信息脱敏:日志不得输出 Authorization、password、accessToken、refreshToken
 * 及常见凭据类头(§8.3、§19)。
 */

const SENSITIVE_KEYS = new Set([
  'authorization',
  'proxy-authorization',
  'x-api-key',
  'cookie',
  'password',
  'accesstoken',
  'refreshtoken',
  'token',
])

export const REDACTED = '[REDACTED]'

export function isSensitiveKey(key: string): boolean {
  return SENSITIVE_KEYS.has(key.toLowerCase())
}

/** 请求头脱敏(用于日志输出)。 */
export function redactHeaders(
  headers: Record<string, string | undefined>,
): Record<string, string | undefined> {
  return Object.fromEntries(
    Object.entries(headers).map(([key, value]) => [key, isSensitiveKey(key) ? REDACTED : value]),
  )
}

/** 深拷贝式脱敏:对象/数组逐层处理,命中敏感键的值替换为 [REDACTED]。 */
export function redactSensitive(value: unknown): unknown {
  if (Array.isArray(value)) return value.map(redactSensitive)
  if (typeof value === 'object' && value !== null) {
    return Object.fromEntries(
      Object.entries(value).map(([key, child]) => [
        key,
        isSensitiveKey(key) ? REDACTED : redactSensitive(child),
      ]),
    )
  }
  return value
}
