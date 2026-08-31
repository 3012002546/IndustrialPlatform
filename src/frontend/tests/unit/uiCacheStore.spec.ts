import { beforeEach, describe, expect, it, vi } from 'vitest'

import { AUTH_SESSION_HTTP_STORAGE_KEY } from '@/auth/sessionStore'
import { TERMINAL_OVERRIDE_STORAGE_KEY } from '@/device/override'
import { LOCALE_PREFERENCES_STORAGE_KEY } from '@/localization/preferences'
import {
  clearCurrentUserUiCache,
  UI_CACHE_CLEARED_EVENT,
} from '@/stores/uiCacheStore'
import { buildPageStateKey } from '@/workspace/pageState'
import { buildUserTabsKey } from '@/workspace/persistence'
import {
  buildAppDataTablePreferenceKey,
  buildScopedAppDataTableUserKey,
} from '@/components/management/appDataTable/preferences'

describe('uiCacheStore', () => {
  const scope = { tenantId: 'tenant-1', userId: 'user-1' }
  const otherScope = { tenantId: 'tenant-2', userId: 'user-1' }

  beforeEach(() => {
    localStorage.clear()
    sessionStorage.clear()
  })

  it('clears only the current user UI allowlist and preserves security and appearance state', () => {
    localStorage.setItem(buildUserTabsKey(scope), 'tabs')
    localStorage.setItem(
      buildAppDataTablePreferenceKey(buildScopedAppDataTableUserKey(scope), 'users', 'main'),
      'table',
    )
    localStorage.setItem(
      buildAppDataTablePreferenceKey(
        buildScopedAppDataTableUserKey(otherScope),
        'users',
        'main',
      ),
      'other-user-table',
    )
    sessionStorage.setItem(buildPageStateKey(scope, 'users'), 'page')
    sessionStorage.setItem(buildPageStateKey(scope, 'stale-tab'), 'stale-page')
    sessionStorage.setItem(buildPageStateKey(otherScope, 'users'), 'other-user-page')
    sessionStorage.setItem(AUTH_SESSION_HTTP_STORAGE_KEY, 'session')
    localStorage.setItem(LOCALE_PREFERENCES_STORAGE_KEY, 'locale')
    localStorage.setItem(TERMINAL_OVERRIDE_STORAGE_KEY, 'pc')
    localStorage.setItem('industrial-platform.ui.preferences.v1:tenant-1:user-1', 'theme')
    localStorage.setItem('industrial-platform.pc.experience-mode.v1:user-1', 'management')

    clearCurrentUserUiCache(scope)

    expect(localStorage.getItem(buildUserTabsKey(scope))).toBeNull()
    expect(
      localStorage.getItem(
        buildAppDataTablePreferenceKey(buildScopedAppDataTableUserKey(scope), 'users', 'main'),
      ),
    ).toBeNull()
    expect(sessionStorage.getItem(buildPageStateKey(scope, 'users'))).toBeNull()
    expect(sessionStorage.getItem(buildPageStateKey(scope, 'stale-tab'))).toBeNull()
    expect(sessionStorage.getItem(buildPageStateKey(otherScope, 'users'))).toBe('other-user-page')
    expect(
      localStorage.getItem(
        buildAppDataTablePreferenceKey(
          buildScopedAppDataTableUserKey(otherScope),
          'users',
          'main',
        ),
      ),
    ).toBe('other-user-table')
    expect(sessionStorage.getItem(AUTH_SESSION_HTTP_STORAGE_KEY)).toBe('session')
    expect(localStorage.getItem(LOCALE_PREFERENCES_STORAGE_KEY)).toBe('locale')
    expect(localStorage.getItem(TERMINAL_OVERRIDE_STORAGE_KEY)).toBe('pc')
    expect(localStorage.getItem('industrial-platform.ui.preferences.v1:tenant-1:user-1')).toBe('theme')
    expect(localStorage.getItem('industrial-platform.pc.experience-mode.v1:user-1')).toBe('management')
  })

  it('publishes the current tenant/user scope for mounted UI consumers', () => {
    const listener = vi.fn()
    document.addEventListener(UI_CACHE_CLEARED_EVENT, listener)

    clearCurrentUserUiCache(scope)

    expect(listener).toHaveBeenCalledTimes(1)
    expect((listener.mock.calls[0]?.[0] as CustomEvent).detail).toEqual({ scope })
    document.removeEventListener(UI_CACHE_CLEARED_EVENT, listener)
  })
})
