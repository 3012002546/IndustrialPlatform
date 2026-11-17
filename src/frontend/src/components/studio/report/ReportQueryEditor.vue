<script setup lang="ts">
import { computed } from 'vue'
import type { Json } from '@/api/studio'
import { clone } from '../shared/model'
import { useReportCopy } from './copy'
import { tableCell } from './tableEditing'
import { queryFilterColumns } from './queryFields'
const props = defineProps<{
  modelValue: Json
  datasets: Json[]
  datasetSchemas?: Json
  queryOnly?: boolean
}>()
const emit = defineEmits<{ 'update:modelValue': [value: Json] }>()
const t = useReportCopy()
const filterColumns = computed(() => queryFilterColumns(props.modelValue, props.datasetSchemas))
function selectFilterFields(ids: string[]) {
  edit((value) => {
    const previous = value.query.filters ?? []
    value.query.filters = [
      ...previous.filter((item: Json) => !item.fieldId),
      ...ids.map((id) => ({ ...filterColumns.value.find((item) => item.fieldId === id) })),
    ]
  })
}
const optionFields = (revision: string): Json[] =>
  props.datasetSchemas?.[revision]?.item?.children ?? []
const bands = computed<Json[]>(() =>
  (props.modelValue.bands ?? []).filter(
    (band: Json) => band.kind === 'Detail' && band.data?.mode !== 'ParentPath',
  ),
)
const columns = computed<Json[]>(
  () =>
    (props.modelValue.bands ?? [])
      .find((band: Json) => band.id === props.modelValue.query?.resultBandId)
      ?.controls.filter((control: Json) => control.type === 'Table')
      .flatMap((control: Json) =>
        control.table.columns.map((column: Json) => ({ ...column, controlId: control.id })),
      ) ?? [],
)
function edit(fn: (value: Json) => void) {
  const value = clone(props.modelValue)
  fn(value)
  emit('update:modelValue', value)
}
function sync(value: Json) {
  const band = value.bands.find((band: Json) => band.id === value.query.resultBandId)
  const known = new Map(
    (value.query.columns ?? []).map((column: Json) => [
      `${column.controlId}/${column.columnId}`,
      column,
    ]),
  )
  value.query.columns = (band?.controls ?? [])
    .filter((control: Json) => control.type === 'Table')
    .flatMap((control: Json) =>
      control.table.columns.map(
        (column: Json) =>
          known.get(`${control.id}/${column.id}`) ?? {
            controlId: control.id,
            columnId: column.id,
            visible: true,
            sortable: true,
          },
      ),
    )
  if (band?.data?.instanceId) value.query.instanceId = band.data.instanceId
}
function mode(mode: string) {
  edit((value) => {
    value.mode = mode
    if (mode === 'Fixed') delete value.query
    else {
      value.query ??= {
        instanceId: value.instances?.[0]?.instanceId ?? '',
        resultBandId: bands.value[0]?.id ?? '',
        filters: [],
        columns: [],
        defaultSort: [],
        defaultPageSize: 20,
      }
      sync(value)
    }
  })
}
function sourceColumn(doc: Json, entry: Json) {
  const control = doc.bands
    .find((band: Json) => band.id === doc.query.resultBandId)
    ?.controls.find((control: Json) => control.id === entry.controlId)
  return {
    control,
    column: control?.table.columns.find((column: Json) => column.id === entry.columnId),
  }
}
function setInstance(id: string) {
  edit((value) => {
    value.query.instanceId = id
    if (props.queryOnly)
      value.bands.find((band: Json) => band.id === value.query.resultBandId).data.instanceId = id
  })
}
function moveColumn(index: number, delta: number) {
  edit((value) => {
    const target = index + delta
    if (target < 0 || target >= value.query.columns.length) return
    const [entry] = value.query.columns.splice(index, 1)
    value.query.columns.splice(target, 0, entry)
    const band = value.bands.find((item: Json) => item.id === value.query.resultBandId)
    for (const control of band.controls.filter((item: Json) => item.type === 'Table')) {
      const order = value.query.columns
        .filter((item: Json) => item.controlId === control.id)
        .map((item: Json) => item.columnId)
      control.table.columns.sort((a: Json, b: Json) => order.indexOf(a.id) - order.indexOf(b.id))
    }
  })
}
function setVisible(entry: Json, visible: boolean) {
  edit((value) => {
    value.query.columns.find(
      (item: Json) => item.controlId === entry.controlId && item.columnId === entry.columnId,
    ).visible = visible
    if (!visible)
      value.query.defaultSort = value.query.defaultSort.filter(
        (item: Json) => item.fieldId !== entry.columnId,
      )
  })
}
</script>
<template>
  <el-form label-position="top"
    ><el-form-item v-if="!queryOnly" :label="t('mode')"
      ><el-select :model-value="modelValue.mode" @update:model-value="mode"
        ><el-option value="Fixed" :label="t('fixedReport')" /><el-option
          value="Query"
          :label="t('queryReport')" /></el-select
    ></el-form-item>
    <template v-if="modelValue.mode === 'Query' && modelValue.query">
      <el-form-item v-if="!queryOnly" :label="t('result')"
        ><el-select
          :model-value="modelValue.query.resultBandId"
          @update:model-value="
            (id: string) =>
              edit((value) => {
                value.query.resultBandId = id
                sync(value)
              })
          "
          ><el-option
            v-for="band in bands"
            :key="band.id"
            :value="band.id"
            :label="band.name" /></el-select
      ></el-form-item>
      <el-form-item v-if="!queryOnly" :label="t('instance')"
        ><el-select :model-value="modelValue.query.instanceId" @update:model-value="setInstance"
          ><el-option
            v-for="instance in modelValue.instances"
            :key="instance.instanceId"
            :value="instance.instanceId"
            :label="instance.instanceId" /></el-select
      ></el-form-item>
      <el-button v-if="!queryOnly" @click="edit(sync)">{{ t('syncColumns') }}</el-button>
      <div v-if="queryOnly" class="query-setting-column query-column-head">
        <span>{{ t('sourceField') }}</span
        ><span>{{ t('columnTitle') }}</span
        ><span>{{ t('visible') }}</span
        ><span>{{ t('order') }}</span>
      </div>
      <div
        v-for="(column, index) in modelValue.query.columns"
        :key="`${column.controlId}/${column.columnId}`"
        :class="{ 'query-setting-column': queryOnly }"
      >
        <span v-if="queryOnly">{{
          (
            tableCell(sourceColumn(modelValue, column).control.table, 0, column.columnId)?.value
              ?.path ?? []
          ).join('.') || sourceColumn(modelValue, column).column?.label
        }}</span>
        <el-input
          v-if="queryOnly"
          :model-value="column.label ?? sourceColumn(modelValue, column).column?.label"
          :aria-label="t('columnTitle')"
          @update:model-value="
            (label: string) =>
              edit((value) => {
                sourceColumn(value, column).column.label = label
                value.query.columns[index].label = label
                const filter = value.query.filters.find(
                  (item: Json) => item.fieldId === column.columnId,
                )
                if (filter) filter.label = label
              })
          "
        />
        <span v-else>{{ sourceColumn(modelValue, column).column?.label ?? column.columnId }}</span>
        <el-checkbox
          :model-value="column.visible !== false"
          @update:model-value="
            (checked: boolean | string | number) => setVisible(column, !!checked)
          "
          >{{ t('visible') }}</el-checkbox
        >
        <span v-if="queryOnly">
          <el-button
            :aria-label="t('moveFieldUp')"
            :disabled="index === 0"
            @click="moveColumn(Number(index), -1)"
            >↑</el-button
          >
          <el-button
            :aria-label="t('moveFieldDown')"
            :disabled="index === modelValue.query.columns.length - 1"
            @click="moveColumn(Number(index), 1)"
            >↓</el-button
          >
        </span>
        <el-checkbox
          v-else
          :model-value="column.sortable === true"
          @update:model-value="
            (checked: boolean | string | number) =>
              edit((value) => {
                value.query.columns[index].sortable = !!checked
                if (!checked)
                  value.query.defaultSort = value.query.defaultSort.filter(
                    (sort: Json) => sort.fieldId !== column.columnId,
                  )
              })
          "
          >{{ t('sortable') }}</el-checkbox
        >
      </div>
      <h4>{{ t('filter') }}</h4>
      <p>{{ t('fieldFiltersHelp') }}</p>
      <el-select
        multiple
        filterable
        clearable
        :aria-label="t('filterFields')"
        :placeholder="t('filterFields')"
        :model-value="
          modelValue.query.filters
            .filter((item: Json) => item.fieldId)
            .map((item: Json) => item.fieldId)
        "
        @update:model-value="selectFilterFields"
        ><el-option
          v-for="field in filterColumns"
          :key="field.fieldId"
          :value="field.fieldId"
          :label="field.label"
      /></el-select>
      <template v-for="(filter, index) in modelValue.query.filters" :key="index">
        <div v-if="!filter.fieldId">
          <el-select
            :model-value="filter.parameterName"
            @update:model-value="
              (name: string) => edit((value) => (value.query.filters[index].parameterName = name))
            "
            ><el-option
              v-for="parameter in modelValue.parameters"
              :key="parameter.name"
              :value="parameter.name"
              :label="parameter.label || parameter.name" /></el-select
          ><el-input
            :model-value="filter.label"
            :aria-label="t('name')"
            @update:model-value="
              (label: string) => edit((value) => (value.query.filters[index].label = label))
            "
          /><el-select
            :model-value="filter.control"
            @update:model-value="
              (control: string) => edit((value) => (value.query.filters[index].control = control))
            "
            ><el-option
              v-for="control in [
                'Text',
                'Number',
                'Select',
                'Date',
                'DateRange',
                'BusinessSelector',
              ]"
              :key="control"
              :value="control"
              :label="control"
          /></el-select>
          <template v-if="['Select', 'BusinessSelector'].includes(String(filter.control))"
            ><el-select
              :model-value="filter.optionsDatasetRevisionNId"
              :aria-label="t('source')"
              @update:model-value="
                (revision: string) =>
                  edit((value) => (value.query.filters[index].optionsDatasetRevisionNId = revision))
              "
              ><el-option
                v-for="dataset in datasets.filter((item) => item.publishedRevisionNId)"
                :key="dataset.nId"
                :value="dataset.publishedRevisionNId"
                :label="dataset.name" /></el-select
            ><el-select
              :model-value="filter.valueFieldId"
              :placeholder="t('valueField')"
              @update:model-value="
                (id: string) => edit((value) => (value.query.filters[index].valueFieldId = id))
              "
              ><el-option
                v-for="field in optionFields(String(filter.optionsDatasetRevisionNId))"
                :key="field.id || field.name"
                :value="field.id || field.name"
                :label="field.label || field.name" /></el-select
            ><el-select
              :model-value="filter.labelFieldId"
              :placeholder="t('labelField')"
              @update:model-value="
                (id: string) => edit((value) => (value.query.filters[index].labelFieldId = id))
              "
              ><el-option
                v-for="field in optionFields(String(filter.optionsDatasetRevisionNId))"
                :key="field.id || field.name"
                :value="field.id || field.name"
                :label="field.label || field.name" /></el-select
          ></template>
          <el-button link @click="edit((value) => value.query.filters.splice(index, 1))">{{
            t('remove')
          }}</el-button>
        </div>
      </template>
      <el-button
        v-if="!queryOnly"
        @click="
          edit((value) =>
            value.query.filters.push({ parameterName: '', label: '', control: 'Text' }),
          )
        "
        >{{ t('add') }}</el-button
      >
      <template v-if="!queryOnly">
        <h4>{{ t('order') }}</h4>
        <div v-for="(sort, index) in modelValue.query.defaultSort" :key="index">
          <el-select
            :model-value="sort.fieldId"
            @update:model-value="
              (id: string) => edit((value) => (value.query.defaultSort[index].fieldId = id))
            "
            ><el-option
              v-for="column in modelValue.query.columns.filter(
                (item: Json) => item.sortable && item.visible !== false,
              )"
              :key="column.columnId"
              :value="column.columnId"
              :label="
                columns.find((item) => item.id === column.columnId)?.label ?? column.columnId
              " /></el-select
          ><el-select
            :model-value="sort.direction"
            @update:model-value="
              (direction: string) =>
                edit((value) => (value.query.defaultSort[index].direction = direction))
            "
            ><el-option value="Asc" label="↑" /><el-option value="Desc" label="↓" /></el-select
          ><el-button link @click="edit((value) => value.query.defaultSort.splice(index, 1))">{{
            t('remove')
          }}</el-button>
        </div>
        <el-button
          @click="
            edit((value) =>
              value.query.defaultSort.push({ fieldId: '', direction: 'Asc', nulls: 'Last' }),
            )
          "
          >{{ t('add') }}</el-button
        >
      </template>
    </template>
  </el-form>
</template>
<style scoped>
.query-setting-column {
  display: grid;
  grid-template-columns: minmax(160px, 1fr) minmax(200px, 1fr) 90px 120px;
  gap: 12px;
  align-items: start;
  padding: 8px 0;
  border-bottom: 1px solid var(--el-border-color-lighter);
}
.query-setting-column > span {
  line-height: 32px;
  overflow-wrap: anywhere;
}
.query-column-head {
  font-weight: 600;
}
@media (max-width: 900px) {
  .query-setting-column {
    grid-template-columns: minmax(110px, 1fr) minmax(130px, 1fr) 75px 100px;
  }
}
</style>
