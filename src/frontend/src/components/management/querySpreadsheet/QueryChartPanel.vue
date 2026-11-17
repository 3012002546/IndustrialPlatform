<script setup lang="ts">
import { computed, ref } from 'vue'
import ChartRenderer from '@/components/studio/shared/ChartRenderer.vue'
import type { QueryColumn, QueryRow } from './types'
import { chartRows } from './model'
import { useSheetCopy } from './copy'
const props = defineProps<{
  columns: QueryColumn[]
  rows: QueryRow[]
  kind: 'Bar' | 'Line' | 'Pie'
}>()
defineEmits<{ refresh: [] }>()
const t = useSheetCopy()
const category = ref(props.columns[0]?.field ?? ''),
  value = ref(
    props.columns.find((c) => props.rows.some((r) => typeof r[c.field] === 'number'))?.field ?? '',
  ),
  aggregate = ref('sum')
const data = computed(() => {
  try {
    if (!category.value || (aggregate.value !== 'count' && !value.value)) return null
    const rows = chartRows(props.rows, category.value, value.value, aggregate.value)
    if (props.kind === 'Pie' && rows.some((r) => r.value < 0)) return null
    return rows
  } catch {
    return null
  }
})
const config = computed(() => ({
  chartType: props.kind,
  xField: 'category',
  series: [
    {
      valueField: 'value',
      name: props.columns.find((c) => c.field === value.value)?.title ?? t.value('count'),
    },
  ],
}))
</script>
<template>
  <section class="query-chart">
    <div class="query-chart-tools">
      <label
        >{{ t('category')
        }}<el-select v-model="category" :aria-label="t('category')"
          ><el-option
            v-for="column in columns"
            :key="column.field"
            :label="column.title"
            :value="column.field" /></el-select
      ></label>
      <label
        >{{ t('value')
        }}<el-select v-model="value" :disabled="aggregate === 'count'" :aria-label="t('value')"
          ><el-option
            v-for="column in columns"
            :key="column.field"
            :label="column.title"
            :value="column.field" /></el-select
      ></label>
      <label
        >{{ t('aggregate')
        }}<el-select v-model="aggregate" :aria-label="t('aggregate')"
          ><el-option
            v-for="item in ['sum', 'avg', 'count'] as const"
            :key="item"
            :label="t(item)"
            :value="item" /></el-select
      ></label>
      <el-button @click="$emit('refresh')">{{ t('chartRefresh') }}</el-button>
    </div>
    <p>{{ t('chartScope') }}</p>
    <el-alert v-if="!data" :title="t('chartInvalid')" type="warning" :closable="false" />
    <el-empty v-else-if="!data.length" :description="t('noVisible')" />
    <ChartRenderer v-else :config="config" :rows="data" />
  </section>
</template>
<style scoped>
.query-chart {
  border: 1px solid var(--el-border-color);
  padding: 12px;
  color: var(--el-text-color-primary);
}
.query-chart-tools {
  display: flex;
  flex-wrap: wrap;
  gap: 12px;
  align-items: end;
}
.query-chart-tools label {
  display: grid;
  gap: 4px;
  width: 160px;
}
.query-chart p {
  font-size: 12px;
  color: var(--el-text-color-secondary);
}
</style>
