import { createPinia, setActivePinia } from 'pinia'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import {
  AUTH_SESSION_HTTP_STORAGE_KEY,
  setAuthGateway,
  setCurrentSession,
} from '@/auth'
import type { AuthGateway, AuthSession } from '@/auth/types'
import { useAuthStore } from '@/stores/authStore'

const session: AuthSession = {
  accessToken: 'native-access',
  refreshToken: 'native-refresh',
  expiresAt: new Date(Date.now() + 60_000).toISOString(),
  user: {
    userId: 'native-user',
    username: 'native-user',
    displayName: 'Native User',
    tenantId: 'tenant',
    roles: ['operator'],
    permissions: ['runtime.read'],
    mustChangePassword: false,
  },
}

function gateway(): AuthGateway {
  return {
    login: vi.fn(async () => session),
    refresh: vi.fn(async () => session),
    logout: vi.fn(async () => undefined),
    getCurrentUser: vi.fn(async () => session.user),
    changePassword: vi.fn(async () => undefined),
    getBootstrapStatus: vi.fn(async () => ({ state: 'Ready' as const, adminExists: true })),
  }
}

describe('native auth storage', () => {
  beforeEach(() => {
    vi.stubEnv('VITE_AUTH_MODE', 'http')
    setActivePinia(createPinia())
    setAuthGateway(gateway())
    setCurrentSession(null)
    sessionStorage.clear()
  })

  afterEach(() => {
    delete window.industrialPlatformRuntime
    vi.unstubAllEnvs()
  })

  it('stores HTTP bearer sessions through the host secure-storage bridge', async () => {
    const save = vi.fn(async () => undefined)
    const clear = vi.fn(async () => undefined)
    window.industrialPlatformRuntime = {
      authStorage: {
        load: vi.fn(async () => null),
        save,
        clear,
      },
    }

    const store = useAuthStore()
    await store.login({ username: 'native-user', password: 'not-persisted' })

    expect(save).toHaveBeenCalledWith(
      AUTH_SESSION_HTTP_STORAGE_KEY,
      expect.stringContaining('native-access'),
    )
    expect(sessionStorage.getItem(AUTH_SESSION_HTTP_STORAGE_KEY)).toBeNull()
  })
})
