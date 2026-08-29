<script setup lang="ts">
defineProps<{ degraded: boolean; unavailable: boolean }>()
const emit = defineEmits<{ retry: [] }>()
</script>

<template>
  <div
    v-if="degraded || unavailable"
    class="ip-platform-service-status"
    data-testid="platform-service-status"
    role="status"
  >
    <span>SystemData 运行策略暂处于降级状态</span>
    <span v-if="unavailable">当前快照不可用</span>
    <button type="button" @click="emit('retry')">重试</button>
  </div>
</template>

<style scoped>
.ip-platform-service-status {
  display: inline-flex;
  align-items: center;
  gap: var(--ip-space-2);
  color: var(--ip-color-warning);
  font-size: var(--ip-font-size-xs);
}

.ip-platform-service-status button {
  color: inherit;
  background: transparent;
  border: 1px solid currentColor;
  border-radius: var(--ip-radius-sm);
  cursor: pointer;
}
</style>
