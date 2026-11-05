<script setup lang="ts">
import { onBeforeUnmount, ref, watch } from 'vue'
import type { TraceSearchField } from './model'
import { useTraceCopy } from './copy'
const model = defineModel<Record<string, unknown>>({ required: true })
const props = defineProps<{
  fields: TraceSearchField[]
  loadOptions: (
    name: string,
    parameters: Record<string, unknown>,
    signal: AbortSignal,
  ) => Promise<{ items: { value: string; label: string }[] }>
}>()
const t = useTraceCopy(),
  options = ref<Record<string, { value: string; label: string }[]>>({}),
  errors = ref<Record<string, string>>({}),
  loading = ref<Record<string, boolean>>({})
const requests = new Map<string, AbortController>()
const dateValue = (name: string) => model.value[name] as [string, string] | undefined
const selectedValue = (name: string) => model.value[name] as string | undefined
let generation = 0
function clear() {
  generation++
  requests.forEach((r) => r.abort())
  requests.clear()
  options.value = {}
  errors.value = {}
  loading.value = {}
}
watch(() => [props.fields, JSON.stringify(model.value)], clear)
onBeforeUnmount(clear)
async function load(name: string) {
  requests.get(name)?.abort()
  const controller = new AbortController(),
    current = generation
  requests.set(name, controller)
  loading.value[name] = true
  errors.value[name] = ''
  try {
    const result = await props.loadOptions(name, { ...model.value }, controller.signal)
    if (current === generation && !controller.signal.aborted) options.value[name] = result.items
  } catch (e) {
    if (current === generation && !controller.signal.aborted)
      errors.value[name] = e instanceof Error ? e.message : String(e)
  } finally {
    if (current === generation && !controller.signal.aborted) loading.value[name] = false
  }
}
</script>
<template>
  <div class="trace-search-fields">
    <label v-for="field in fields" :key="field.parameterName"
      >{{ field.label }}{{ field.required ? ' *' : '' }}
      <el-date-picker
        v-if="field.control === 'DateRange'"
        :model-value="dateValue(field.parameterName)"
        type="daterange"
        value-format="YYYY-MM-DD"
        :aria-label="field.label"
        @update:model-value="(v: unknown) => (model = { ...model, [field.parameterName]: v })"
      />
      <el-select
        v-else-if="field.control === 'Select' || field.control === 'BusinessSelector'"
        :model-value="selectedValue(field.parameterName)"
        filterable
        clearable
        :loading="loading[field.parameterName]"
        :aria-label="field.label"
        @visible-change="
          (open: boolean) => {
            if (open) void load(field.parameterName)
          }
        "
        @update:model-value="(v: unknown) => (model = { ...model, [field.parameterName]: v })"
        ><el-option
          v-for="option in options[field.parameterName] ?? []"
          :key="option.value"
          :value="option.value"
          :label="option.label"
      /></el-select>
      <el-input
        v-else
        :model-value="String(model[field.parameterName] ?? '')"
        :aria-label="field.label"
        :placeholder="field.label"
        @update:model-value="(v: unknown) => (model = { ...model, [field.parameterName]: v })"
      />
      <small v-if="errors[field.parameterName]" role="alert"
        >{{ errors[field.parameterName] }}
        <el-button link @click="load(field.parameterName)">{{ t('retry') }}</el-button></small
      >
    </label>
  </div>
</template>
<style scoped>
.trace-search-fields {
  display: flex;
  flex-wrap: wrap;
  gap: 10px;
}
.trace-search-fields label {
  display: grid;
  gap: 4px;
  min-width: 180px;
  max-width: 340px;
  flex: 1;
}
</style>
