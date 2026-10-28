<script setup lang="ts">
import { computed } from 'vue'
import { useLocalizationStore } from '@/stores/localizationStore'
import type { Json } from '@/api/studio'
defineProps<{ parameters?: Json }>()
const locale = useLocalizationStore()
const english = computed(() => locale.locale === 'en-US')
</script>
<template>
  <section class="dashboard-embedded-summary">
    <h3>{{ english ? 'Page parameters' : '页面参数' }}</h3>
    <dl v-if="Object.keys(parameters ?? {}).length">
      <template v-for="(value, key) in parameters" :key="key">
        <dt>{{ key }}</dt>
        <dd>
          {{ value == null ? '—' : typeof value === 'object' ? JSON.stringify(value) : value }}
        </dd>
      </template>
    </dl>
    <p v-else>{{ english ? 'No parameters provided' : '未传入参数' }}</p>
  </section>
</template>
<style scoped>
.dashboard-embedded-summary {
  padding: 12px;
  overflow: auto;
}
dl {
  display: grid;
  grid-template-columns: 1fr 2fr;
  gap: 8px;
}
dt {
  font-weight: 600;
}
dd {
  margin: 0;
  overflow-wrap: anywhere;
}
</style>
