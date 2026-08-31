<script setup lang="ts">
import { computed } from 'vue'

import { localeMessages } from '@/localization/i18n'
import { usePlatformLocale } from '@/localization/localeContext'

defineProps<{ degraded: boolean; unavailable: boolean }>()
const emit = defineEmits<{ retry: [] }>()
const locale = usePlatformLocale()
const copy = computed(() => localeMessages[locale.value].systemData.copy)
</script>

<template>
  <div
    v-if="degraded || unavailable"
    class="ip-platform-service-status"
    data-testid="platform-service-status"
    role="status"
  >
    <span>{{ copy.degraded }}</span>
    <span v-if="unavailable">{{ copy.snapshotUnavailable }}</span>
    <button type="button" @click="emit('retry')">{{ copy.retry }}</button>
  </div>
</template>

<style scoped>
.ip-platform-service-status {
  display: inline-flex;
  box-sizing: border-box;
  flex: 0 1 auto;
  align-items: center;
  gap: var(--ip-space-2);
  width: min(280px, 24vw);
  min-width: 0;
  max-width: min(280px, 24vw);
  overflow: hidden;
  color: var(--ip-color-warning);
  font-size: var(--ip-font-size-xs);
  white-space: nowrap;
}

.ip-platform-service-status span {
  flex: 1 1 auto;
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.ip-platform-service-status button {
  flex: 0 0 auto;
  padding: 0 var(--ip-space-2);
  color: inherit;
  background: transparent;
  border: 1px solid currentColor;
  border-radius: var(--ip-radius-sm);
  cursor: pointer;
}
</style>
