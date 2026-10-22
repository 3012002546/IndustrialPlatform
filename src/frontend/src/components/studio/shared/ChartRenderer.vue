<script setup lang="ts">
import { computed } from 'vue'
import type { Json } from '@/api/studio'
import { useStudioCopy } from './copy'
const props = defineProps<{ config: Json; rows: Json[] }>()
const t = useStudioCopy()
const series = computed(() => (props.config.series as Json[]) ?? [])
const kind = computed(() => String(props.config.chartType ?? 'Bar'))
const error = computed(() =>
  !['Bar', 'Line', 'Pie'].includes(kind.value) ||
  !props.config.xField ||
  !series.value.length ||
  series.value.some(
    (s) =>
      !s.valueField ||
      props.rows.some(
        (row) =>
          row[String(s.valueField)] == null || !Number.isFinite(Number(row[String(s.valueField)])),
      ),
  )
    ? t.value('fieldMissing')
    : '',
)
const max = computed(() =>
  Math.max(
    1,
    ...series.value.flatMap((s) =>
      props.rows.map((row) => Math.abs(Number(row[String(s.valueField)]))),
    ),
  ),
)
const colors = [
  'var(--el-color-primary)',
  'var(--el-color-success)',
  'var(--el-color-warning)',
  'var(--el-color-danger)',
]
function points(field: string) {
  return props.rows
    .map(
      (row, i) =>
        `${40 + (i * 510) / Math.max(1, props.rows.length - 1)},${230 - (Number(row[field]) / max.value) * 190}`,
    )
    .join(' ')
}
const sum = computed(() =>
  props.rows.reduce(
    (total, row) => total + Math.max(0, Number(row[String(series.value[0]?.valueField)])),
    0,
  ),
)
</script>
<template>
  <el-alert v-if="error" :title="error" type="error" :closable="false" />
  <div v-else>
    <svg
      viewBox="0 0 600 280"
      role="img"
      :aria-label="t('chart')"
      style="width: 100%; max-height: 340px"
    >
      <template v-if="kind !== 'Pie'">
        <path d="M35 15V235H580" fill="none" stroke="var(--el-border-color)" />
        <template v-for="(s, si) in series" :key="si">
          <polyline
            v-if="kind === 'Line'"
            :points="points(String(s.valueField))"
            fill="none"
            :stroke="colors[si % colors.length]"
            stroke-width="3"
          />
          <template v-else>
            <rect
              v-for="(row, i) in rows"
              :key="i"
              :x="
                40 +
                (i * 530) / Math.max(1, rows.length) +
                (si * 530) / Math.max(1, rows.length) / series.length
              "
              :y="230 - (Math.max(0, Number(row[String(s.valueField)])) / max) * 190"
              :height="(Math.max(0, Number(row[String(s.valueField)])) / max) * 190"
              :width="Math.max(2, 500 / Math.max(1, rows.length) / series.length)"
              :fill="colors[si % colors.length]"
            >
              <title>{{ row[String(config.xField)] }}: {{ row[String(s.valueField)] }}</title>
            </rect>
          </template>
        </template>
        <text
          v-for="(row, i) in rows"
          :key="i"
          :x="40 + (i * 530) / Math.max(1, rows.length)"
          y="257"
          fill="currentColor"
          font-size="11"
        >
          {{ row[String(config.xField)] }}
        </text>
      </template>
      <template v-else>
        <circle
          cx="180"
          cy="135"
          r="95"
          fill="none"
          stroke="var(--el-fill-color)"
          stroke-width="50"
        />
        <circle
          v-for="(row, i) in rows"
          :key="i"
          cx="180"
          cy="135"
          r="95"
          fill="none"
          :stroke="colors[i % colors.length]"
          stroke-width="50"
          :stroke-dasharray="`${sum ? (Number(row[String(series[0]?.valueField)]) / sum) * 597 : 0} 597`"
          :stroke-dashoffset="
            (-rows
              .slice(0, i)
              .reduce((a, r) => a + Math.max(0, Number(r[String(series[0]?.valueField)])), 0) /
              (sum || 1)) *
            597
          "
          transform="rotate(-90 180 135)"
        >
          <title>{{ row[String(config.xField)] }}: {{ row[String(series[0]?.valueField)] }}</title>
        </circle>
      </template>
    </svg>
    <div class="studio-tools">
      <span v-for="(s, i) in series" :key="i" :style="{ color: colors[i % colors.length] }">{{
        s.name ?? s.valueField
      }}</span>
    </div>
  </div>
</template>
