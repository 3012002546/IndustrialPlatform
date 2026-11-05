<script setup lang="ts">
import BindingEditor from '../shared/BindingEditor.vue'
import { rowBinding, traceContexts, type TraceMapping } from './model'
import { useTraceCopy } from './copy'
const model = defineModel<TraceMapping[]>({ required: true })
withDefaults(defineProps<{ contexts?: string[] }>(), { contexts: () => traceContexts })
const t = useTraceCopy()
</script>
<template>
  <div class="trace-mappings">
    <div v-for="(mapping, i) in model" :key="i" class="studio-field-block">
      <el-input v-model="mapping.target" :aria-label="t('target')" :placeholder="t('target')" />
      <BindingEditor v-model="mapping.source" :contexts="contexts" />
      <el-button :aria-label="t('remove')" @click="model = model.filter((_, index) => index !== i)"
        >−</el-button
      >
    </div>
    <el-button @click="model = [...model, { target: '', source: rowBinding() }]">{{
      t('add')
    }}</el-button>
  </div>
</template>
