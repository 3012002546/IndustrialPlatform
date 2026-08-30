<script setup lang="ts">
import { useId } from 'vue'

defineProps<{
  /** 页面标题(缺省时不渲染标题区)。 */
  title?: string
  /** 页面说明。 */
  description?: string
}>()

defineSlots<{
  default?: () => unknown
  breadcrumb?: () => unknown
  meta?: () => unknown
  actions?: () => unknown
}>()

const titleId = useId()
</script>

<template>
  <section class="app-page" :aria-labelledby="title ? titleId : undefined">
    <header
      v-if="title || $slots.breadcrumb || $slots.meta || $slots.actions"
      class="app-page__header"
    >
      <div v-if="$slots.breadcrumb" class="app-page__breadcrumb">
        <slot name="breadcrumb" />
      </div>
      <div class="app-page__heading-row">
        <div class="app-page__heading">
          <h1 v-if="title" :id="titleId" class="app-page__title">{{ title }}</h1>
          <p v-if="description" class="app-page__description">{{ description }}</p>
        </div>
        <div v-if="$slots.meta || $slots.actions" class="app-page__extensions">
          <div v-if="$slots.meta" class="app-page__meta"><slot name="meta" /></div>
          <div v-if="$slots.actions" class="app-page__actions"><slot name="actions" /></div>
        </div>
      </div>
    </header>
    <div class="app-page__body">
      <slot />
    </div>
  </section>
</template>

<style scoped>
.app-page {
  display: flex;
  flex-direction: column;
  gap: var(--ip-space-4);
  width: 100%;
  min-width: 0;
  box-sizing: border-box;
}

.app-page__header {
  display: flex;
  flex-direction: column;
  gap: var(--ip-space-1);
}

.app-page__breadcrumb {
  color: var(--ip-color-text-secondary);
  font-size: var(--ip-font-size-sm);
}

.app-page__heading-row {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: var(--ip-space-4);
}

.app-page__heading {
  min-width: 0;
}

.app-page__extensions,
.app-page__actions {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  justify-content: flex-end;
  gap: var(--ip-space-2);
}

.app-page__title {
  margin: 0;
  font-size: var(--ip-font-size-xl);
  font-weight: 600;
  line-height: var(--ip-line-height-tight);
  color: var(--ip-color-text-primary);
}

.app-page__description {
  margin: 0;
  font-size: var(--ip-font-size-md);
  line-height: var(--ip-line-height-normal);
  color: var(--ip-color-text-secondary);
}

.app-page__body {
  flex: 1;
  width: 100%;
  min-width: 0;
  min-height: 0;
}
</style>
