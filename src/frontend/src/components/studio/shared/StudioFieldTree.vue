<script setup lang="ts">
import { computed } from 'vue'
import type { Json } from '@/api/studio'
import type { Binding } from './model'
const props = defineProps<{ sources: { label: string; schema: Json; binding: Binding }[] }>()
const emit = defineEmits<{ select: [binding: Binding] }>()
interface FieldNode {
  key: string
  label: string
  binding: Binding
  children: FieldNode[]
}
const nodes = computed(() => {
  let count = 0
  function visit(
    schema: Json,
    binding: Binding,
    label: string,
    key: string,
    depth: number,
  ): FieldNode {
    const result: FieldNode = {
      key,
      label: `${label} · ${schema.type ?? ''}`,
      binding,
      children: [],
    }
    if (++count > 2000 || depth >= 16) return result
    if (schema.type === 'Object')
      result.children = (schema.children ?? []).map((field: Json) =>
        visit(
          field,
          { ...binding, path: [...(binding.path ?? []), String(field.name)] },
          String(field.label || field.name),
          `${key}.${field.name}`,
          depth + 1,
        ),
      )
    else if (schema.type === 'Array' && schema.item)
      result.children = [
        visit(
          schema.item,
          { ...binding, path: [...(binding.path ?? []), '0'] },
          '[]',
          `${key}[]`,
          depth + 1,
        ),
      ]
    return result
  }
  return props.sources.map((source, index) =>
    visit(
      source.schema,
      { ...source.binding, path: source.binding.path ?? [], nullPolicy: 'Error' },
      source.label,
      String(index),
      0,
    ),
  )
})
function drag(event: DragEvent, node: FieldNode) {
  event.dataTransfer?.setData('application/x-platform-studio-binding', JSON.stringify(node.binding))
  if (event.dataTransfer) event.dataTransfer.effectAllowed = 'copy'
}
</script>
<template>
  <el-tree
    :data="nodes"
    node-key="key"
    :expand-on-click-node="false"
    @node-click="(node: FieldNode) => emit('select', node.binding)"
    ><template #default="{ data }"
      ><span draggable="true" @dragstart="drag($event, data)">{{ data.label }}</span></template
    ></el-tree
  >
</template>
