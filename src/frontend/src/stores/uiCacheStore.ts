import { buildUserTabsKey } from '@/workspace/persistence'
import { buildPageStateKeyPrefix } from '@/workspace/pageState'
import type { UserUiScope } from '@/theme/types'
import { buildScopedAppDataTableUserKey } from '@/components/management/appDataTable/preferences'

export const UI_CACHE_CLEARED_EVENT = 'industrial-platform:ui-cache-cleared'

export interface UiCacheClearedDetail {
  scope: UserUiScope
}

/**
 * 删除当前用户的非安全 UI 缓存。前缀是显式白名单，认证、租户、locale、theme、
 * terminal override 和 experience mode 均不匹配，绝不使用 storage.clear()。
 */
export function clearCurrentUserUiCache(
  scope: UserUiScope,
): void {
  const localKeys = [
    buildUserTabsKey(scope),
    ...readKeys(globalThis.localStorage).filter((key) =>
      key.startsWith(
        `industrial-platform.table-preferences.v1:${buildScopedAppDataTableUserKey(scope)}:`,
      ),
    ),
  ]
  localKeys.forEach((key) => removeKey(globalThis.localStorage, key))
  readKeys(globalThis.sessionStorage)
    .filter((key) => key.startsWith(`${buildPageStateKeyPrefix(scope)}:`))
    .forEach((key) => removeKey(globalThis.sessionStorage, key))
  const event = new CustomEvent<UiCacheClearedDetail>(UI_CACHE_CLEARED_EVENT, {
    detail: { scope },
  })
  if (typeof document !== 'undefined') document.dispatchEvent(event)
  else globalThis.dispatchEvent(event)
}

function readKeys(storage: Storage): string[] {
  const keys: string[] = []
  for (let index = 0; index < storage.length; index += 1) {
    const key = storage.key(index)
    if (key !== null) keys.push(key)
  }
  return keys
}

function removeKey(storage: Storage, key: string): void {
  try {
    storage.removeItem(key)
  } catch {
    // Browser storage may be unavailable; never affect auth state.
  }
}
