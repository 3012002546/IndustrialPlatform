<script setup lang="ts">
import { computed } from 'vue'
import type { Json } from '@/api/studio'
import { clone } from '@/components/studio/shared/model'
import { advancedOptions, advancedPath, patchAdvancedControl } from './advancedControls'
import { useReportCopy } from './copy'
const props = defineProps<{ modelValue: Json }>()
const emit = defineEmits<{ 'update:modelValue': [value: Json] }>()
const t = useReportCopy()
const type = computed(() => String(props.modelValue.type))
const options = computed(() => ({ ...advancedOptions(type.value), ...props.modelValue.advanced }))
const series = computed<Json[]>(() => options.value.series ?? [])
function patch(value: Json) {
  emit('update:modelValue', patchAdvancedControl(props.modelValue, value))
}
function path(key: string, value: string) {
  patch({ [key]: advancedPath(value) })
}
function seriesPatch(index: number, value: Json) {
  const next = clone(series.value)
  next[index] = { ...next[index], ...value }
  patch({ series: next })
}
function addSeries() {
  if (series.value.length < 8)
    patch({
      series: [
        ...clone(series.value),
        { name: t.value('seriesName') + ' ' + (series.value.length + 1), valuePath: ['value'] },
      ],
    })
}
function removeSeries(index: number) {
  if (series.value.length > 1) patch({ series: series.value.filter((_, i) => i !== index) })
}
</script>
<template>
  <section class="advanced-editor" :aria-label="t('advancedSettings')">
    <h4>{{ t('advancedSettings') }}</h4>
    <el-form label-position="top">
      <el-form-item v-if="type === 'CharacterComb'" :label="t('combCells')">
        <el-input-number
          :model-value="options.cells"
          :min="1"
          :max="128"
          :precision="0"
          :aria-label="t('combCells')"
          @update:model-value="patch({ cells: $event })"
        />
      </el-form-item>
      <template v-if="type === 'Shape' || type === 'Line'">
        <el-form-item v-if="type === 'Shape'" :label="t('shapeKind')">
          <el-select
            :model-value="options.kind"
            :aria-label="t('shapeKind')"
            @update:model-value="patch({ kind: $event })"
          >
            <el-option
              v-for="kind in ['Rectangle', 'Ellipse', 'Diamond'] as const"
              :key="kind"
              :value="kind"
              :label="t(kind)"
            />
          </el-select>
        </el-form-item>
        <template v-else>
          <el-form-item :label="t('lineDirection')"
            ><el-select
              :model-value="options.direction"
              :aria-label="t('lineDirection')"
              @update:model-value="patch({ direction: $event })"
            >
              <el-option
                v-for="direction in [
                  'Horizontal',
                  'Vertical',
                  'DiagonalDown',
                  'DiagonalUp',
                ] as const"
                :key="direction"
                :value="direction"
                :label="t(direction)"
              /> </el-select
          ></el-form-item>
          <el-form-item :label="t('lineDash')"
            ><el-select
              :model-value="options.dash"
              :aria-label="t('lineDash')"
              @update:model-value="patch({ dash: $event })"
            >
              <el-option
                v-for="dash in ['Solid', 'Dashed', 'Dotted'] as const"
                :key="dash"
                :value="dash"
                :label="t(dash)"
              /> </el-select
          ></el-form-item>
        </template>
        <el-form-item :label="t('strokeColor')"
          ><el-color-picker
            :model-value="options.stroke"
            :aria-label="t('strokeColor')"
            @update:model-value="patch({ stroke: $event ?? '#000000' })"
        /></el-form-item>
        <el-form-item :label="t('strokeWidth')"
          ><el-input-number
            :model-value="options.strokeWidthMm"
            :min="0.1"
            :max="5"
            :step="0.1"
            :precision="2"
            :aria-label="t('strokeWidth')"
            @update:model-value="patch({ strokeWidthMm: $event })"
        /></el-form-item>
        <template v-if="type === 'Shape'">
          <el-checkbox
            :model-value="options.fill === 'none'"
            @update:model-value="patch({ fill: $event ? 'none' : '#FFFFFF' })"
            >{{ t('noFill') }}</el-checkbox
          >
          <el-form-item v-if="options.fill !== 'none'" :label="t('fillColor')"
            ><el-color-picker
              :model-value="options.fill"
              :aria-label="t('fillColor')"
              @update:model-value="patch({ fill: $event ?? 'none' })"
          /></el-form-item>
        </template>
      </template>
      <template v-if="type === 'Barcode'">
        <p class="advanced-help">{{ t('barcodeHint') }}</p>
        <el-checkbox
          :model-value="options.showText"
          @update:model-value="patch({ showText: $event })"
          >{{ t('showBarcodeText') }}</el-checkbox
        >
      </template>
      <template v-if="type === 'Chart'">
        <el-form-item :label="t('chartKind')"
          ><el-select
            :model-value="options.kind"
            :aria-label="t('chartKind')"
            @update:model-value="patch({ kind: $event })"
            ><el-option
              v-for="kind in ['Bar', 'Line'] as const"
              :key="kind"
              :value="kind"
              :label="t(kind)" /></el-select
        ></el-form-item>
        <el-form-item :label="t('categoryPath')"
          ><el-input
            :model-value="options.categoryPath?.join('.')"
            :aria-label="t('categoryPath')"
            :placeholder="t('pathSegmentsHint')"
            @update:model-value="path('categoryPath', $event)"
        /></el-form-item>
        <fieldset v-for="(item, index) in series" :key="index" class="advanced-series">
          <legend>{{ t('seriesName') }} {{ index + 1 }}</legend>
          <el-input
            :model-value="item.name"
            :aria-label="t('seriesName') + ' ' + (index + 1)"
            maxlength="128"
            @update:model-value="seriesPatch(index, { name: $event })"
          />
          <el-input
            :model-value="item.valuePath?.join('.')"
            :aria-label="t('valuePath') + ' ' + (index + 1)"
            :placeholder="t('valuePath')"
            @update:model-value="seriesPatch(index, { valuePath: advancedPath($event) })"
          />
          <el-button :disabled="series.length <= 1" @click="removeSeries(index)">{{
            t('removeSeries')
          }}</el-button>
        </fieldset>
        <el-button :disabled="series.length >= 8" @click="addSeries">{{
          t('addSeries')
        }}</el-button>
      </template>
      <template v-if="type === 'Gauge'">
        <el-form-item :label="t('gaugeMinimum')"
          ><el-input-number
            :model-value="options.min"
            :aria-label="t('gaugeMinimum')"
            @update:model-value="patch({ min: $event })"
        /></el-form-item>
        <el-form-item :label="t('gaugeMaximum')"
          ><el-input-number
            :model-value="options.max"
            :aria-label="t('gaugeMaximum')"
            @update:model-value="patch({ max: $event })"
        /></el-form-item>
        <el-form-item :label="t('gaugeLabel')"
          ><el-input
            :model-value="options.label"
            :aria-label="t('gaugeLabel')"
            maxlength="128"
            @update:model-value="patch({ label: $event })"
        /></el-form-item>
      </template>
      <template v-if="type === 'PivotTable'">
        <el-form-item :label="t('pivotRowPath')"
          ><el-input
            :model-value="options.rowPath?.join('.')"
            :aria-label="t('pivotRowPath')"
            @update:model-value="path('rowPath', $event)"
        /></el-form-item>
        <el-form-item :label="t('pivotColumnPath')"
          ><el-input
            :model-value="options.columnPath?.join('.')"
            :aria-label="t('pivotColumnPath')"
            @update:model-value="path('columnPath', $event)"
        /></el-form-item>
        <el-form-item :label="t('valuePath')"
          ><el-input
            :model-value="options.valuePath?.join('.')"
            :aria-label="t('valuePath')"
            @update:model-value="path('valuePath', $event)"
        /></el-form-item>
        <el-form-item :label="t('pivotAggregate')"
          ><el-select
            :model-value="options.aggregate"
            :aria-label="t('pivotAggregate')"
            @update:model-value="patch({ aggregate: $event })"
            ><el-option
              v-for="aggregate in ['Sum', 'Count', 'Avg', 'Min', 'Max'] as const"
              :key="aggregate"
              :value="aggregate"
              :label="t(aggregate)" /></el-select
        ></el-form-item>
        <el-checkbox
          :model-value="options.totals"
          @update:model-value="patch({ totals: $event })"
          >{{ t('pivotTotals') }}</el-checkbox
        >
        <p class="advanced-help">{{ t('pivotHint') }}</p>
      </template>
    </el-form>
    <p v-if="type === 'Chart' || type === 'Sparkline'" class="advanced-help">
      {{ t('advancedArrayHint') }}
    </p>
    <p
      v-if="!['Shape', 'Line'].includes(type) && modelValue.value?.from === 'Constant'"
      class="advanced-help"
    >
      {{ t('advancedExampleHint') }}
    </p>
    <p class="advanced-help">
      {{
        t(
          ['CharacterComb', 'PivotTable'].includes(type)
            ? 'advancedDataExport'
            : 'advancedGraphicExport',
        )
      }}
    </p>
  </section>
</template>
<style scoped>
.advanced-editor {
  min-width: 0;
}
.advanced-editor h4 {
  margin: 8px 0;
}
.advanced-help {
  color: var(--el-text-color-secondary);
  font-size: 12px;
  line-height: 1.5;
  overflow-wrap: anywhere;
}
.advanced-series {
  margin: 8px 0;
  padding: 8px;
  border: 1px solid var(--el-border-color);
  display: grid;
  gap: 6px;
  min-width: 0;
}
</style>
