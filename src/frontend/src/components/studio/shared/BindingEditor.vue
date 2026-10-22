<script setup lang="ts">
import { computed } from 'vue'
import type { Json } from '@/api/studio'
import type { Binding } from './model'
import { useStudioCopy } from './copy'
const props = defineProps<{ modelValue: Binding; instances?: string[] }>()
const emit = defineEmits<{ 'update:modelValue': [value: Binding] }>()
const t = useStudioCopy()
const path = computed({
  get: () => (props.modelValue.path ?? []).join('.'),
  set: (value) => update({ path: value.split('.').filter(Boolean) }),
})
const constant = computed({
  get: () => JSON.stringify(props.modelValue.value ?? ''),
  set: (value) => {
    try {
      update({ value: JSON.parse(value) as unknown })
    } catch {
      update({ value })
    }
  },
})
function update(change: Partial<Binding>) {
  emit('update:modelValue', { ...props.modelValue, ...change })
}
</script>
<template>
  <div class="studio-prop-row">
    <el-select
      :model-value="modelValue.from"
      :aria-label="t('context')"
      @update:model-value="(v: Json[string]) => update({ from: v })"
      ><el-option
        v-for="v in ['Instance', 'Form', 'Row', 'PageParam', 'Context', 'Constant']"
        :key="v"
        :label="v"
        :value="v" /></el-select
    ><el-select
      :model-value="modelValue.nullPolicy ?? 'Error'"
      :aria-label="t('nullPolicy')"
      @update:model-value="(v: Json[string]) => update({ nullPolicy: v })"
      ><el-option v-for="v in ['Error', 'Null', 'Default']" :key="v" :label="v" :value="v"
    /></el-select>
  </div>
  <el-select
    v-if="modelValue.from === 'Instance'"
    :model-value="modelValue.instanceId"
    :placeholder="t('instanceId')"
    :aria-label="t('instanceId')"
    filterable
    allow-create
    @update:model-value="(v: Json[string]) => update({ instanceId: v })"
    ><el-option v-for="v in instances ?? []" :key="v" :label="v" :value="v"
  /></el-select>
  <el-input
    v-if="modelValue.from !== 'Constant'"
    v-model="path"
    :placeholder="t('path')"
    :aria-label="t('path')"
  /><el-input v-else v-model="constant" :placeholder="t('value')" :aria-label="t('value')" />
  <el-input
    v-if="modelValue.nullPolicy === 'Default'"
    :model-value="String(modelValue.defaultValue ?? '')"
    :placeholder="t('defaultValue')"
    @update:model-value="(v: Json[string]) => update({ defaultValue: v })"
  />
</template>
