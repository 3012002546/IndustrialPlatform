<script setup lang="ts">
import { computed } from 'vue'
import AppDataTable from '@/components/management/AppDataTable.vue'
import type { Json } from '@/api/studio'
import { useStudioCopy } from './copy'
const props = defineProps<{ value: unknown }>(),
  t = useStudioCopy()
const data = computed(() =>
  props.value && typeof props.value === 'object' && 'data' in props.value
    ? (props.value as Json).data
    : props.value,
)
const rows = computed(() =>
  Array.isArray(data.value)
    ? data.value.map((value, index) =>
        typeof value === 'object' && value !== null
          ? { ...value, _sampleId: index }
          : { value, _sampleId: index },
      )
    : [],
)
const columns = computed(() =>
  Object.keys(rows.value[0] ?? {})
    .filter((key) => key !== '_sampleId')
    .map((field) => ({ field, title: field, minWidth: 140 })),
)
function tree(value: unknown, key = 'root'): Json {
  return {
    key,
    label:
      key +
      ': ' +
      (value === null
        ? 'null'
        : Array.isArray(value)
          ? `[${value.length}]`
          : typeof value === 'object'
            ? '{}'
            : String(value)),
    children:
      value && typeof value === 'object'
        ? Object.entries(value).map(([key, value]) => tree(value, key))
        : [],
  }
}
const items = computed(() => [tree(data.value)])
</script>
<template>
  <el-tabs
    ><el-tab-pane v-if="rows.length" :label="t('table')"
      ><AppDataTable
        table-key="studio.sample"
        :columns="columns"
        :rows="rows"
        row-key="_sampleId"
        selection="none"
        toolbar-profile="compact" /></el-tab-pane
    ><el-tab-pane :label="t('tree')"
      ><el-tree :data="items" :props="{ label: 'label', children: 'children' }" /></el-tab-pane
    ><el-tab-pane :label="t('diagnostic')">
      <pre class="studio-json">{{ JSON.stringify(value, null, 2) }}</pre>
    </el-tab-pane></el-tabs
  >
</template>
