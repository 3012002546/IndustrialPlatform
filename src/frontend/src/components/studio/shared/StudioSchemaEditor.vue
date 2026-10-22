<script setup lang="ts">
import type { Json } from '@/api/studio'
import { clone } from './model'
import { useStudioCopy } from './copy'
const props = withDefaults(defineProps<{ modelValue: Json; depth?: number }>(), { depth: 0 })
const emit = defineEmits<{ 'update:modelValue': [value: Json]; remove: [] }>()
const t = useStudioCopy()
function update(key: string, value: unknown) {
  const next = { ...props.modelValue, [key]: value }
  if (key === 'type') {
    delete next.children
    delete next.item
    if (value === 'Object') next.children = []
    if (value === 'Array')
      next.item = {
        id: crypto.randomUUID(),
        name: 'item',
        label: 'item',
        type: 'Object',
        nullable: false,
        children: [],
      }
  }
  emit('update:modelValue', next)
}
function child(index: number, value: Json) {
  const children = clone(props.modelValue.children as Json[])
  children[index] = value
  update('children', children)
}
function remove(index: number) {
  const children = clone(props.modelValue.children as Json[])
  children.splice(index, 1)
  update('children', children)
}
function add() {
  update('children', [
    ...((props.modelValue.children as Json[]) ?? []),
    {
      id: crypto.randomUUID(),
      name: 'field' + (((props.modelValue.children as Json[]) ?? []).length + 1),
      label: '',
      type: 'String',
      nullable: true,
    },
  ])
}
</script>
<template>
  <div
    class="studio-binding"
    :style="{
      paddingLeft: depth ? '14px' : '0',
      borderLeft: depth ? '2px solid var(--el-border-color)' : '0',
    }"
  >
    <div class="studio-tools">
      <el-input
        :model-value="modelValue.name"
        :aria-label="t('fieldName')"
        :placeholder="t('fieldName')"
        @update:model-value="(v: Json[string]) => update('name', v)"
      /><el-input
        :model-value="modelValue.label"
        :aria-label="t('label')"
        :placeholder="t('label')"
        @update:model-value="(v: Json[string]) => update('label', v)"
      /><el-select
        :model-value="modelValue.type"
        :aria-label="t('type')"
        @update:model-value="(v: Json[string]) => update('type', v)"
        ><el-option
          v-for="v in [
            'Object',
            'Array',
            'String',
            'Int32',
            'Int64',
            'Decimal',
            'Boolean',
            'Date',
            'DateTime',
            'Guid',
          ]"
          :key="v"
          :label="v"
          :value="v" /></el-select
      ><el-checkbox
        :model-value="!!modelValue.nullable"
        @update:model-value="(v: Json[string]) => update('nullable', v)"
        >Nullable</el-checkbox
      ><el-button v-if="depth" link type="danger" @click="emit('remove')">{{
        t('remove')
      }}</el-button>
    </div>
    <small>{{ modelValue.id }}</small>
    <template v-if="modelValue.type === 'Object'"
      ><StudioSchemaEditor
        v-for="(entry, index) in (modelValue.children as Json[]) ?? []"
        :key="String(entry.id)"
        :model-value="entry"
        :depth="depth + 1"
        @update:model-value="(v: Json[string]) => child(index, v)"
        @remove="remove(index)"
      /><el-button link type="primary" @click="add"
        >{{ t('add') }} {{ t('field') }}</el-button
      ></template
    >
    <StudioSchemaEditor
      v-if="modelValue.type === 'Array' && modelValue.item"
      :model-value="modelValue.item as Json"
      :depth="depth + 1"
      @update:model-value="(v: Json[string]) => update('item', v)"
    />
  </div>
</template>
