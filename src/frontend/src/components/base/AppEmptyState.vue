<script setup lang="ts">
defineProps<{
  title: string
  description?: string
}>()
</script>

<template>
  <div class="app-empty-state" role="status">
    <div class="app-empty-state__icon" aria-hidden="true">
      <slot name="icon">
        <svg
          width="40"
          height="40"
          viewBox="0 0 24 24"
          fill="none"
          stroke="currentColor"
          stroke-width="1.5"
          aria-hidden="true"
        >
          <circle cx="12" cy="12" r="9" />
          <path d="M12 8v4M12 16h.01" stroke-linecap="round" />
        </svg>
      </slot>
    </div>
    <h2 class="app-empty-state__title">{{ title }}</h2>
    <p v-if="description" class="app-empty-state__description">{{ description }}</p>
    <div v-if="$slots.default" class="app-empty-state__actions">
      <slot />
    </div>
  </div>
</template>

<style scoped>
.app-empty-state {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  gap: var(--ip-space-2);
  padding: var(--ip-space-8) var(--ip-space-4);
  text-align: center;
  color: var(--ip-color-text-secondary);
}

.app-empty-state__icon {
  color: var(--ip-color-text-disabled);
}

.app-empty-state__title {
  margin: 0;
  font-size: var(--ip-font-size-lg);
  font-weight: 500;
  color: var(--ip-color-text-primary);
}

.app-empty-state__description {
  margin: 0;
  font-size: var(--ip-font-size-md);
}

.app-empty-state__actions {
  margin-top: var(--ip-space-3);
}
</style>
