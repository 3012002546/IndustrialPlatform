<script setup lang="ts">
import type { Json } from '@/api/studio'
import BindingEditor from '../shared/BindingEditor.vue'
import { reportContexts, binding } from './model'
import { useReportCopy } from './copy'
const t = useReportCopy()
defineProps<{ modelValue: Json[]; instances: string[]; label: string; targetLabel?: string }>()
const emit = defineEmits<{ 'update:modelValue': [value: Json[]] }>()
</script>
<template>
  <fieldset>
    <legend>{{ label }}</legend>
    <div v-for="(map, index) in modelValue ?? []" :key="index" class="studio-field-block">
      <el-input
        v-model="map.target"
        :aria-label="targetLabel ?? t('mappingTarget')"
        :placeholder="targetLabel ?? t('mappingTarget')"
      />
      <BindingEditor v-model="map.source" :instances="instances" :contexts="reportContexts" />
      <el-button
        @click="
          emit(
            'update:modelValue',
            modelValue.filter((_, i) => i !== index),
          )
        "
        >−</el-button
      >
    </div>
    <el-button
      @click="
        emit('update:modelValue', [
          ...(modelValue ?? []),
          { target: '', source: binding('PageParam') },
        ])
      "
      >+</el-button
    >
  </fieldset>
</template>
