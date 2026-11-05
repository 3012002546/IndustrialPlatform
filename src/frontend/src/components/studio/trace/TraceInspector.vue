<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import type { StudioResource } from '@/api/studio'
import type { Target } from '../graph/types'
import { clone } from '../shared/model'
import type { TraceDocument } from './model'
import type { TracePropertyCommand } from './adapter'
import TraceStartEditor from './TraceStartEditor.vue'
import TraceObjectEditor from './TraceObjectEditor.vue'
import TraceRelationEditor from './TraceRelationEditor.vue'
import { useTraceCopy } from './copy'
import { useTracePresentation } from './presentation'
const props = defineProps<{
  document: TraceDocument
  target: Target | null
  datasets: StudioResource[]
  readOnly?: boolean
}>()
const emit = defineEmits<{ apply: [command: TracePropertyCommand] }>()
const t = useTraceCopy(),
  text = useTracePresentation(),
  local = ref(clone(props.document))
watch(
  () => [props.document, props.target],
  () => {
    local.value = clone(props.document)
  },
)
const objectIndex = computed(() =>
  props.target?.kind === 'node'
    ? local.value.objects.findIndex((o) => o.id === props.target?.id)
    : -1,
)
const relationIndex = computed(() =>
  props.target?.kind === 'edge'
    ? local.value.relations.findIndex((r) => r.id === props.target?.id)
    : -1,
)
function apply() {
  if (props.readOnly) return
  const object = local.value.objects[objectIndex.value],
    relation = local.value.relations[relationIndex.value]
  if (object) emit('apply', { type: 'trace.object', object: clone(object) })
  else if (relation) emit('apply', { type: 'trace.relation', relation: clone(relation) })
  else
    emit('apply', {
      type: 'trace.start',
      start: clone(local.value.start),
      parameters: clone(local.value.parameters),
      scope: clone(local.value.scope),
      limits: clone(local.value.limits),
      ...(local.value.presentation ? { presentation: clone(local.value.presentation) } : {}),
    })
}
defineExpose({ apply })
</script>
<template>
  <div class="trace-inspector">
    <div class="trace-inspector-heading">
      <h3>{{ t('properties') }}</h3>
      <div class="trace-inspector-actions">
        <slot name="actions" />
      </div>
    </div>
    <div class="trace-selection-title">
      {{
        objectIndex >= 0
          ? text.itemName(local.objects[objectIndex]!, objectIndex)
          : relationIndex >= 0
            ? text.itemName(local.relations[relationIndex]!, relationIndex, 'relation')
            : t('start')
      }}
    </div>
    <el-form :disabled="readOnly" label-position="top">
      <TraceObjectEditor
        v-if="objectIndex >= 0"
        :key="local.objects[objectIndex]!.id"
        v-model="local.objects[objectIndex]!"
        :datasets="datasets"
      />
      <TraceRelationEditor
        v-else-if="relationIndex >= 0"
        :key="local.relations[relationIndex]!.id"
        v-model="local.relations[relationIndex]!"
        :datasets="datasets"
        :objects="local.objects"
      />
      <TraceStartEditor v-else v-model="local" :datasets="datasets" />
    </el-form>
    <div class="trace-inspector-footer">
      <el-button type="primary" :disabled="readOnly" @click="apply">{{ t('apply') }}</el-button>
    </div>
  </div>
</template>
<style scoped>
.trace-inspector {
  display: flex;
  flex-direction: column;
  height: 100%;
  min-height: 0;
  gap: 10px;
}
.trace-inspector > .el-form {
  flex: 1;
  min-height: 0;
  overflow: hidden;
}
.trace-inspector :deep(.trace-property-tabs) {
  height: 100%;
  display: flex;
  flex-direction: column;
}
.trace-inspector :deep(.el-tabs__header) {
  flex: none;
  margin-bottom: 12px;
}
.trace-inspector :deep(.el-tabs__content) {
  flex: 1;
  min-height: 0;
  overflow: auto;
  scrollbar-width: thin;
  padding-right: 4px;
}
.trace-inspector :deep(.el-tabs__item) {
  padding-inline: 8px;
}
.trace-inspector-heading,
.trace-selection-title,
.trace-inspector-footer {
  flex: none;
}
.trace-selection-title {
  padding: 10px 12px;
  background: var(--el-fill-color-light);
  border: 1px solid var(--el-border-color-lighter);
  border-radius: var(--el-border-radius-base);
  overflow-wrap: anywhere;
  font-weight: 600;
}
.trace-inspector-heading {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 8px;
  background: var(--el-bg-color);
}
.trace-inspector-heading h3 {
  margin: 0;
  font-size: 16px;
}
.trace-inspector-actions {
  display: flex;
  align-items: center;
  gap: 8px;
}
.trace-inspector-actions :deep(.el-button + .el-button) {
  margin-left: 0;
}
.trace-inspector-footer {
  display: flex;
  justify-content: flex-end;
  padding-top: 10px;
  border-top: 1px solid var(--el-border-color-lighter);
}
.trace-inspector :deep(label) {
  display: grid;
  gap: 5px;
  margin-bottom: 14px;
  font-size: 14px;
  line-height: 1.6;
  color: var(--el-text-color-regular);
}
.trace-inspector :deep(fieldset) {
  border: 0;
  padding: 0;
  margin: 0 0 16px;
  min-width: 0;
}
.trace-inspector :deep(legend) {
  padding: 0;
  margin-bottom: 10px;
  font-size: 13px;
  font-weight: 600;
  color: var(--el-text-color-primary);
}
.trace-inspector :deep(.el-select) {
  width: 100%;
}
.trace-inspector :deep(.trace-size-fields) {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 8px;
}
.trace-inspector :deep(.el-input-number) {
  width: 100%;
}
.trace-inspector :deep(.trace-technical-id) {
  margin: 4px 0 14px;
  font-size: 12px;
  color: var(--el-text-color-secondary);
}
.trace-inspector :deep(.studio-field-block) {
  display: grid;
  gap: 6px;
  margin: 10px 0;
  padding-bottom: 10px;
  border-bottom: 1px solid var(--el-border-color-lighter);
}
</style>
