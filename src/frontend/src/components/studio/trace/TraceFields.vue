<script setup lang="ts">
import BindingEditor from '../shared/BindingEditor.vue'
import { rowBinding, traceContexts, type TraceField } from './model'
import { useTraceCopy } from './copy'
import { useTracePresentation } from './presentation'
const model = defineModel<TraceField[]>({ required: true })
withDefaults(defineProps<{ contexts?: string[] }>(), { contexts: () => traceContexts })
const t = useTraceCopy()
const text = useTracePresentation()
</script>
<template>
  <div v-for="(field, i) in model" :key="i" class="studio-field-block">
    <el-input v-model="field.label" :aria-label="t('label')" :placeholder="t('label')" />
    <BindingEditor v-model="field.value" :contexts="contexts" />
    <el-select v-model="field.format" :aria-label="t('format')"
      ><el-option
        v-for="v in ['Text', 'Decimal', 'Date', 'DateTime', 'Boolean']"
        :key="v"
        :value="v"
        :label="text.option(v)"
    /></el-select>
    <el-button :aria-label="t('remove')" @click="model = model.filter((_, index) => index !== i)"
      >−</el-button
    >
  </div>
  <el-button @click="model = [...model, { label: '', value: rowBinding(), format: 'Text' }]">{{
    t('add')
  }}</el-button>
</template>
