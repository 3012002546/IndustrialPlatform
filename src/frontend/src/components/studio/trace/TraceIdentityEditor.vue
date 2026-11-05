<script setup lang="ts">
import BindingEditor from '../shared/BindingEditor.vue'
import { traceContexts, type TraceIdentityBindings } from './model'
import { useTraceCopy } from './copy'
const model = defineModel<TraceIdentityBindings>({ required: true })
const t = useTraceCopy()
</script>
<template>
  <fieldset>
    <legend>{{ t('identity') }}</legend>
    <label v-for="key in ['sourceSystem', 'factory', 'businessKey'] as const" :key="key"
      >{{ t(key) }}<BindingEditor v-model="model[key]" :contexts="traceContexts"
    /></label>
  </fieldset>
</template>
