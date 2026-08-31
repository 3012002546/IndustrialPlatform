<script setup lang="ts">
import { computed } from 'vue'
import { Refresh } from '@element-plus/icons-vue'

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
    :aria-label="`${copy.degraded}${unavailable ? `, ${copy.snapshotUnavailable}` : ''}`"
  >
    <span class="ip-platform-service-status__copy">{{ copy.degraded }}</span>
    <span v-if="unavailable" class="ip-platform-service-status__copy">{{ copy.snapshotUnavailable }}</span>
    <button type="button" :aria-label="copy.retry" :title="copy.retry" @click="emit('retry')">
      <Refresh class="ip-platform-service-status__retry-icon" aria-hidden="true" />
      <span class="ip-platform-service-status__retry-text">{{ copy.retry }}</span>
    </button>
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

.ip-platform-service-status__retry-icon {
  display: none;
}

@media (max-width: 1440px) {
  .ip-platform-service-status {
    position: relative;
    flex: 0 0 32px;
    justify-content: center;
    width: 32px;
    max-width: 32px;
    height: 32px;
    gap: 0;
    overflow: visible;
  }

  .ip-platform-service-status__copy,
  .ip-platform-service-status__retry-text {
    position: absolute;
    width: 1px;
    height: 1px;
    overflow: hidden;
    clip: rect(0 0 0 0);
    white-space: nowrap;
  }

  .ip-platform-service-status button {
    display: inline-flex;
    align-items: center;
    justify-content: center;
    width: 32px;
    height: 32px;
    padding: 0;
    border: 0;
  }

  .ip-platform-service-status__retry-icon {
    display: block;
    width: 16px;
    height: 16px;
  }
}
</style>
