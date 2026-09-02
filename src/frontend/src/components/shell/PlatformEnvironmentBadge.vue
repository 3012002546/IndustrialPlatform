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
  min-height: 19px;
  padding: 2px 5px;
  border: 1px solid currentColor;
  border-radius: var(--ip-radius-sm);
  font-size: 10px;
  font-weight: 500;
  letter-spacing: 0.04em;
  line-height: 15px;
}
</style>
