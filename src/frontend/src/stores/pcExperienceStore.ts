import { defineStore } from 'pinia'
import { computed, ref } from 'vue'

import { PERMISSIONS } from '@/permissions'
import type { PcExperienceMode, PcExperienceScope } from '@/operation/types'
import type { PersistedRouteLocation } from '@/workspace/types'

export const PC_EXPERIENCE_MODE_KEY_PREFIX = 'industrial-platform.pc.experience-mode.v1'
export const PC_EXPERIENCE_RETURN_ROUTE_KEY_PREFIX =
  'industrial-platform.pc.experience-return-route.v1'

export interface PcExperienceReturnRoute extends PersistedRouteLocation {
  path: string
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null
}

function isStringMap(value: unknown): value is Record<string, string | string[]> {
  if (!isRecord(value)) return false
  return Object.values(value).every(
    (item) =>
      typeof item === 'string' ||
      (Array.isArray(item) && item.every((entry) => typeof entry === 'string')),
  )
}

function isReturnRoute(value: unknown): value is PcExperienceReturnRoute {
  if (!isRecord(value)) return false
  const keys = new Set(['path', 'name', 'params', 'query'])
  return (
    Object.keys(value).every((key) => keys.has(key)) &&
    typeof value.path === 'string' &&
    value.path.startsWith('/pc/') &&
    value.path.length <= 2048 &&
    typeof value.name === 'string' &&
    value.name.length > 0 &&
    value.name.length <= 128 &&
    isStringMap(value.params) &&
    isStringMap(value.query)
  )
}

function isMode(value: string | null | undefined): value is PcExperienceMode {
  return value === 'management' || value === 'operation'
}

export function buildPcExperiencePreferenceKey(scope: PcExperienceScope): string {
  return `${PC_EXPERIENCE_MODE_KEY_PREFIX}:${encodeURIComponent(scope.tenantId)}:${encodeURIComponent(scope.userId)}:${scope.device}`
}

export function buildPcExperienceReturnRouteKey(scope: PcExperienceScope): string {
  return `${PC_EXPERIENCE_RETURN_ROUTE_KEY_PREFIX}:${encodeURIComponent(scope.tenantId)}:${encodeURIComponent(scope.userId)}:${scope.device}`
}

export function writePcExperienceReturnRoute(
  scope: PcExperienceScope,
  route: PcExperienceReturnRoute,
): boolean {
  if (!isReturnRoute(route)) return false
  try {
    const serialized = JSON.stringify(route)
    if (serialized.length > 4096) return false
    globalThis.sessionStorage.setItem(buildPcExperienceReturnRouteKey(scope), serialized)
    return true
  } catch {
    return false
  }
}

export function readPcExperienceReturnRoute(
  scope: PcExperienceScope,
): PcExperienceReturnRoute | null {
  try {
    const raw = globalThis.sessionStorage.getItem(buildPcExperienceReturnRouteKey(scope))
    if (raw === null) return null
    const parsed: unknown = JSON.parse(raw)
    return isReturnRoute(parsed) ? parsed : null
  } catch {
    return null
  }
}

export function clearPcExperienceReturnRoute(scope: PcExperienceScope): void {
  try {
    globalThis.sessionStorage.removeItem(buildPcExperienceReturnRouteKey(scope))
  } catch {
    // Session storage is best effort; mode navigation must remain usable.
  }
}

export function canEnterPcExperienceMode(
  mode: PcExperienceMode,
  permissions: readonly string[],
): boolean {
  const required = mode === 'management' ? PERMISSIONS.platformHomeView : PERMISSIONS.platformOperationView
  return permissions.includes(required)
}

export function resolvePcExperienceMode(
  permissions: readonly string[],
  savedMode?: string | null,
): PcExperienceMode | null {
  if (isMode(savedMode) && canEnterPcExperienceMode(savedMode, permissions)) return savedMode
  if (canEnterPcExperienceMode('management', permissions)) return 'management'
  if (canEnterPcExperienceMode('operation', permissions)) return 'operation'
  return null
}

export const usePcExperienceStore = defineStore('pcExperience', () => {
  const mode = ref<PcExperienceMode | null>(null)
  const permissions = ref<readonly string[]>([])
  const scope = ref<PcExperienceScope | null>(null)

  const canSwitch = computed(
    () =>
      canEnterPcExperienceMode('management', permissions.value) &&
      canEnterPcExperienceMode('operation', permissions.value),
  )

  function bind(nextScope: PcExperienceScope, nextPermissions: readonly string[]): void {
    permissions.value = [...nextPermissions]
    scope.value = nextScope
    const saved = globalThis.localStorage.getItem(buildPcExperiencePreferenceKey(nextScope))
    mode.value = resolvePcExperienceMode(nextPermissions, saved)
    if (mode.value !== null) {
      globalThis.localStorage.setItem(buildPcExperiencePreferenceKey(nextScope), mode.value)
    }
  }

  function setMode(nextMode: PcExperienceMode): boolean {
    if (!canEnterPcExperienceMode(nextMode, permissions.value)) return false
    mode.value = nextMode
    if (scope.value !== null) {
      globalThis.localStorage.setItem(buildPcExperiencePreferenceKey(scope.value), nextMode)
    }
    return true
  }

  return { mode, permissions, scope, canSwitch, bind, setMode }
})
