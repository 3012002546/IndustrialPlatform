/**
 * 版本化会话存储(§10.1):Phase 2 限定 sessionStorage,键带 .v1 版本。
 * 损坏 JSON、未知版本、缺少字段、非法过期时间和已过期会话一律视为无效(清理)。
 * password 不进入本存储。
 */

import type { AuthSession } from './types'

export const AUTH_SESSION_STORAGE_KEY = 'industrial-platform.auth.mock.v1'
export const AUTH_SESSION_VERSION = 1

interface StoredSession {
  version: number
  session: AuthSession
}

function isStringArray(value: unknown): value is string[] {
  return Array.isArray(value) && value.every((item) => typeof item === 'string')
}

/** 校验会话结构;缺少字段或字段类型不符判为无效(对应损坏数据清理)。 */
export function isValidAuthSession(value: unknown): value is AuthSession {
  if (typeof value !== 'object' || value === null) return false
  const record = value as Record<string, unknown>
  const user = record['user']
  if (typeof user !== 'object' || user === null) return false
  const userRecord = user as Record<string, unknown>
  return (
    typeof record['accessToken'] === 'string' &&
    typeof record['refreshToken'] === 'string' &&
    typeof record['expiresAt'] === 'string' &&
    typeof userRecord['userId'] === 'string' &&
    typeof userRecord['username'] === 'string' &&
    typeof userRecord['displayName'] === 'string' &&
    typeof userRecord['tenantId'] === 'string' &&
    isStringArray(userRecord['roles']) &&
    isStringArray(userRecord['permissions'])
  )
}

/** 过期判断:非法时间或已过期为真。 */
export function isSessionExpired(expiresAt: string, now: number): boolean {
  const time = Date.parse(expiresAt)
  return Number.isNaN(time) || time <= now
}

/** 解析并校验会话存储;任何异常输入返回 null,不抛出。 */
export function parseStoredSession(raw: string | null, now: number): AuthSession | null {
  if (raw === null) return null
  let parsed: unknown
  try {
    parsed = JSON.parse(raw)
  } catch {
    return null
  }
  if (typeof parsed !== 'object' || parsed === null) return null
  const record = parsed as Record<string, unknown>
  if (record['version'] !== AUTH_SESSION_VERSION) return null
  const session = record['session']
  if (!isValidAuthSession(session)) return null
  if (isSessionExpired(session.expiresAt, now)) return null
  return session
}

export type SessionStorage = Pick<Storage, 'getItem' | 'setItem' | 'removeItem'>

/** 读取会话;无有效会话返回 null。 */
export function readAuthSession(storage: SessionStorage, now: number): AuthSession | null {
  return parseStoredSession(storage.getItem(AUTH_SESSION_STORAGE_KEY), now)
}

/** 写入版本化会话。 */
export function writeAuthSession(storage: SessionStorage, session: AuthSession): void {
  const payload: StoredSession = { version: AUTH_SESSION_VERSION, session }
  storage.setItem(AUTH_SESSION_STORAGE_KEY, JSON.stringify(payload))
}

/** 清除会话。 */
export function clearAuthSession(storage: Pick<SessionStorage, 'removeItem'>): void {
  storage.removeItem(AUTH_SESSION_STORAGE_KEY)
}
