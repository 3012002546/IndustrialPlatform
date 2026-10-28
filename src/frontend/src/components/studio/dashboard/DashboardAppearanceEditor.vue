<script setup lang="ts">
import { ref, watch } from 'vue'
import type { Json } from '@/api/studio'
import { useDashboardText } from './dashboardCopy'
import { dashboardFontFamilies } from './appearance'

const props = defineProps<{ modelValue?: Json | undefined }>()
const emit = defineEmits<{ 'update:modelValue': [value: Json] }>()
const t = useDashboardText()
const mode = ref<'visual' | 'json'>('visual')
const jsonText = ref('')
const error = ref(false)
const colors = ['color', 'background', 'borderColor']
const numbers = [
  { key: 'fontSizePx', min: 8, max: 128, step: 1 },
  { key: 'borderWidth', min: 0, max: 20, step: 1 },
  { key: 'opacity', min: 0, max: 1, step: 0.05 },
  { key: 'paddingPx', min: 0, max: 48, step: 1 },
]
watch(
  () => props.modelValue,
  (value) => {
    jsonText.value = JSON.stringify(value ?? {}, null, 2)
    error.value = false
  },
  { immediate: true, deep: true },
)
function textValue(key: string): string {
  return typeof props.modelValue?.[key] === 'string' ? props.modelValue[key] : ''
}
function numberValue(key: string): number | undefined {
  return typeof props.modelValue?.[key] === 'number' ? props.modelValue[key] : undefined
}
function update(key: string, value: unknown) {
  const next = { ...(props.modelValue ?? {}) }
  if (value === '' || value == null) delete next[key]
  else next[key] = value
  emit('update:modelValue', next)
}
function applyJson(): boolean {
  try {
    const value = JSON.parse(jsonText.value)
    if (!value || typeof value !== 'object' || Array.isArray(value)) throw new Error()
    for (const field of numbers) {
      const number = value[field.key]
      if (
        number !== undefined &&
        (typeof number !== 'number' ||
          !Number.isFinite(number) ||
          number < field.min ||
          number > field.max)
      )
        throw new Error()
    }
    for (const key of [...colors, 'fontKey']) {
      if (value[key] !== undefined && typeof value[key] !== 'string') throw new Error()
    }
    error.value = false
    if (JSON.stringify(value) !== JSON.stringify(props.modelValue ?? {}))
      emit('update:modelValue', value)
    return true
  } catch {
    error.value = true
    return false
  }
}
function changeMode(value: unknown) {
  if (value !== 'visual' && value !== 'json') return
  if (mode.value === 'json' && value === 'visual' && !applyJson()) return
  mode.value = value
}
</script>

<template>
  <div class="dashboard-appearance-editor">
    <el-radio-group
      :model-value="mode"
      size="small"
      :aria-label="t('appearanceMode')"
      @update:model-value="changeMode"
    >
      <el-radio-button value="visual">{{ t('visualMode') }}</el-radio-button>
      <el-radio-button value="json">{{ t('jsonMode') }}</el-radio-button>
    </el-radio-group>
    <template v-if="mode === 'visual'">
      <el-form-item :label="t.propertyLabel('fontKey')">
        <el-select
          :model-value="textValue('fontKey')"
          :aria-label="t.propertyLabel('fontKey')"
          :placeholder="t('systemDefault')"
          clearable
          @update:model-value="(value: unknown) => update('fontKey', value)"
        >
          <el-option
            v-for="(_, key) in dashboardFontFamilies"
            :key="key"
            :value="key"
            :label="t(`font.${key}`)"
          />
          <el-option
            v-if="textValue('fontKey') && !dashboardFontFamilies[textValue('fontKey')]"
            :value="textValue('fontKey')"
            :label="textValue('fontKey')"
          />
        </el-select>
      </el-form-item>
      <div class="dashboard-appearance-numbers">
        <el-form-item v-for="field in numbers" :key="field.key" :label="t.propertyLabel(field.key)">
          <el-input-number
            :model-value="numberValue(field.key)"
            :aria-label="t.propertyLabel(field.key)"
            :placeholder="t('systemDefault')"
            :min="field.min"
            :max="field.max"
            :step="field.step"
            controls-position="right"
            @update:model-value="(value: number | undefined) => update(field.key, value)"
          />
        </el-form-item>
      </div>
      <el-form-item v-for="key in colors" :key="key" :label="t.propertyLabel(key)">
        <div class="dashboard-appearance-color">
          <el-color-picker
            :model-value="
              textValue(key) === 'transparent' ? undefined : textValue(key) || undefined
            "
            :aria-label="t.propertyLabel(key)"
            show-alpha
            color-format="hex"
            @change="(value: string | null) => update(key, value)"
          />
          <el-input
            :model-value="textValue(key) === 'transparent' ? '' : textValue(key)"
            :aria-label="t.propertyLabel(key)"
            :placeholder="textValue(key) === 'transparent' ? t('transparent') : t('systemDefault')"
            clearable
            @update:model-value="(value: string) => update(key, value)"
          />
          <el-button v-if="key === 'background'" link @click="update(key, 'transparent')">{{
            t('transparent')
          }}</el-button>
        </div>
      </el-form-item>
    </template>
    <template v-else>
      <el-input
        v-model="jsonText"
        type="textarea"
        :rows="10"
        :aria-label="t('appearanceJson')"
        @change="applyJson"
      />
      <el-button size="small" @click="applyJson">{{ t('applyJson') }}</el-button>
    </template>
    <el-alert v-if="error" :title="t('appearanceJsonInvalid')" type="error" :closable="false" />
  </div>
</template>

<style scoped>
.dashboard-appearance-editor {
  width: 100%;
  min-width: 0;
}
.dashboard-appearance-editor > .el-radio-group {
  margin-bottom: 14px;
}
.dashboard-appearance-numbers {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 0 10px;
}
.dashboard-appearance-numbers :deep(.el-input-number) {
  width: 100%;
}
.dashboard-appearance-color {
  display: flex;
  align-items: center;
  gap: 6px;
  width: 100%;
  min-width: 0;
}
.dashboard-appearance-color > .el-input {
  flex: 1;
  min-width: 0;
}
.dashboard-appearance-color > .el-button {
  margin: 0;
}
.dashboard-appearance-editor > .el-button,
.dashboard-appearance-editor > .el-alert {
  margin-top: 8px;
}
</style>
