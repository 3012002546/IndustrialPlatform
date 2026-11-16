<script setup lang="ts">
import { computed } from 'vue'
import { useRoute } from 'vue-router'
import TraceResourceContent from '@/components/studio/trace/TraceResourceContent.vue'
const route = useRoute()
const link = computed(() => ({
  kind: 'Resource',
  modalUrl: window.location.href,
  expiresOn: String(route.query.expiresOn ?? ''),
  embeddable: true,
}))
</script>
<template>
  <main class="trace-resource-preview"><TraceResourceContent :link="link" /></main>
</template>
<style scoped>
.trace-resource-preview {
  height: 100dvh;
  display: flex;
  padding: var(--ip-space-3);
  box-sizing: border-box;
  color: var(--ip-color-text-primary);
  background: var(--ip-color-bg-container);
}
</style>
