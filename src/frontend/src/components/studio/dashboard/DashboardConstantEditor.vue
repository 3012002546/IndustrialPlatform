<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import DashboardStructuredEditor from './DashboardStructuredEditor.vue'
import { useDashboardText } from './dashboardCopy'
import type { Json } from '@/api/studio'
const props = defineProps<{ modelValue?: unknown; jsonOnly?: boolean }>()
const emit = defineEmits<{ 'update:modelValue': [value: unknown] }>()
const t = useDashboardText()
const json = ref(false),
  text = ref(''),
  error = ref('')
let baseline = ''
const kind = computed(() =>
  props.modelValue === null
    ? 'null'
    : Array.isArray(props.modelValue)
      ? 'array'
      : typeof props.modelValue === 'object'
        ? 'object'
        : typeof props.modelValue === 'number'
          ? 'number'
          : typeof props.modelValue === 'boolean'
            ? 'boolean'
            : 'string',
)
watch(
  () => props.modelValue,
  (value) => {
    const next = JSON.stringify(value ?? null, null, 2)
    if (baseline === next) return
    baseline = next
    text.value = next
    error.value = ''
  },
  { immediate: true, deep: true },
)
function changeKind(value: unknown) {
  emit(
    'update:modelValue',
    value === 'array'
      ? []
      : value === 'object'
        ? {}
        : value === 'number'
          ? 0
          : value === 'boolean'
            ? false
            : value === 'null'
              ? null
              : '',
  )
}
function apply(): boolean {
  if (!props.jsonOnly && !json.value) return true
  try {
    const value = JSON.parse(text.value)
    if (!props.jsonOnly || JSON.stringify(value) !== JSON.stringify(props.modelValue ?? null))
      emit('update:modelValue', value)
    baseline = JSON.stringify(value, null, 2)
    error.value = ''
    return true
  } catch {
    error.value = t.value('constantJsonInvalid')
    return false
  }
}
function format() {
  try {
    text.value = JSON.stringify(JSON.parse(text.value), null, 2)
    error.value = ''
  } catch {
    error.value = t.value('constantJsonInvalid')
  }
}
function reset() {
  text.value = JSON.stringify(props.modelValue ?? null, null, 2)
  error.value = ''
}
defineExpose({ apply })
</script>
<template>
  <div class="dashboard-constant-editor">
    <el-switch
      v-if="!jsonOnly"
      v-model="json"
      :active-text="t('jsonMode')"
      :inactive-text="t('visualMode')"
    />
    <template v-if="jsonOnly || json">
      <div class="json-actions">
        <el-button size="small" @click="format">{{ t('formatJson') }}</el-button>
        <el-button size="small" @click="apply">{{
          t(jsonOnly ? 'applyJson' : 'confirm')
        }}</el-button>
        <el-button size="small" @click="reset">{{ t('resetJson') }}</el-button>
      </div>
      <el-input
        v-model="text"
        class="json-text"
        type="textarea"
        :rows="jsonOnly ? 16 : 8"
        :aria-label="t('constantValue')"
        spellcheck="false"
      />
      <p v-if="error" role="alert">{{ error }}</p>
    </template>
    <template v-else>
      <el-select
        :model-value="kind"
        :aria-label="t('constantValueType')"
        @update:model-value="changeKind"
      >
        <el-option
          v-for="item in [
            { value: 'array', label: t('dataShapeArray') },
            { value: 'object', label: t('dataShapeObject') },
            { value: 'string', label: t.optionLabel('String') },
            { value: 'number', label: t.optionLabel('Decimal') },
            { value: 'boolean', label: t.optionLabel('Boolean') },
            { value: 'null', label: 'null' },
          ]"
          :key="item.value"
          :value="item.value"
          :label="item.label"
        />
      </el-select>
      <DashboardStructuredEditor
        v-if="kind === 'array' || kind === 'object'"
        kind="rows"
        :model-value="kind === 'array' ? modelValue : [modelValue]"
        @update:model-value="
          emit('update:modelValue', kind === 'array' ? $event : (($event[0] as Json) ?? {}))
        "
      />
      <el-input-number
        v-else-if="kind === 'number'"
        :model-value="Number(modelValue)"
        @update:model-value="emit('update:modelValue', $event ?? 0)"
      />
      <el-switch
        v-else-if="kind === 'boolean'"
        :model-value="Boolean(modelValue)"
        @update:model-value="emit('update:modelValue', $event)"
      />
      <el-input
        v-else-if="kind === 'string'"
        :model-value="String(modelValue ?? '')"
        :aria-label="t('constantValue')"
        :placeholder="t('constantValue')"
        @update:model-value="emit('update:modelValue', $event)"
      />
    </template>
  </div>
</template>
<style scoped>
.dashboard-constant-editor {
  display: grid;
  gap: 8px;
  width: 100%;
}
.json-actions {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
}
.json-actions .el-button {
  margin: 0;
}
.json-text :deep(textarea) {
  font-family: Consolas, monospace;
  line-height: 1.5;
  tab-size: 2;
}
</style>
