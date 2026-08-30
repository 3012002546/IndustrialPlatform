import { beforeEach, describe, expect, it } from 'vitest'

import { AUTH_SESSION_HTTP_STORAGE_KEY } from '@/auth/sessionStore'
import { TERMINAL_OVERRIDE_STORAGE_KEY } from '@/device/override'
import { LOCALE_PREFERENCES_STORAGE_KEY } from '@/localization/preferences'
import { clearCurrentUserUiCache } from '@/stores/uiCacheStore'
import { buildPageStateKey } from '@/workspace/pageState'
import { buildUserTabsKey } from '@/workspace/persistence'

describe('uiCacheStore', () => {
  const scope = { tenantId: 'tenant-1', userId: 'user-1' }

  beforeEach(() => {
    localStorage.clear()
    sessionStorage.clear()
  })

  it('clears only the current user UI allowlist and preserves security and appearance state', () => {
    localStorage.setItem(buildUserTabsKey(scope), 'tabs')
    localStorage.setItem('industrial-platform.table-preferences.v1:user-1:users:main', 'table')
    localStorage.setItem('industrial-platform.table-preferences.v1:user-2:users:main', 'other-user-table')
    sessionStorage.setItem(buildPageStateKey('users'), 'page')
    sessionStorage.setItem(buildPageStateKey('stale-tab'), 'stale-page')
    sessionStorage.setItem(AUTH_SESSION_HTTP_STORAGE_KEY, 'session')
    localStorage.setItem(LOCALE_PREFERENCES_STORAGE_KEY, 'locale')
    localStorage.setItem(TERMINAL_OVERRIDE_STORAGE_KEY, 'pc')
    localStorage.setItem('industrial-platform.ui.preferences.v1:tenant-1:user-1', 'theme')
    localStorage.setItem('industrial-platform.pc.experience-mode.v1:user-1', 'management')

    clearCurrentUserUiCache(scope)

    expect(localStorage.getItem(buildUserTabsKey(scope))).toBeNull()
    expect(localStorage.getItem('industrial-platform.table-preferences.v1:user-1:users:main')).toBeNull()
    expect(sessionStorage.getItem(buildPageStateKey('users'))).toBeNull()
    expect(sessionStorage.getItem(buildPageStateKey('stale-tab'))).toBeNull()
    expect(localStorage.getItem('industrial-platform.table-preferences.v1:user-2:users:main')).toBe('other-user-table')
    expect(sessionStorage.getItem(AUTH_SESSION_HTTP_STORAGE_KEY)).toBe('session')
    expect(localStorage.getItem(LOCALE_PREFERENCES_STORAGE_KEY)).toBe('locale')
    expect(localStorage.getItem(TERMINAL_OVERRIDE_STORAGE_KEY)).toBe('pc')
    expect(localStorage.getItem('industrial-platform.ui.preferences.v1:tenant-1:user-1')).toBe('theme')
    expect(localStorage.getItem('industrial-platform.pc.experience-mode.v1:user-1')).toBe('management')
  })
})
