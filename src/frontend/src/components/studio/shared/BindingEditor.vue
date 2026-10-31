<script setup lang="ts">
import { computed } from 'vue'
import type { Json } from '@/api/studio'
import type { Binding } from './model'
import { useStudioCopy } from './copy'
const props = defineProps<{
  modelValue: Binding
  instances?: string[]
  contexts?: string[]
  bands?: { id: string; name: string }[]
  paths?: string[]
}>()
const emit = defineEmits<{ 'update:modelValue': [value: Binding] }>()
const t = useStudioCopy()
const pathOptions = computed(() => [
  { value: '', label: t.value('value') },
  ...(props.paths ?? []).map((value) => ({ value, label: value })),
])
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
  const value = { ...props.modelValue, ...change }
  if (value.from !== 'ParentRow') delete value.contextBandId
  emit('update:modelValue', value)
}
function setAncestor(id: string) {
  const value = { ...props.modelValue }
  if (id) value.contextBandId = id
  else delete value.contextBandId
  emit('update:modelValue', value)
}
function drop(event: DragEvent) {
  const text = event.dataTransfer?.getData('application/x-platform-studio-binding')
  if (!text) return
  try {
    const value = JSON.parse(text) as Binding
    if (
      !value ||
      typeof value.from !== 'string' ||
      !Array.isArray(value.path) ||
      value.path.length > 32 ||
      value.path.some((part) => ['__proto__', 'constructor', 'prototype'].includes(String(part)))
    )
      return
    if (
      !(props.contexts ?? ['Instance', 'Form', 'Row', 'PageParam', 'Context', 'Constant']).includes(
        value.from,
      )
    )
      return
    emit('update:modelValue', { ...value, nullPolicy: props.modelValue.nullPolicy ?? 'Error' })
  } catch {
    /* Only typed Studio field drags are accepted. */
  }
}
</script>
<template>
  <div class="studio-binding-editor" @dragover.prevent @drop.prevent="drop">
    <div class="studio-prop-row">
      <el-select
        :model-value="modelValue.from"
        :aria-label="t('context')"
        @update:model-value="(v: Json[string]) => update({ from: v })"
        ><el-option
          v-for="v in contexts ?? ['Instance', 'Form', 'Row', 'PageParam', 'Context', 'Constant']"
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
    <el-select-v2
      v-if="modelValue.from !== 'Constant' && paths?.length"
      v-model="path"
      filterable
      allow-create
      :options="pathOptions"
      :aria-label="t('path')"
    />
    <el-input
      v-else-if="modelValue.from !== 'Constant'"
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
    <el-select
      v-if="modelValue.from === 'ParentRow' && bands?.length"
      :model-value="modelValue.contextBandId"
      clearable
      :aria-label="t('context')"
      @update:model-value="setAncestor"
      ><el-option v-for="band in bands ?? []" :key="band.id" :value="band.id" :label="band.name"
    /></el-select>
  </div>
</template>
