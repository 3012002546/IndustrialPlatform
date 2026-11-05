<script setup lang="ts">
import { computed } from 'vue'

const props = withDefaults(
  defineProps<{ zoom: number; locale: string; disabled?: boolean; size?: 'small' | 'default' }>(),
  { size: 'small' },
)
const emit = defineEmits<{ zoom: [value: number]; fit: [] }>()
const zh = computed(() => props.locale.startsWith('zh'))
</script>

<template>
  <div class="graph-viewport-controls">
    <el-button
      :size="size"
      :disabled="disabled"
      :aria-label="zh ? '缩小' : 'Zoom out'"
      @click="emit('zoom', zoom / 1.2)"
      >−</el-button
    >
    <span>{{ Math.round(zoom * 100) }}%</span>
    <el-button
      :size="size"
      :disabled="disabled"
      :aria-label="zh ? '放大' : 'Zoom in'"
      @click="emit('zoom', zoom * 1.2)"
      >+</el-button
    >
    <el-button :size="size" :disabled="disabled" @click="emit('fit')">{{
      zh ? '适应画布' : 'Fit'
    }}</el-button>
    <el-button :size="size" :disabled="disabled" @click="emit('zoom', 1)">100%</el-button>
  </div>
</template>

<style scoped>
.graph-viewport-controls {
  display: flex;
  align-items: center;
  gap: 8px;
  flex-shrink: 0;
  white-space: nowrap;
}
.graph-viewport-controls :deep(.el-button + .el-button) {
  margin-left: 0;
}
</style>
