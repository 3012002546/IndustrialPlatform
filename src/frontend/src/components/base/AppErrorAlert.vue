<script setup lang="ts">
defineProps<{
  title: string
  message?: string
  /** 可展示的 TraceId,便于前后端排查。 */
  traceId?: string
}>()
</script>

<template>
  <div class="app-error-alert" role="alert">
    <h2 class="app-error-alert__title">{{ title }}</h2>
    <p v-if="message" class="app-error-alert__message">{{ message }}</p>
    <p v-if="traceId" class="app-error-alert__trace">TraceId: {{ traceId }}</p>
    <div v-if="$slots.default" class="app-error-alert__actions">
      <slot />
    </div>
  </div>
</template>

<style scoped>
.app-error-alert {
  display: flex;
  flex-direction: column;
  gap: var(--ip-space-1);
  padding: var(--ip-space-4);
  border: 1px solid var(--ip-color-border);
  border-radius: var(--ip-radius-lg);
  background: var(--ip-color-danger-bg);
  color: var(--ip-color-danger);
}

.app-error-alert__title {
  margin: 0;
  font-size: var(--ip-font-size-lg);
  font-weight: 600;
}

.app-error-alert__message {
  margin: 0;
  font-size: var(--ip-font-size-md);
  color: var(--ip-color-text-primary);
}

.app-error-alert__trace {
  margin: 0;
  font-size: var(--ip-font-size-xs);
  font-family: ui-monospace, monospace;
  color: var(--ip-color-text-secondary);
}

.app-error-alert__actions {
  display: flex;
  gap: var(--ip-space-2);
  margin-top: var(--ip-space-2);
}
</style>
