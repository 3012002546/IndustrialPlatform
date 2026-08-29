<script setup lang="ts">
import { computed } from 'vue'

import { localeMessages } from '@/localization/i18n'
import { usePlatformLocale } from '@/localization/localeContext'

export interface PlatformTenantContext {
  id: string
  name: string
}

defineProps<{ tenant: PlatformTenantContext | null }>()
const locale = usePlatformLocale()
const copy = computed(() => localeMessages[locale.value].shell.top)
</script>

<template>
  <span v-if="tenant !== null" class="ip-context-switcher" data-testid="tenant-context">
    <span class="ip-context-switcher__label">{{ copy.tenant }}</span>
    <strong>{{ tenant.name }}</strong>
  </span>
</template>

<style scoped>
.ip-context-switcher {
  display: inline-flex;
  align-items: baseline;
  gap: var(--ip-space-2);
  min-width: 0;
  color: inherit;
}

.ip-context-switcher strong {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.ip-context-switcher__label {
  opacity: 0.72;
}
</style>
