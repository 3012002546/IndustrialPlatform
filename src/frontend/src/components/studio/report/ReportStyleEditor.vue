<script setup lang="ts">
import type { Json } from '@/api/studio'
import { useReportCopy } from './copy'
const props = defineProps<{ modelValue: Json }>()
const emit = defineEmits<{ 'update:modelValue': [value: Json]; patch: [value: Json] }>()
const t = useReportCopy()
function update(key: string, value: unknown) {
  emit('update:modelValue', { ...props.modelValue, [key]: value })
  emit('patch', { [key]: value })
}
function nested(key: string, field: string, value: unknown) {
  emit('update:modelValue', {
    ...props.modelValue,
    [key]: { ...(props.modelValue[key] ?? {}), [field]: value },
  })
  emit('patch', { [key]: { [field]: value } })
}
</script>
<template>
  <el-form label-position="top">
    <el-form-item :label="t('font')"
      ><el-input
        :model-value="modelValue.fontFamily"
        @update:model-value="(value: string) => update('fontFamily', value)"
    /></el-form-item>
    <el-form-item :label="t('size')"
      ><el-input-number
        :model-value="modelValue.fontSizePt ?? 10"
        :min="4"
        :max="96"
        @update:model-value="(value?: number) => update('fontSizePt', value)"
    /></el-form-item>
    <el-form-item :label="t('fontWeight')"
      ><el-select
        :model-value="modelValue.fontWeight ?? 400"
        @update:model-value="(value: number) => update('fontWeight', value)"
        ><el-option :value="400" :label="t('normalWeight')" /><el-option
          :value="700"
          :label="t('boldWeight')" /></el-select
      ><el-checkbox
        :model-value="modelValue.italic === true"
        @update:model-value="(value: string | number | boolean) => update('italic', value)"
        >{{ t('italic') }}</el-checkbox
      ></el-form-item
    >
    <el-form-item :label="t('color')"
      ><el-color-picker
        :model-value="modelValue.color ?? '#000000'"
        @update:model-value="(value: string | null) => update('color', value ?? '#000000')"
    /></el-form-item>
    <el-form-item :label="t('background')"
      ><el-color-picker
        :model-value="modelValue.backgroundColor"
        @update:model-value="
          (value: string | null) => update('backgroundColor', value ?? undefined)
        "
    /></el-form-item>
    <el-form-item :label="t('alignment')"
      ><el-select
        :model-value="modelValue.textAlign ?? 'Left'"
        @update:model-value="(value: string) => update('textAlign', value)"
        ><el-option
          v-for="value in ['Left', 'Center', 'Right']"
          :key="value"
          :value="value"
          :label="value" /></el-select
      ><el-select
        :model-value="modelValue.verticalAlign ?? 'Top'"
        @update:model-value="(value: string) => update('verticalAlign', value)"
        ><el-option
          v-for="value in ['Top', 'Middle', 'Bottom']"
          :key="value"
          :value="value"
          :label="value" /></el-select
    ></el-form-item>
    <el-form-item :label="t('lineHeight')"
      ><el-input-number
        :model-value="modelValue.lineHeight ?? 1.3"
        :min="0.5"
        :max="5"
        :step="0.1"
        @update:model-value="(value?: number) => update('lineHeight', value)"
    /></el-form-item>
    <el-form-item :label="t('padding')"
      ><label v-for="side in ['top', 'right', 'bottom', 'left']" :key="side"
        >{{ side
        }}<el-input-number
          :model-value="modelValue.paddingMm?.[side] ?? 0"
          :min="0"
          :max="100"
          @update:model-value="(value?: number) => nested('paddingMm', side, value)" /></label
    ></el-form-item>
    <el-form-item :label="t('border')"
      ><el-select
        :model-value="modelValue.border?.style ?? 'None'"
        @update:model-value="(value: string) => nested('border', 'style', value)"
        ><el-option
          v-for="value in ['None', 'Solid', 'Dashed']"
          :key="value"
          :value="value"
          :label="value" /></el-select
      ><el-input-number
        :model-value="modelValue.border?.widthMm ?? 0.2"
        :min="0"
        :max="10"
        :step="0.1"
        @update:model-value="
          (value?: number) => nested('border', 'widthMm', value)
        " /><el-color-picker
        :model-value="modelValue.border?.color ?? '#000000'"
        @update:model-value="
          (value: string | null) => nested('border', 'color', value ?? '#000000')
        "
    /></el-form-item>
    <el-form-item :label="t('numberFormat')"
      ><el-input
        :model-value="modelValue.numberFormat"
        placeholder="0.000"
        @update:model-value="(value: string) => update('numberFormat', value || undefined)"
    /></el-form-item>
    <el-form-item :label="t('dateFormat')"
      ><el-input
        :model-value="modelValue.dateFormat"
        placeholder="yyyy-MM-dd"
        @update:model-value="(value: string) => update('dateFormat', value || undefined)"
    /></el-form-item>
  </el-form>
</template>
