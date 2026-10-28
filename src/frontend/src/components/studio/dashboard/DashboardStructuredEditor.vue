<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import type { Json } from '@/api/studio'
import { clone } from '@/components/studio/shared/model'
import { useLocalizationStore } from '@/stores/localizationStore'
import { DASHBOARD_MEDIA_ASSETS } from './mediaAssets'
import { useDashboardText } from './dashboardCopy'
import {
  newStructuredItem,
  structuredError,
  structuredFields,
  type StructuredField,
} from './structuredFields'

const props = defineProps<{
  modelValue?: unknown
  kind: string
  componentType?: string
  columns?: Json[]
  components?: { id: string; name: string }[]
  fieldOptions?: string[]
}>()
const emit = defineEmits<{ 'update:modelValue': [value: unknown[]] }>()
const t = useDashboardText(),
  locale = useLocalizationStore()
const mode = ref('visual'),
  jsonText = ref(''),
  error = ref(''),
  expanded = ref<number[]>([0])
const newField = ref('')
const selectedItem = ref(0)
const items = computed<unknown[]>(() => (Array.isArray(props.modelValue) ? props.modelValue : []))
const visibleItems = computed(() =>
  selectedItem.value < items.value.length
    ? [{ item: items.value[selectedItem.value], index: selectedItem.value }]
    : [],
)
const fields = computed(() =>
  props.kind === 'rows'
    ? [
        ...new Set([
          ...(props.columns ?? []).map((column) => String(column.field ?? column.key ?? '')),
          ...items.value.flatMap((item) => Object.keys((item ?? {}) as object)),
        ]),
      ]
        .filter(Boolean)
        .map((key) => ({ key, type: 'scalar' }) as StructuredField)
    : structuredFields(props.kind, props.componentType ?? ''),
)
watch(
  () => props.modelValue,
  (value) => {
    jsonText.value = JSON.stringify(value ?? [], null, 2)
    error.value = ''
    const length = Array.isArray(value) ? value.length : 0
    selectedItem.value = Math.min(selectedItem.value, Math.max(0, length - 1))
    expanded.value = expanded.value.filter((index) => index < length)
    if (!expanded.value.length && length) expanded.value = [selectedItem.value]
  },
  { immediate: true, deep: true },
)
function read(item: unknown, key: string): unknown {
  if (!key) return item
  if (props.kind === 'rows')
    return item && typeof item === 'object' ? (item as Json)[key] : undefined
  return key
    .split('.')
    .reduce<unknown>(
      (value, part) => (value && typeof value === 'object' ? (value as Json)[part] : undefined),
      item,
    )
}
function write(index: number, key: string, value: unknown) {
  const next = clone(items.value)
  if (!key) next[index] = value
  else if (props.kind === 'rows') next[index] = { ...(next[index] as Json), [key]: value }
  else {
    const path = key.split('.')
    let target = next[index] as Json
    for (const part of path.slice(0, -1)) {
      if (!target[part] || typeof target[part] !== 'object') target[part] = {}
      target = target[part]
    }
    const last = path.at(-1)!
    if (value === undefined) delete target[last]
    else target[last] = value
  }
  commit(next)
}
function commit(next: unknown[]): boolean {
  error.value = structuredError(props.kind, next)
  if (error.value) return false
  emit('update:modelValue', next)
  return true
}
function applyJson(): boolean {
  try {
    const next = JSON.parse(jsonText.value)
    error.value = structuredError(props.kind, next)
    if (error.value) return false
    if (JSON.stringify(next) !== JSON.stringify(items.value)) emit('update:modelValue', next)
    return true
  } catch {
    error.value = 'structuredArrayInvalid'
    return false
  }
}
function changeMode(value: unknown) {
  if (value !== 'visual' && value !== 'json') return
  if (mode.value === 'json' && value === 'visual' && !applyJson()) return
  mode.value = value
}
function add() {
  const index = items.value.length
  const item =
    props.kind === 'rows'
      ? Object.fromEntries(
          fields.value.map((field) => {
            const original = read(items.value[0], field.key)
            return [
              field.key,
              typeof original === 'number'
                ? 0
                : typeof original === 'boolean'
                  ? false
                  : original === null
                    ? null
                    : original && typeof original === 'object'
                      ? {}
                      : '',
            ]
          }),
        )
      : newStructuredItem(props.kind, index, props.componentType ?? '', props.columns)
  if (props.kind === 'series')
    (item as Json).name = `${t.value.propertyLabel('series')} ${index + 1}`
  if (props.kind === 'items') (item as Json).label = `${t.value('item')} ${index + 1}`
  const uniqueKey =
    props.kind === 'columns'
      ? 'field'
      : props.kind === 'parameterMap'
        ? 'target'
        : props.kind === 'cases'
          ? 'value'
          : ''
  if (uniqueKey && typeof (item as Json)[uniqueKey] === 'string') {
    const prefix = String((item as Json)[uniqueKey]).replace(/\d+$/, '')
    let ordinal = index + 1
    while (items.value.some((existing) => read(existing, uniqueKey) === prefix + ordinal)) ordinal++
    ;(item as Json)[uniqueKey] = prefix + ordinal
    if (props.kind === 'columns') (item as Json).label = (item as Json).field
  }
  if (commit([...clone(items.value), item])) selectItem(index)
}
function addRowField() {
  const key = newField.value.trim()
  if (!key || fields.value.some((field) => field.key === key)) return
  const rows = items.value.length ? items.value : [{}]
  if (commit(rows.map((item) => ({ ...(item as Json), [key]: '' })))) newField.value = ''
}
function remove(index: number) {
  const next = clone(items.value)
  next.splice(index, 1)
  if (commit(next)) selectItem(Math.max(0, index - 1))
}
function move(index: number, delta: number) {
  const next = clone(items.value),
    target = index + delta
  if (target < 0 || target >= next.length) return
  ;[next[index], next[target]] = [next[target], next[index]]
  if (commit(next)) selectItem(target)
}
function selectItem(index: number) {
  selectedItem.value = index
  expanded.value = [index]
}
function text(item: unknown, key: string): string {
  const value = read(item, key)
  return value == null ? '' : typeof value === 'object' ? JSON.stringify(value) : String(value)
}
function localized(item: unknown, key: string): string {
  const value = read(item, key)
  return value && typeof value === 'object'
    ? String((value as Json)[locale.locale] ?? (value as Json)['zh-CN'] ?? '')
    : String(value ?? '')
}
function setLocalized(index: number, key: string, value: string) {
  const current = read(items.value[index], key)
  write(
    index,
    key,
    current && typeof current === 'object' ? { ...current, [locale.locale]: value } : value,
  )
}
function scalarType(item: unknown, key: string): string {
  const value = read(item, key)
  return value === null
    ? 'null'
    : typeof value === 'number'
      ? 'number'
      : typeof value === 'boolean'
        ? 'boolean'
        : typeof value === 'object'
          ? 'json'
          : 'string'
}
function setType(index: number, key: string, type: string) {
  write(
    index,
    key,
    type === 'null'
      ? null
      : type === 'number'
        ? 0
        : type === 'boolean'
          ? false
          : type === 'json'
            ? {}
            : '',
  )
}
function setScalar(index: number, key: string, value: string) {
  if (scalarType(items.value[index], key) === 'json') {
    try {
      write(index, key, JSON.parse(value))
      error.value = ''
    } catch {
      error.value = 'structuredCellInvalid'
    }
  } else write(index, key, value)
}
function visible(field: StructuredField, item: unknown): boolean {
  return (
    !field.when ||
    (field.when === 'path'
      ? read(item, 'source.from') !== 'Constant'
      : read(item, 'source.from') === field.when)
  )
}
function title(item: unknown, index: number): string {
  if (props.kind === 'rows')
    return `${index + 1} · ${text(item, String(props.columns?.[0]?.field ?? Object.keys((item ?? {}) as object)[0] ?? '')) || t.value('item')}`
  return `${index + 1} · ${localized(item, 'label') || text(item, 'name') || text(item, 'field') || text(item, 'target') || text(item, 'id') || (typeof item === 'string' ? item : t.value('item'))}`
}
function fieldLabel(field: StructuredField): string {
  if (props.kind === 'columns' && field.key === 'label') return t.value('columnTitle')
  if (props.kind === 'series' && field.key === 'name') return t.value('seriesName')
  if (props.kind === 'cases' && field.key === 'value') return t.value('stateMatchValue')
  if (field.key === 'color')
    return t.value(
      props.kind === 'series'
        ? 'seriesColor'
        : props.kind === 'cases'
          ? 'stateColor'
          : 'rangeColor',
    )
  if (props.kind === 'points') return field.key === '0' ? 'X' : 'Y'
  if (!field.key) return t.value.propertyLabel('assetId')
  if (props.kind === 'rows') {
    const column = props.columns?.find((column) => (column.field ?? column.key) === field.key)
    if (column?.label) return localized(column, 'label')
  }
  return t.value.propertyLabel(field.key)
}
</script>

