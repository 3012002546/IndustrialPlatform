<script setup lang="ts">
import { computed } from 'vue'
const props = defineProps<{ tool: 'select' | 'pan' | 'connect'; locale: string }>()
const zh = computed(() => props.locale.startsWith('zh'))
</script>
<template>
  <span class="graph-interaction-hint">{{
    tool === 'connect'
      ? zh
        ? '从来源节点的输出端口拖到目标节点的输入端口 · Esc 返回选择'
        : 'Drag an output port to an input port · Esc to select'
      : zh
        ? '拖动空白处平移 · Shift + 拖动框选 · Ctrl/⌘ + 滚轮缩放'
        : 'Drag blank space to pan · Shift-drag to select · Ctrl/⌘ + wheel to zoom'
  }}</span>
</template>
<style scoped>
.graph-interaction-hint {
  color: var(--el-text-color-secondary);
  font-size: 12px;
  overflow-wrap: anywhere;
}
</style>
