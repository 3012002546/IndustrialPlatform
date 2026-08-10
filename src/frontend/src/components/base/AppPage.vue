<script setup lang="ts">
import { useId } from 'vue'

defineProps<{
  /** 页面标题(缺省时不渲染标题区)。 */
  title?: string
  /** 页面说明。 */
  description?: string
}>()

const titleId = useId()
</script>

<template>
  <section class="app-page" :aria-labelledby="title ? titleId : undefined">
    <header v-if="title" class="app-page__header">
      <h1 :id="titleId" class="app-page__title">{{ title }}</h1>
      <p v-if="description" class="app-page__description">{{ description }}</p>
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
}

.app-page__header {
  display: flex;
  flex-direction: column;
  gap: var(--ip-space-1);
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
  min-height: 0;
}
</style>