<template>
  <div class="dashboard-structured-editor" :data-kind="kind">
    <el-radio-group
      :model-value="mode"
      size="small"
      :aria-label="t('structuredMode')"
      @update:model-value="changeMode"
    >
      <el-radio-button value="visual">{{ t('visualMode') }}</el-radio-button
      ><el-radio-button value="json">{{ t('jsonMode') }}</el-radio-button>
    </el-radio-group>
    <template v-if="mode === 'visual'">
      <div v-if="kind === 'rows'" class="structured-heading">
        <el-input
          v-model="newField"
          :placeholder="t('newDataField')"
          :aria-label="t('newDataField')"
        />
        <el-button size="small" @click="addRowField">{{ t('addDataField') }}</el-button>
      </div>
      <div class="structured-heading">
        <small>{{ t('structuredCount', { count: items.length }) }}</small
        ><el-button size="small" @click="add">{{ t('addItem') }}</el-button>
      </div>
      <p v-if="kind === 'columns'" class="structured-hint">{{ t('columnWidthHint') }}</p>
      <el-select
        v-if="items.length"
        class="structured-selection"
        :model-value="selectedItem"
        :aria-label="t('currentConfigItem')"
        filterable
        @update:model-value="(value: number) => selectItem(value)"
      >
        <el-option
          v-for="(item, index) in items"
          :key="index"
          :value="index"
          :label="title(item, index)"
        />
      </el-select>
      <el-collapse v-model="expanded">
        <el-collapse-item
          v-for="{ item, index } in visibleItems"
          :key="index"
          :name="index"
          :title="title(item, index)"
        >
          <div class="structured-actions">
            <el-button link size="small" :disabled="index === 0" @click="move(index, -1)">{{
              t('moveUp')
            }}</el-button>
            <el-button
              link
              size="small"
              :disabled="index === items.length - 1"
              @click="move(index, 1)"
              >{{ t('moveDown') }}</el-button
            >
            <el-button link type="danger" size="small" @click="remove(index)">{{
              t('remove')
            }}</el-button>
          </div>
          <template v-for="field in fields" :key="field.key">
            <el-form-item v-if="visible(field, item)" :label="fieldLabel(field)">
              <el-input-number
                v-if="field.type === 'number'"
                :model-value="
                  typeof read(item, field.key) === 'number'
                    ? (read(item, field.key) as number)
                    : undefined
                "
                :min="field.min"
                :max="field.max"
                :aria-label="fieldLabel(field)"
                controls-position="right"
                @update:model-value="(value: number | undefined) => write(index, field.key, value)"
              />
              <el-select
                v-else-if="field.options"
                :model-value="text(item, field.key)"
                :aria-label="fieldLabel(field)"
                @update:model-value="(value: unknown) => write(index, field.key, value)"
              >
                <el-option
                  v-for="option in field.options"
                  :key="option"
                  :value="option"
                  :label="t.optionLabel(option)"
                />
                <el-option
                  v-if="text(item, field.key) && !field.options.includes(text(item, field.key))"
                  :value="text(item, field.key)"
                  :label="text(item, field.key)"
                />
              </el-select>
              <div v-else-if="field.type === 'color'" class="structured-color">
                <el-color-picker
                  :model-value="text(item, field.key) || undefined"
                  show-alpha
                  @change="(value: string | null) => write(index, field.key, value ?? undefined)"
                />
                <el-input
                  :model-value="text(item, field.key)"
                  :aria-label="fieldLabel(field)"
                  @update:model-value="
                    (value: string) => write(index, field.key, value || undefined)
                  "
                />
              </div>
              <el-select
                v-else-if="field.type === 'children'"
                multiple
                filterable
                :model-value="(read(item, field.key) as string[]) ?? []"
                :aria-label="fieldLabel(field)"
                @update:model-value="(value: unknown) => write(index, field.key, value)"
              >
                <el-option
                  v-for="component in components ?? []"
                  :key="component.id"
                  :value="component.id"
                  :label="component.name || component.id"
                />
              </el-select>
              <el-select
                v-else-if="field.type === 'asset'"
                filterable
                allow-create
                default-first-option
                :model-value="text(item, field.key)"
                :aria-label="fieldLabel(field)"
                @update:model-value="(value: unknown) => write(index, field.key, value)"
              >
                <el-option
                  v-for="(_, key) in DASHBOARD_MEDIA_ASSETS"
                  :key="key"
                  :value="key"
                  :label="key"
                />
              </el-select>
              <el-input
                v-else-if="field.type === 'localized'"
                :model-value="localized(item, field.key)"
                :aria-label="fieldLabel(field)"
                @update:model-value="(value: string) => setLocalized(index, field.key, value)"
              />
              <el-input
                v-else-if="field.type === 'path'"
                :model-value="
                  Array.isArray(read(item, field.key))
                    ? (read(item, field.key) as unknown[]).join('.')
                    : text(item, field.key)
                "
                :aria-label="fieldLabel(field)"
                @update:model-value="
                  (value: string) => write(index, field.key, value.split('.').filter(Boolean))
                "
              />
              <div v-else-if="field.type === 'scalar'" class="structured-scalar">
                <el-select
                  :model-value="scalarType(item, field.key)"
                  :aria-label="t('valueType')"
                  @update:model-value="(value: string) => setType(index, field.key, value)"
                >
                  <el-option
                    v-for="type in ['string', 'number', 'boolean', 'null', 'json']"
                    :key="type"
                    :value="type"
                    :label="t(`valueType.${type}`)"
                  />
                </el-select>
                <el-input-number
                  v-if="scalarType(item, field.key) === 'number'"
                  :model-value="read(item, field.key) as number"
                  :aria-label="fieldLabel(field)"
                  controls-position="right"
                  @update:model-value="
                    (value: number | undefined) => write(index, field.key, value ?? null)
                  "
                />
                <el-switch
                  v-else-if="scalarType(item, field.key) === 'boolean'"
                  :model-value="read(item, field.key) as boolean"
                  :aria-label="fieldLabel(field)"
                  @update:model-value="(value: boolean) => write(index, field.key, value)"
                />
                <el-input
                  v-else-if="scalarType(item, field.key) !== 'null'"
                  :model-value="text(item, field.key)"
                  :readonly="scalarType(item, field.key) === 'json'"
                  :aria-label="fieldLabel(field)"
                  @change="(value: string) => setScalar(index, field.key, value)"
                  @update:model-value="
                    (value: string) =>
                      scalarType(item, field.key) === 'string' && write(index, field.key, value)
                  "
                />
              </div>
              <el-select
                v-else-if="field.key === 'field' || field.key.endsWith('Field')"
                :model-value="text(item, field.key)"
                :aria-label="fieldLabel(field)"
                filterable
                allow-create
                default-first-option
                @update:model-value="(value: unknown) => write(index, field.key, value)"
              >
                <el-option
                  v-for="option in fieldOptions ?? []"
                  :key="option"
                  :label="option"
                  :value="option"
                />
              </el-select>
              <el-input
                v-else
                :model-value="text(item, field.key)"
                :aria-label="fieldLabel(field)"
                @update:model-value="(value: string) => write(index, field.key, value)"
              />
            </el-form-item>
          </template>
        </el-collapse-item>
      </el-collapse>
      <p v-if="!items.length" class="structured-hint">{{ t('empty') }}</p>
    </template>
    <template v-else
      ><el-input
        v-model="jsonText"
        type="textarea"
        :rows="8"
        :aria-label="t('structuredJson')"
        @change="applyJson"
      /><el-button size="small" @click="applyJson">{{ t('applyJson') }}</el-button></template
    >
    <el-alert v-if="error" :title="t(error)" type="error" :closable="false" />
  </div>
