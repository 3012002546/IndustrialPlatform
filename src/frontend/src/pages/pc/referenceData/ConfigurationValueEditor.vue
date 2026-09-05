<script setup lang="ts">
import { computed } from 'vue'
import type { ConfigurationDataType } from '@/api/referenceData/parameterTypes'
import type { DictionaryItem } from '@/api/referenceData/types'
import { localeMessages } from '@/localization/i18n'
import { useLocalizationStore } from '@/stores/localizationStore'

const props = withDefaults(
  defineProps<{
    modelValue: string | null
    dataType: ConfigurationDataType
    label: string
    disabled?: boolean
    allowUnset?: boolean
    enumItems?: DictionaryItem[]
  }>(),
  { disabled: false, allowUnset: true, enumItems: () => [] },
)
const emit = defineEmits<{ 'update:modelValue': [value: string | null] }>()
const localization = useLocalizationStore()
const copy = computed(() => localeMessages[localization.locale].referenceData)
const isString = computed(() =>
  ['String', 'Date', 'DateTime', 'Enum', 'Reference'].includes(props.dataType),
)
const text = computed({
  get() {
    if (props.modelValue == null) return ''
    if (!isString.value) return props.modelValue
    try {
      return String(JSON.parse(props.modelValue))
    } catch {
      return props.modelValue
    }
  },
  set(value: string) {
    emit('update:modelValue', isString.value ? JSON.stringify(value) : value)
  },
})
function setUnset(unset: boolean) {
  emit(
    'update:modelValue',
    unset ? null : props.dataType === 'Boolean' ? 'false' : isString.value ? '""' : '',
  )
}
</script>

<template>
  <div class="configuration-value-editor">
    <el-checkbox
      v-if="allowUnset"
      :model-value="modelValue === null"
      :disabled="disabled"
      @update:model-value="setUnset(Boolean($event))"
      >{{ copy.notConfigured }}</el-checkbox
    >
    <el-select
      v-if="dataType === 'Boolean'"
      v-model="text"
      :aria-label="label"
      :disabled="disabled || (allowUnset && modelValue === null)"
    >
      <el-option value="true" :label="copy.trueValue" /><el-option
        value="false"
        :label="copy.falseValue"
      />
    </el-select>
    <el-select
      v-else-if="dataType === 'Enum' && enumItems.length"
      v-model="text"
      :aria-label="label"
      filterable
      :disabled="disabled || (allowUnset && modelValue === null)"
    >
      <el-option
        v-for="item in enumItems"
        :key="item.nId"
        :value="item.nId"
        :label="`${item.name} (${item.nId})`"
      />
    </el-select>
    <el-input
      v-else
      v-model="text"
      :aria-label="label"
      :disabled="disabled || (allowUnset && modelValue === null)"
      :type="dataType === 'Json' ? 'textarea' : dataType === 'Date' ? 'date' : 'text'"
      :rows="5"
      :placeholder="dataType === 'DateTime' ? '2026-09-05T08:00:00+08:00' : undefined"
    />
    <small v-if="dataType === 'Decimal' || dataType === 'Integer'">{{ copy.decimalHint }}</small>
  </div>
</template>

<style scoped>
.configuration-value-editor {
  display: grid;
  gap: 6px;
  width: 100%;
}
.configuration-value-editor small {
  color: var(--el-text-color-secondary);
}
</style>
