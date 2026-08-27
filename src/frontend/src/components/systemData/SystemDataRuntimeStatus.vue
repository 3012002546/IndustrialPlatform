<script setup lang="ts">
import { useSystemDataRuntimeStore } from '@/stores/systemData/runtimeStore'

const store = useSystemDataRuntimeStore()

function retry(): void {
  void store.refresh('Pc')
}
</script>

<template>
  <div
    v-if="store.degraded"
    class="systemdata-runtime-status"
    role="status"
    data-testid="systemdata-runtime-degraded"
  >
    <span>SystemData 运行策略暂处于降级状态；已发布导航仍可继续使用。</span>
    <span v-if="store.unavailable">当前快照不可用，管理写入不会使用陈旧数据。</span>
    <button type="button" @click="retry">重试</button>
  </div>
</template>

<style scoped>
.systemdata-runtime-status {
  position: fixed;
  z-index: 1100;
  right: var(--ip-space-4);
  bottom: var(--ip-space-4);
  display: flex;
  align-items: center;
  gap: var(--ip-space-3);
  max-width: min(640px, calc(100vw - 2 * var(--ip-space-4)));
  padding: var(--ip-space-2) var(--ip-space-3);
  color: var(--ip-color-warning);
  background: var(--ip-color-warning-bg);
  border: 1px solid var(--ip-color-warning);
  border-radius: var(--ip-radius-md);
  box-shadow: var(--ip-shadow-md);
  font-size: var(--ip-font-size-sm);
}

.systemdata-runtime-status button {
  flex: 0 0 auto;
  padding: var(--ip-space-1) var(--ip-space-2);
  color: inherit;
  background: transparent;
  border: 1px solid currentcolor;
  border-radius: var(--ip-radius-sm);
  cursor: pointer;
}
</style>
