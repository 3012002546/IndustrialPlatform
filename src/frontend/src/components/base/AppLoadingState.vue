<script setup lang="ts">
/**
 * AppLoadingState(PF-01 §7.10):加载状态,role="status" 供读屏播报。
 */

withDefaults(defineProps<{ label?: string }>(), { label: '加载中…' })
</script>

<template>
  <div class="app-loading-state" role="status" data-testid="app-loading-state">
    <span class="app-loading-state__spinner" aria-hidden="true" />
    <p class="app-loading-state__label">{{ label }}</p>
  </div>
</template>

<style scoped>
.app-loading-state {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: var(--ip-space-3);
  padding: var(--ip-space-8) var(--ip-space-4);
  color: var(--ip-color-text-secondary);
}

.app-loading-state__spinner {
  width: 20px;
  height: 20px;
  border: 2px solid var(--ip-color-border-strong);
  border-top-color: var(--ip-color-primary);
  border-radius: var(--ip-radius-full);
  animation: app-loading-spin 0.8s linear infinite;
}

.app-loading-state__label {
  margin: 0;
  font-size: var(--ip-font-size-md);
}

@keyframes app-loading-spin {
  to {
    transform: rotate(360deg);
  }
}

@media (prefers-reduced-motion: reduce) {
  .app-loading-state__spinner {
    animation: none;
  }
}
</style>
