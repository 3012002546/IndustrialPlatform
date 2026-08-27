import { defineStore } from 'pinia'
import { ref, type Ref } from 'vue'

import { getSystemDataRuntimeApi } from '@/api/systemData/runtimeRegistry'
import type {
  FeatureRuntimeDto,
  NavigationRuntimeDto,
  ThemePolicyDto,
} from '@/api/systemData/types'
import type { NavigationGroup } from '@/components/navigation/types'
import {
  pcNavigationGroups,
  resetPcNavigationGroups,
  replacePcNavigationGroups,
} from '@/components/navigation/navigation'
import {
  applyNavigationPolicy,
  featureNIds,
  mapRuntimeNavigation,
} from '@/systemData/runtime/navigation'

const MAX_STALE_MS = 5 * 60 * 1000

interface CacheEntry<T> {
  data: T
  etag: string | undefined
  verifiedAt: number
}

export interface SystemDataRuntimeStoreState {
  navigationGroups: Ref<NavigationGroup[]>
  themePolicy: Ref<ThemePolicyDto | null>
  degraded: Ref<boolean>
  unavailable: Ref<boolean>
  lastVerifiedAt: Ref<number | null>
  clear(): void
  setPermissions(next: readonly string[]): void
  refresh(terminal?: 'Pc' | 'Pda' | 'Mobile'): Promise<void>
}

function isFresh(entry: CacheEntry<unknown>, now: number): boolean {
  return now - entry.verifiedAt <= MAX_STALE_MS
}

export const useSystemDataRuntimeStore = defineStore(
  'systemDataRuntime',
  (): SystemDataRuntimeStoreState => {
    const navigationCache = ref<CacheEntry<NavigationRuntimeDto> | null>(null)
    const featureCache = ref<CacheEntry<FeatureRuntimeDto> | null>(null)
    const themeCache = ref<CacheEntry<ThemePolicyDto> | null>(null)
    const themePolicy = ref<ThemePolicyDto | null>(null)
    const permissionNIds = ref<string[]>([])
    const navigationGroups = ref<NavigationGroup[]>([])
    const degraded = ref(false)
    const unavailable = ref(false)
    const lastVerifiedAt = ref<number | null>(null)

    let refreshPromise: Promise<void> | null = null

    function recomputeNavigation(): void {
      const snapshot = navigationCache.value
      if (snapshot === null) {
        navigationGroups.value = [...pcNavigationGroups]
        resetPcNavigationGroups()
        return
      }
      const raw = mapRuntimeNavigation(snapshot.data.nodes)
      navigationGroups.value = applyNavigationPolicy(
        raw,
        permissionNIds.value,
        featureNIds(featureCache.value?.data.items ?? []),
      )
      if (navigationGroups.value.length === 0) resetPcNavigationGroups()
      else replacePcNavigationGroups(navigationGroups.value)
    }

    function setPermissions(next: readonly string[]): void {
      permissionNIds.value = [...next]
      recomputeNavigation()
    }

    function clear(): void {
      navigationCache.value = null
      featureCache.value = null
      themeCache.value = null
      themePolicy.value = null
      permissionNIds.value = []
      navigationGroups.value = []
      degraded.value = false
      unavailable.value = false
      lastVerifiedAt.value = null
      resetPcNavigationGroups()
    }

    async function refresh(terminal: 'Pc' | 'Pda' | 'Mobile' = 'Pc'): Promise<void> {
      if (refreshPromise !== null) return refreshPromise
      const api = getSystemDataRuntimeApi()
      if (api === null) return
      refreshPromise = (async () => {
        const now = Date.now()
        const results = await Promise.allSettled([
          api.getNavigation(terminal, navigationCache.value?.etag),
          api.getFeatures(featureCache.value?.etag),
          api.getThemePolicy(themeCache.value?.etag),
        ])
        let hasExpiredFailure = false
        let hasDegradedResponse = false
        const [navigationResult, featureResult, themeResult] = results

        if (navigationResult.status === 'fulfilled') {
          if (navigationResult.value.kind === 'updated') {
            navigationCache.value = {
              data: navigationResult.value.data,
              etag: navigationResult.value.etag,
              verifiedAt: now,
            }
            hasDegradedResponse ||= navigationResult.value.data.degraded
          } else if (navigationCache.value !== null) {
            navigationCache.value = {
              ...navigationCache.value,
              verifiedAt: now,
              etag: navigationResult.value.etag,
            }
          }
        } else if (navigationCache.value === null || !isFresh(navigationCache.value, now)) {
          hasExpiredFailure = true
        }

        if (featureResult.status === 'fulfilled') {
          if (featureResult.value.kind === 'updated') {
            featureCache.value = {
              data: featureResult.value.data,
              etag: featureResult.value.etag,
              verifiedAt: now,
            }
            hasDegradedResponse ||= featureResult.value.data.degraded
          } else if (featureCache.value !== null) {
            featureCache.value = {
              ...featureCache.value,
              verifiedAt: now,
              etag: featureResult.value.etag,
            }
          }
        } else if (featureCache.value === null || !isFresh(featureCache.value, now)) {
          hasExpiredFailure = true
        }

        if (themeResult.status === 'fulfilled') {
          if (themeResult.value.kind === 'updated') {
            themeCache.value = {
              data: themeResult.value.data,
              etag: themeResult.value.etag,
              verifiedAt: now,
            }
            themePolicy.value = themeResult.value.data
            hasDegradedResponse ||= themeResult.value.data.degraded
          } else if (themeCache.value !== null) {
            themeCache.value = {
              ...themeCache.value,
              verifiedAt: now,
              etag: themeResult.value.etag,
            }
          }
        } else if (themeCache.value === null || !isFresh(themeCache.value, now)) {
          hasExpiredFailure = true
        }

        recomputeNavigation()
        lastVerifiedAt.value = now
        degraded.value =
          hasDegradedResponse ||
          hasExpiredFailure ||
          results.some((result) => result.status === 'rejected')
        unavailable.value = hasExpiredFailure
      })()
      try {
        await refreshPromise
      } finally {
        refreshPromise = null
      }
    }

    return {
      navigationGroups,
      themePolicy,
      degraded,
      unavailable,
      lastVerifiedAt,
      clear,
      setPermissions,
      refresh,
    }
  },
)
