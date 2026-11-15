<script setup lang="ts">
import { computed } from 'vue'
import type { Json } from '@/api/studio'
import ReportMappings from './ReportMappings.vue'
import { useReportCopy } from './copy'
const t = useReportCopy()
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
  <el-select
    :model-value="modelValue.mode"
    :aria-label="t('collectionSource')"
    @update:model-value="mode"
    ><el-option value="RootPath" :label="t('collectionRoot')" />
    <el-option value="ParentPath" :label="t('collectionParent')" />
    <el-option value="Dataset" :label="t('datasetSource')" />
  </el-select>
  <el-select
    v-if="modelValue.mode === 'RootPath'"
    :model-value="modelValue.instanceId"
    @update:model-value="
      (value: string) => emit('update:modelValue', { ...modelValue, instanceId: value })
    "
    :aria-label="t('instance')"
    :placeholder="t('chooseInstance')"
    ><el-option v-for="id in instances" :key="id" :value="id" :label="id"
  /></el-select>
  <el-select
    v-if="modelValue.mode === 'Dataset'"
    :model-value="modelValue.datasetRevisionNId"
    @update:model-value="
      (value: string) => emit('update:modelValue', { ...modelValue, datasetRevisionNId: value })
    "
    :aria-label="t('datasetSource')"
    :placeholder="t('chooseDataset')"
    ><el-option
      v-for="d in datasets ?? []"
      :key="d.nId"
      :value="d.publishedRevisionNId"
      :label="d.name"
  /></el-select>
  <el-input
    v-model="path"
    :aria-label="t('collectionPath')"
    :placeholder="t('collectionPathHint')"
  />
  <ReportMappings
    v-if="modelValue.mode === 'Dataset'"
    :model-value="modelValue.parameterMap"
    @update:model-value="
      (value: Json[]) => emit('update:modelValue', { ...modelValue, parameterMap: value })
    "
    :instances="instances"
    :label="t('params')"
  />
</template>
