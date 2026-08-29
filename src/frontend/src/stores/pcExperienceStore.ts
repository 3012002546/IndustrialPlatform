import { defineStore } from 'pinia'
import { computed, ref } from 'vue'

import { PERMISSIONS } from '@/permissions'
import type { PcExperienceMode, PcExperienceScope } from '@/operation/types'

export const PC_EXPERIENCE_MODE_KEY_PREFIX = 'industrial-platform.pc.experience-mode.v1'

function isMode(value: string | null | undefined): value is PcExperienceMode {
  return value === 'management' || value === 'operation'
}

export function buildPcExperiencePreferenceKey(scope: PcExperienceScope): string {
  return `${PC_EXPERIENCE_MODE_KEY_PREFIX}:${encodeURIComponent(scope.tenantId)}:${encodeURIComponent(scope.userId)}:${scope.device}`
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
