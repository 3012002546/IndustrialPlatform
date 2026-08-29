<script setup lang="ts">
export interface PlatformTenantContext {
  id: string
  name: string
}

defineProps<{ tenant: PlatformTenantContext | null }>()
</script>

<template>
  <span v-if="tenant !== null" class="ip-context-switcher" data-testid="tenant-context">
    <span class="ip-context-switcher__label">租户</span>
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
