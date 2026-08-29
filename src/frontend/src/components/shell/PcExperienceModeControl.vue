<script setup lang="ts">
import { computed, onMounted } from 'vue'
import { useRouter } from 'vue-router'

import { PERMISSIONS } from '@/permissions'
import { ROUTE_NAMES } from '@/router/routes'
import { useAuthStore } from '@/stores/authStore'
import { usePcExperienceStore } from '@/stores/pcExperienceStore'
import type { PcExperienceMode } from '@/operation/types'

const props = withDefaults(
  defineProps<{ mode?: PcExperienceMode }>(),
  { mode: 'management' },
)
const emit = defineEmits<{ change: [mode: PcExperienceMode] }>()

const router = useRouter()
const authStore = useAuthStore()
const experienceStore = usePcExperienceStore()
const visible = computed(
  () =>
    authStore.hasPermission(PERMISSIONS.platformHomeView) &&
    authStore.hasPermission(PERMISSIONS.platformOperationView),
)
const currentMode = computed(() => experienceStore.mode ?? props.mode)

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
  emit('change', mode)
  await router.push({ name: mode === 'operation' ? ROUTE_NAMES.pcOperation : ROUTE_NAMES.pcHome })
}
</script>

<template>
  <div
    v-if="visible"
    class="pc-experience-mode-control"
    data-testid="pc-experience-mode-control"
    role="group"
    aria-label="体验模式"
  >
    <button
      type="button"
      :class="{ 'is-active': currentMode === 'management' }"
      :aria-pressed="currentMode === 'management'"
      @click="switchMode('management')"
    >
      管理
    </button>
    <button
      type="button"
      :class="{ 'is-active': currentMode === 'operation' }"
      :aria-pressed="currentMode === 'operation'"
      @click="switchMode('operation')"
    >
      生产操作
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
