/**
 * 页面测试共享会话夹具:构造合法 AuthSession 并可选写入 sessionStorage。
 * 复用 FE-004 的版本化存储校验,保证页面测试与会话契约一致。
 */

import { writeAuthSession } from '@/auth'
import type { AuthSession } from '@/auth/types'

export function makeAuthSession(permissions: string[] = []): AuthSession {
  return {
    accessToken: 'at',
    refreshToken: 'rt',
    expiresAt: new Date(Date.now() + 3_600_000).toISOString(),
    user: {
      userId: 'u1',
      username: 'mock.admin',
      displayName: 'Mock 演示账号',
      tenantId: 't1',
      roles: ['admin'],
      permissions,
      mustChangePassword: false,
    },
  }
}

/** 写入 sessionStorage 并返回会话,供 authStore.restore() 恢复。 */
export function persistAuthSession(permissions: string[] = []): AuthSession {
  const session = makeAuthSession(permissions)
  writeAuthSession(sessionStorage, session)
  return session
}
