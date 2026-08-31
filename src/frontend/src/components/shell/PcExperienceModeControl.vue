<script setup lang="ts">
import { computed, onMounted } from 'vue'
import { useRoute, useRouter } from 'vue-router'

import { PERMISSIONS } from '@/permissions'
import { ROUTE_NAMES } from '@/router/routes'
import { useAuthStore } from '@/stores/authStore'
import {
  clearPcExperienceReturnRoute,
  readPcExperienceReturnRoute,
  usePcExperienceStore,
  writePcExperienceReturnRoute,
} from '@/stores/pcExperienceStore'
import { localeMessages } from '@/localization/i18n'
import { usePlatformLocale } from '@/localization/localeContext'
import type { PcExperienceMode } from '@/operation/types'
import { toPersistedRoute } from '@/workspace'
import type { PcExperienceReturnRoute } from '@/stores/pcExperienceStore'

const props = withDefaults(
  defineProps<{ mode?: PcExperienceMode }>(),
  { mode: 'management' },
)
const emit = defineEmits<{ change: [mode: PcExperienceMode] }>()

const router = useRouter()
const route = useRoute()
const authStore = useAuthStore()
const experienceStore = usePcExperienceStore()
const locale = usePlatformLocale()
const copy = computed(() => localeMessages[locale.value])
const visible = computed(
  () =>
    authStore.hasPermission(PERMISSIONS.platformHomeView) &&
    authStore.hasPermission(PERMISSIONS.platformOperationView),
)
const currentMode = computed(() => experienceStore.mode ?? props.mode)

function currentScope() {
  const user = authStore.user
  return user === null
    ? null
    : { tenantId: user.tenantId, userId: user.userId, device: 'pc' as const }
}

function saveManagementReturnRoute(): void {
  const scope = currentScope()
  if (
    scope === null ||
    route.meta.requiresAuth !== true ||
    route.meta.terminal !== 'pc' ||
    route.meta.experience === 'operation' ||
    typeof route.name !== 'string'
  ) {
    return
  }
  const persisted = toPersistedRoute({
    name: route.name,
    params: route.params,
    query: route.query,
  })
  writePcExperienceReturnRoute(scope, { path: route.path, ...persisted })
}

function isAllowedManagementRoute(saved: PcExperienceReturnRoute): boolean {
  try {
    const resolved = router.resolve({
      name: saved.name,
      params: saved.params,
      query: saved.query,
    })
    if (
      resolved.path !== saved.path ||
      resolved.meta.requiresAuth !== true ||
      resolved.meta.terminal !== 'pc' ||
      resolved.meta.experience === 'operation'
    ) {
      return false
    }
    const permission = resolved.meta.permission
    if (typeof permission === 'string' && !authStore.hasPermission(permission)) return false
    const anyPermissions = resolved.meta.anyPermissions as readonly string[] | undefined
    return (
      anyPermissions === undefined || anyPermissions.some((item) => authStore.hasPermission(item))
    )
  } catch {
    return false
  }
}

function managementReturnLocation() {
  const scope = currentScope()
  if (scope !== null) {
    const saved = readPcExperienceReturnRoute(scope)
    clearPcExperienceReturnRoute(scope)
    if (saved !== null && isAllowedManagementRoute(saved)) {
      return { name: saved.name, params: saved.params, query: saved.query }
    }
  }
  return { name: ROUTE_NAMES.pcHome }
}

onMounted(() => {
  const user = authStore.user
  if (user !== null) {
    experienceStore.bind(
      { tenantId: user.tenantId, userId: user.userId, device: 'pc' },
      user.permissions,
    )
  }
})

async function switchMode(mode: PcExperienceMode): Promise<void> {
  if (!experienceStore.setMode(mode)) return
  if (mode === 'operation') saveManagementReturnRoute()
  emit('change', mode)
  await router.push(
    mode === 'operation' ? { name: ROUTE_NAMES.pcOperation } : managementReturnLocation(),
  )
}
</script>

<template>
  <div
    v-if="visible"
    class="pc-experience-mode-control"
    data-testid="pc-experience-mode-control"
    role="group"
    :aria-label="copy.shell.copy.experienceMode"
  >
    <button
      type="button"
      :class="{ 'is-active': currentMode === 'management' }"
      :aria-pressed="currentMode === 'management'"
      @click="switchMode('management')"
    >
      {{ copy.shell.mode.management }}
    </button>
    <button
      type="button"
      :class="{ 'is-active': currentMode === 'operation' }"
      :aria-pressed="currentMode === 'operation'"
      @click="switchMode('operation')"
    >
      {{ copy.shell.mode.operation }}
    </button>
  </div>
</template>

<style scoped>
.pc-experience-mode-control {
  display: inline-flex;
  align-items: center;
  padding: 2px;
  border: 1px solid rgb(255 255 255 / 0.35);
  border-radius: var(--ip-radius-md);
}

.pc-experience-mode-control button {
  min-height: 28px;
  padding: 0 var(--ip-space-2);
  color: inherit;
  background: transparent;
  border: 0;
  border-radius: var(--ip-radius-sm);
  cursor: pointer;
  font: inherit;
  font-size: var(--ip-font-size-xs);
  white-space: nowrap;
}

.pc-experience-mode-control button.is-active,
.pc-experience-mode-control button:hover,
.pc-experience-mode-control button:focus-visible {
  background: rgb(255 255 255 / 0.18);
}

.pc-experience-mode-control button:focus-visible {
  outline: 2px solid currentColor;
  outline-offset: 1px;
}
</style>
