import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import { setAuthGateway } from '@/auth/gateway'
import type { AuthGateway, AuthSession } from '@/auth/types'
import { useAuthStore } from '@/stores/authStore'
import { useLockStore } from '@/stores/lockStore'

const session: AuthSession = {
  accessToken: 'access-token',
  refreshToken: 'refresh-token',
  expiresAt: new Date(Date.now() + 3_600_000).toISOString(),
  user: {
    userId: 'user-1',
    username: 'operator',
    displayName: '操作员',
    tenantId: 'tenant-1',
    roles: ['admin'],
    permissions: [],
    mustChangePassword: false,
  },
}

describe('lockStore', () => {
  beforeEach(async () => {
    sessionStorage.clear()
    setActivePinia(createPinia())
    setAuthGateway({
      login: vi.fn(async () => session),
      refresh: vi.fn(async () => session),
      logout: vi.fn(async () => undefined),
      getCurrentUser: vi.fn(async () => session.user),
      changePassword: vi.fn(async () => undefined),
      getBootstrapStatus: vi.fn(async () => ({ state: 'Ready' as const, adminExists: true })),
    } satisfies AuthGateway)
    useAuthStore().adoptSession(session)
  })

  it('clears only the local session and unlocks through the current username password login', async () => {
    const auth = useAuthStore()
    const lock = useLockStore()

    lock.lock()

    expect(lock.isLocked).toBe(true)
    expect(lock.lockedUser?.username).toBe('operator')
    expect(auth.isAuthenticated).toBe(false)
    expect(sessionStorage.length).toBe(0)

    await lock.unlock('current-password')

    expect(lock.isLocked).toBe(false)
    expect(auth.isAuthenticated).toBe(true)
  })
})
