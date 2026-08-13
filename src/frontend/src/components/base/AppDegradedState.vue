<script setup lang="ts">
/**
 * AppDegradedState(PF-01 §7.10):部分能力降级,必须说明不可用与仍可继续的能力。
 * 提供 retry 槽让调用方重试。
 */

withDefaults(
  defineProps<{
    title?: string
    unavailable: string[]
    available: string[]
  }>(),
  { title: '部分能力暂不可用' },
)
</script>

<template>
  <div class="app-degraded-state" role="alert" data-testid="app-degraded-state">
    <h2 class="app-degraded-state__title">{{ title }}</h2>
    <div class="app-degraded-state__section">
      <p class="app-degraded-state__label app-degraded-state__label--unavailable">暂不可用</p>
      <ul class="app-degraded-state__list app-degraded-state__list--unavailable">
        <li v-for="item in unavailable" :key="item">{{ item }}</li>
      </ul>
    </div>
    <div class="app-degraded-state__section">
      <p class="app-degraded-state__label app-degraded-state__label--available">仍可使用</p>
      <ul class="app-degraded-state__list app-degraded-state__list--available">
        <li v-for="item in available" :key="item">{{ item }}</li>
      </ul>
    </div>
    <div v-if="$slots.retry" class="app-degraded-state__retry">
      <slot name="retry" />
    </div>
  </div>
</template>

<style scoped>
.app-degraded-state {
  display: flex;
  flex-direction: column;
  gap: var(--ip-space-3);
  padding: var(--ip-space-4);
  border: 1px solid var(--ip-color-border);
  border-radius: var(--ip-radius-lg);
  background: var(--ip-color-warning-bg);
  color: var(--ip-color-warning);
}

.app-degraded-state__title {
  margin: 0;
  font-size: var(--ip-font-size-lg);
  font-weight: 600;
}

.app-degraded-state__section {
  display: flex;
  flex-direction: column;
  gap: var(--ip-space-1);
}

.app-degraded-state__label {
  margin: 0;
  font-size: var(--ip-font-size-sm);
  font-weight: 600;
}

.app-degraded-state__label--unavailable {
  color: var(--ip-color-danger);
}

.app-degraded-state__label--available {
  color: var(--ip-color-success);
}

.app-degraded-state__list {
  margin: 0;
  padding-left: var(--ip-space-5);
  font-size: var(--ip-font-size-md);
}

.app-degraded-state__list--unavailable {
  color: var(--ip-color-danger);
}

.app-degraded-state__list--available {
  color: var(--ip-color-text-primary);
}

.app-degraded-state__retry {
  display: flex;
  gap: var(--ip-space-2);
  margin-top: var(--ip-space-2);
}
</style>
