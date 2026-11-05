<script setup lang="ts">
import { Position, Pointer, TopRight } from '@element-plus/icons-vue'
import { useTraceCopy } from './copy'
defineProps<{ readOnly?: boolean }>()
const tool = defineModel<'select' | 'pan' | 'connect'>('tool', { required: true })
const t = useTraceCopy()
function drag(event: DragEvent) {
  tool.value = 'select'
  event.dataTransfer?.setData('application/x-studio-graph-node', 'trace.object')
}
</script>
<template>
  <div class="trace-graph-tools" role="toolbar" :aria-label="t('palette')">
    <div class="trace-pointer-tools">
      <el-tooltip :content="t('selectTool')" placement="top">
        <el-button
          :icon="Position"
          :aria-label="t('selectTool')"
          :aria-pressed="tool === 'select'"
          :type="tool === 'select' ? 'primary' : 'default'"
          @click="tool = 'select'"
          >{{ t('selectTool') }}</el-button
        >
      </el-tooltip>
      <el-tooltip :content="t('panTool')" placement="top">
        <el-button
          :icon="Pointer"
          :aria-label="t('panTool')"
          :aria-pressed="tool === 'pan'"
          :type="tool === 'pan' ? 'primary' : 'default'"
          @click="tool = 'pan'"
          >{{ t('panTool') }}</el-button
        >
      </el-tooltip>
    </div>
    <div class="trace-shape-tools">
      <el-tooltip :content="t('objectToolHint')" placement="right">
        <el-button
          class="trace-object-tool"
          :disabled="readOnly"
          :aria-label="t('objectTool')"
          :draggable="!readOnly"
          @dragstart="readOnly ? $event.preventDefault() : drag($event)"
        >
          <svg width="22" height="22" viewBox="0 0 24 24" fill="none" aria-hidden="true">
            <rect
              x="3"
              y="4"
              width="18"
              height="16"
              rx="3"
              stroke="currentColor"
              stroke-width="1.6"
            />
          </svg>
          <span>{{ t('objectTool') }}</span>
        </el-button>
      </el-tooltip>
      <el-tooltip :content="t('connectToolHint')" placement="right">
        <el-button
          :disabled="readOnly"
          :aria-label="t('connectTool')"
          :aria-pressed="tool === 'connect'"
          :type="tool === 'connect' ? 'primary' : 'default'"
          @click="tool = 'connect'"
          ><el-icon><TopRight /></el-icon><span>{{ t('connectTool') }}</span></el-button
        >
      </el-tooltip>
    </div>
  </div>
</template>
<style scoped>
.trace-graph-tools {
  display: grid;
  gap: 8px;
}
.trace-graph-tools .el-button {
  margin: 0;
}
.trace-pointer-tools,
.trace-shape-tools {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 8px;
}
.trace-shape-tools .el-button {
  width: 100%;
  height: auto;
  aspect-ratio: 1;
  padding: 8px;
}
.trace-shape-tools :deep(.el-button > span) {
  display: flex;
  flex-direction: column;
  gap: 10px;
  align-items: center;
}
.trace-shape-tools :deep(.el-icon) {
  font-size: 24px;
}
.trace-object-tool:not(:disabled) {
  cursor: grab;
}
.trace-object-tool:not(:disabled):active {
  cursor: grabbing;
}
</style>
