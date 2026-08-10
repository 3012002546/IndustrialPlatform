/**
 * 会话存储校验(§10.1):损坏 JSON、未知版本、缺少字段、非法/过期时间一律视为无效。
 * password 不得出现在存储内容中。
 */

import { describe, expect, it } from 'vitest'

import {
  AUTH_SESSION_STORAGE_KEY,
  AUTH_SESSION_VERSION,
  clearAuthSession,
  isSessionExpired,
  parseStoredSession,
  readAuthSession,
  writeAuthSession,
  type SessionStorage,
} from '@/auth'
import type { AuthSession } from '@/auth/types'

const NOW = 1_800_000_000_000
const LATER = NOW + 3_600_000

function validSession(overrides: Record<string, unknown> = {}): AuthSession {
  const base: Record<string, unknown> = {
    accessToken: 'mock.at.1',
    refreshToken: 'mock.rt.1',
    expiresAt: new Date(LATER).toISOString(),
    user: {
      userId: 'u1',
      username: 'mock.admin',
      displayName: 'Mock 演示账号',
      tenantId: 't1',
      roles: ['admin'],
      permissions: ['platform.home.view'],
    },
  }
  return { ...base, ...overrides } as unknown as AuthSession
}

function stored(raw: unknown): string | null {
  return typeof raw === 'string' ? raw : JSON.stringify(raw)
}

function memoryStorage(): SessionStorage & { get: () => string | null } {
  let value: string | null = null
  return {
    getItem: () => value,
    setItem: (_key, raw) => {
      value = raw
    },
    removeItem: () => {
      value = null
    },
    get: () => value,
  }
}

describe('parseStoredSession', () => {
  it('null 输入返回 null', () => {
    expect(parseStoredSession(null, NOW)).toBeNull()
  })

  it('损坏 JSON 返回 null', () => {
    expect(parseStoredSession('{not-json', NOW)).toBeNull()
  })

  it('非对象 JSON 返回 null', () => {
    expect(parseStoredSession(stored('hello'), NOW)).toBeNull()
    expect(parseStoredSession(stored(42), NOW)).toBeNull()
  })

  it('未知版本返回 null', () => {
    const raw = stored({ version: AUTH_SESSION_VERSION + 1, session: validSession() })
    expect(parseStoredSession(raw, NOW)).toBeNull()
  })

  it.each([
    ['缺少 accessToken', { accessToken: undefined }],
    ['缺少 refreshToken', { refreshToken: undefined }],
    ['缺少 expiresAt', { expiresAt: undefined }],
    ['缺少 user', { user: undefined }],
    ['permissions 非字符串数组', { user: { ...validSession().user, permissions: ['a', 1] } }],
    ['userId 缺失', { user: { ...validSession().user, userId: undefined } }],
  ])('%s 返回 null', (_label, patch) => {
    const raw = stored({ version: AUTH_SESSION_VERSION, session: validSession(patch) })
    expect(parseStoredSession(raw, NOW)).toBeNull()
  })

  it('非法过期时间返回 null', () => {
    const raw = stored({
      version: AUTH_SESSION_VERSION,
      session: validSession({ expiresAt: 'not-a-date' }),
    })
    expect(parseStoredSession(raw, NOW)).toBeNull()
  })

  it('已过期会话返回 null', () => {
    const raw = stored({
      version: AUTH_SESSION_VERSION,
      session: validSession({ expiresAt: new Date(NOW - 1).toISOString() }),
    })
    expect(parseStoredSession(raw, NOW)).toBeNull()
  })

  it('恰好到期的会话视为过期', () => {
    const raw = stored({
      version: AUTH_SESSION_VERSION,
      session: validSession({ expiresAt: new Date(NOW).toISOString() }),
    })
    expect(parseStoredSession(raw, NOW)).toBeNull()
  })

  it('有效会话原样返回', () => {
    const session = validSession()
    const parsed = parseStoredSession(stored({ version: AUTH_SESSION_VERSION, session }), NOW)
    expect(parsed).toEqual(session)
  })
})

describe('isSessionExpired', () => {
  it('非法时间视为过期', () => {
    expect(isSessionExpired('garbage', NOW)).toBe(true)
  })

  it('未来时间未过期,过去时间过期', () => {
    expect(isSessionExpired(new Date(LATER).toISOString(), NOW)).toBe(false)
    expect(isSessionExpired(new Date(NOW - 1000).toISOString(), NOW)).toBe(true)
  })
})

describe('write/read/clearAuthSession', () => {
  it('write 后 read 往返一致', () => {
    const storage = memoryStorage()
    const session = validSession()
    writeAuthSession(storage, session)
    expect(readAuthSession(storage, NOW)).toEqual(session)
  })

  it('read 对坏数据返回 null 且不抛错', () => {
    const storage = memoryStorage()
    storage.setItem(AUTH_SESSION_STORAGE_KEY, '{broken')
    expect(readAuthSession(storage, NOW)).toBeNull()
  })

  it('clear 移除存储项', () => {
    const storage = memoryStorage()
    writeAuthSession(storage, validSession())
    clearAuthSession(storage)
    expect(storage.getItem(AUTH_SESSION_STORAGE_KEY)).toBeNull()
  })

  it('存储内容不包含 password', () => {
    const storage = memoryStorage()
    writeAuthSession(storage, validSession())
    const content = storage.getItem(AUTH_SESSION_STORAGE_KEY) ?? ''
    expect(content).not.toContain('password')
    expect(content).not.toContain('Mock@123456')
  })
})
