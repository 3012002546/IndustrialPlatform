<script setup lang="ts">
import { computed } from 'vue'
import type { Json } from '@/api/studio'
import { clone } from '../shared/model'
import { useReportCopy } from './copy'
const props = defineProps<{ modelValue: Json; datasets: Json[]; datasetSchemas?: Json }>()
const emit = defineEmits<{ 'update:modelValue': [value: Json] }>()
const t = useReportCopy()
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
</script>
<template>
  <el-form label-position="top"
    ><el-form-item :label="t('mode')"
      ><el-select :model-value="modelValue.mode" @update:model-value="mode"
        ><el-option value="Fixed" :label="t('fixedReport')" /><el-option
          value="Query"
          :label="t('queryReport')" /></el-select
    ></el-form-item>
    <template v-if="modelValue.mode === 'Query' && modelValue.query">
      <el-form-item :label="t('result')"
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
      <el-form-item :label="t('instance')"
        ><el-select
          :model-value="modelValue.query.instanceId"
          @update:model-value="(id: string) => edit((value) => (value.query.instanceId = id))"
          ><el-option
            v-for="instance in modelValue.instances"
            :key="instance.instanceId"
            :value="instance.instanceId"
            :label="instance.instanceId" /></el-select
      ></el-form-item>
      <el-button @click="edit(sync)">{{ t('syncColumns') }}</el-button>
      <div
        v-for="column in modelValue.query.columns"
        :key="`${column.controlId}/${column.columnId}`"
      >
        <span>{{
          columns.find((item) => item.id === column.columnId && item.controlId === column.controlId)
            ?.label ?? column.columnId
        }}</span
        ><el-checkbox
          :model-value="column.visible !== false"
          @update:model-value="
            (checked: boolean | string | number) =>
              edit(
                (value) =>
                  (value.query.columns.find(
                    (item: Json) =>
                      item.columnId === column.columnId && item.controlId === column.controlId,
                  ).visible = checked),
              )
          "
          >{{ t('visible') }}</el-checkbox
        ><el-checkbox
          :model-value="column.sortable === true"
          @update:model-value="
            (checked: boolean | string | number) =>
              edit(
                (value) =>
                  (value.query.columns.find(
                    (item: Json) =>
                      item.columnId === column.columnId && item.controlId === column.controlId,
                  ).sortable = checked),
              )
          "
          >{{ t('sortable') }}</el-checkbox
        >
      </div>
      <h4>{{ t('filter') }}</h4>
      <div v-for="(filter, index) in modelValue.query.filters" :key="index">
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
            v-for="control in ['Text', 'Number', 'Select', 'Date', 'DateRange', 'BusinessSelector']"
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
      <el-button
        @click="
          edit((value) =>
            value.query.filters.push({ parameterName: '', label: '', control: 'Text' }),
          )
        "
        >{{ t('add') }}</el-button
      >
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
  </el-form>
</template>
