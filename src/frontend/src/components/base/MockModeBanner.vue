<script setup lang="ts">
import { loadRuntimeConfig } from '@/config/runtimeConfig'

defineProps<{
  /** 覆盖默认文案;默认强调“开发 Mock 模式,非生产”。 */
  label?: string
}>()

// 仅在 Mock 模式下显示:http(真实 Identity)模式下徽标会误导用户以为仍是演示数据。
const showMockBadge = loadRuntimeConfig().authMode === 'mock'
</script>

<template>
  <div v-if="showMockBadge" class="mock-mode-banner" role="status">
    <span class="mock-mode-banner__dot" aria-hidden="true"></span>
    <span>{{ label ?? '开发 Mock 模式 · 仅本地开发演示账号' }}</span>
  </div>
</template>

<style scoped>
.mock-mode-banner {
  display: inline-flex;
  align-items: center;
  gap: var(--ip-space-2);
  padding: var(--ip-space-1) var(--ip-space-3);
  border-radius: var(--ip-radius-full);
  background: var(--ip-color-warning-bg);
  color: var(--ip-color-warning);
  font-size: var(--ip-font-size-xs);
  line-height: var(--ip-line-height-normal);
}

.mock-mode-banner__dot {
  width: 8px;
  height: 8px;
  border-radius: var(--ip-radius-full);
  background: currentColor;
}
</style>
