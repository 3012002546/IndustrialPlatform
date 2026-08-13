<script setup lang="ts">
/**
 * AppPermissionState(PF-01 §7.10):无权限语义,不把 403 渲染为空数据。
 */

withDefaults(
  defineProps<{
    title?: string
    description?: string
  }>(),
  { title: '暂无访问权限', description: '你暂无查看此页面的权限,如需访问请联系管理员。' },
)
</script>

<template>
  <div class="app-permission-state" role="note" data-testid="app-permission-state">
    <svg
      class="app-permission-state__icon"
      width="40"
      height="40"
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      stroke-width="1.5"
      aria-hidden="true"
    >
      <rect x="4.5" y="10" width="15" height="10" rx="1.5" />
      <path d="M8 10V7a4 4 0 0 1 8 0v3" stroke-linecap="round" />
      <circle cx="12" cy="15" r="1" fill="currentColor" />
    </svg>
    <h2 class="app-permission-state__title">{{ title }}</h2>
    <p class="app-permission-state__description">{{ description }}</p>
    <div v-if="$slots.default" class="app-permission-state__actions">
      <slot />
    </div>
  </div>
</template>

<style scoped>
.app-permission-state {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  gap: var(--ip-space-2);
  padding: var(--ip-space-8) var(--ip-space-4);
  text-align: center;
  color: var(--ip-color-text-secondary);
}

.app-permission-state__icon {
  color: var(--ip-color-text-disabled);
}

.app-permission-state__title {
  margin: 0;
  font-size: var(--ip-font-size-lg);
  font-weight: 500;
  color: var(--ip-color-text-primary);
}

.app-permission-state__description {
  margin: 0;
  font-size: var(--ip-font-size-md);
}

.app-permission-state__actions {
  margin-top: var(--ip-space-3);
}
</style>
