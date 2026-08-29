<script setup lang="ts">
import type { DeploymentEnvironment } from '@/config/runtimeConfig'

defineProps<{ environment: DeploymentEnvironment }>()
</script>

<template>
  <span class="ip-environment-badge" data-testid="environment-badge" :data-environment="environment">
    {{ environment }}
  </span>
</template>

<style scoped>
.ip-environment-badge {
  display: inline-flex;
  align-items: center;
  min-height: 22px;
  padding: 0 var(--ip-space-2);
  border: 1px solid currentColor;
  border-radius: 999px;
  font-size: var(--ip-font-size-xs);
  font-weight: 700;
  letter-spacing: 0.04em;
}
</style>