</template>

<style scoped>
.dashboard-structured-editor {
  width: 100%;
  min-width: 0;
}
.structured-heading,
.structured-actions,
.structured-color {
  display: flex;
  align-items: center;
  gap: 6px;
}
.structured-heading {
  justify-content: space-between;
  margin: 8px 0;
}
.structured-selection {
  width: 100%;
  margin: 4px 0 8px;
}
.structured-actions {
  justify-content: flex-end;
  margin-bottom: 8px;
}
.structured-actions .el-button {
  margin: 0;
}
.structured-hint {
  font-size: 12px;
  line-height: 1.6;
  color: var(--el-text-color-secondary);
  margin: 8px 0;
}
.structured-scalar {
  display: grid;
  grid-template-columns: 84px minmax(0, 1fr);
  gap: 5px;
  width: 100%;
  min-width: 0;
}
.structured-color {
  width: 100%;
  min-width: 0;
}
.structured-color .el-input {
  flex: 1;
  min-width: 0;
}
.dashboard-structured-editor :deep(.el-input-number) {
  width: 100%;
}
.dashboard-structured-editor :deep(.el-collapse-item__content > .el-form-item) {
  margin-bottom: 12px;
}
.dashboard-structured-editor > .el-alert {
  margin-top: 8px;
}
</style>
