<script setup lang="ts">
import { computed } from 'vue'
import type { Json } from '@/api/studio'
import ReportMappings from './ReportMappings.vue'
const props = defineProps<{ modelValue: Json; instances: string[]; datasets?: Json[] }>()
const emit = defineEmits<{ 'update:modelValue': [value: Json] }>()
const path = computed({
  get: () => (props.modelValue.path ?? []).join('.'),
  set: (v: string) =>
    emit('update:modelValue', { ...props.modelValue, path: v.split('.').filter(Boolean) }),
})
function mode(v: string) {
  emit('update:modelValue', {
    mode: v,
    path: [],
    ...(v === 'RootPath'
      ? { instanceId: '' }
      : v === 'Dataset'
        ? { datasetRevisionNId: '', parameterMap: [] }
        : {}),
  })
}
</script>
<template>
  <el-select :model-value="modelValue.mode" aria-label="Collection" @update:model-value="mode"
    ><el-option v-for="v in ['RootPath', 'ParentPath', 'Dataset']" :key="v" :value="v" :label="v"
  /></el-select>
  <el-select
    v-if="modelValue.mode === 'RootPath'"
    :model-value="modelValue.instanceId"
    @update:model-value="
      (value: string) => emit('update:modelValue', { ...modelValue, instanceId: value })
    "
    aria-label="Instance"
    ><el-option v-for="id in instances" :key="id" :value="id" :label="id"
  /></el-select>
  <el-select
    v-if="modelValue.mode === 'Dataset'"
    :model-value="modelValue.datasetRevisionNId"
    @update:model-value="
      (value: string) => emit('update:modelValue', { ...modelValue, datasetRevisionNId: value })
    "
    aria-label="Dataset revision"
    ><el-option
      v-for="d in datasets ?? []"
      :key="d.nId"
      :value="d.publishedRevisionNId"
      :label="d.name"
  /></el-select>
  <el-input v-model="path" aria-label="Collection path" placeholder="path.to.rows" />
  <ReportMappings
    v-if="modelValue.mode === 'Dataset'"
    :model-value="modelValue.parameterMap"
    @update:model-value="
      (value: Json[]) => emit('update:modelValue', { ...modelValue, parameterMap: value })
    "
    :instances="instances"
    label="Parameters"
  />
</template>
