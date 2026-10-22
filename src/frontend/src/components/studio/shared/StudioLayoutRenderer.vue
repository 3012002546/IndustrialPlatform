<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import type { Json, StudioRuntime } from '@/api/studio'
import AppDataTable from '@/components/management/AppDataTable.vue'
import type { AppDataTableColumn } from '@/components/management/AppDataTable'
import { type Binding, type LayoutNode, nodes, resolveBinding, evaluateCondition } from './model'
import ChartRenderer from './ChartRenderer.vue'
import StudioFileField from './StudioFileField.vue'
import { useStudioCopy } from './copy'
const props = defineProps<{
  node: LayoutNode
  runtime: StudioRuntime
  form: Json
  parameters: Json
  row?: Json | undefined
  preview?: boolean
  readOnly?: boolean
}>()
const emit = defineEmits<{ 'update:form': [value: Json]; row: [row: Json, instanceId: string] }>()
const t = useStudioCopy(),
  activeTab = ref('')
const instances = computed(() =>
  Object.fromEntries(
    Object.entries(props.runtime.instances ?? {}).map(([key, value]) => [key, value.data]),
  ),
)
const bindings = computed(() =>
  ((props.runtime.document.fields as Json[]) ?? []).filter((f) => f.componentId === props.node.id),
)
const bindingError = ref('')
watch(
  () => [props.runtime, props.form, props.parameters, props.row],
  () => {
    bindingError.value = ''
  },
)
function value(property: string, fallback: unknown): unknown {
  const binding = bindings.value.find((f) => f.property === property)
  if (!binding) return fallback
  try {
    return resolveBinding(binding.source as Binding, {
      Instance: instances.value,
      Form: props.form,
      PageParam: props.parameters,
      Row: props.row,
    })
  } catch (e) {
    bindingError.value = e instanceof Error ? e.message : t.value('fieldMissing')
    return undefined
  }
}
const fieldBinding = computed(() => bindings.value.find((f) => f.writeTarget))
const fieldId = computed(() =>
  String(fieldBinding.value?.writeTarget ?? props.node.props.fieldId ?? props.node.id),
)
const field = computed(() =>
  ((props.runtime.ledgerModel?.document.fields as Json[]) ?? []).find(
    (f) => f.id === fieldId.value,
  ),
)
const label = computed(
  () => field.value?.label ?? props.node.props.placeholder ?? field.value?.name ?? fieldId.value,
)
const modelValue = computed({
  get: () =>
    Object.hasOwn(props.form, fieldId.value)
      ? props.form[fieldId.value]
      : value('value', field.value?.defaultValue ?? null),
  set: (value: unknown) => emit('update:form', { ...props.form, [fieldId.value]: value }),
})
const disabled = computed(() => props.readOnly || fieldBinding.value?.readOnly === true)
const visibility = computed(() => {
  try {
    return {
      visible: bindings.value.every((binding) =>
        evaluateCondition(binding.visibleWhen, {
          Instance: instances.value,
          Form: props.form,
          PageParam: props.parameters,
          Row: props.row,
        }),
      ),
      error: '',
    }
  } catch (e) {
    return { visible: false, error: e instanceof Error ? e.message : t.value('fieldMissing') }
  }
})
const required = computed(
  () =>
    field.value?.required ||
    fieldBinding.value?.validators?.some((v: Json) => v.kind === 'Required') ||
    (fieldBinding.value?.requiredWhen &&
      evaluateCondition(fieldBinding.value.requiredWhen, {
        Instance: instances.value,
        Form: props.form,
        PageParam: props.parameters,
        Row: props.row,
      })),
)
function dataRows(instanceId: string): Json[] {
  if (props.node.props.collectionPath !== undefined) {
    const path = props.node.props.collectionPath
    const data = valueFromCollection(
      instanceId,
      Array.isArray(path) ? path : String(path).split('.').filter(Boolean),
    )
    return Array.isArray(data) ? (data as Json[]) : []
  }
  const data = instances.value[instanceId]
  if (bindings.value.some((binding) => binding.property === 'rows')) {
    const bound = value('rows', null)
    return Array.isArray(bound) ? (bound as Json[]) : []
  }
  return Array.isArray(data) ? (data as Json[]) : []
}
function valueFromCollection(instanceId: string, path: (string | number)[]) {
  try {
    return resolveBinding(
      { from: 'Instance', instanceId, path, nullPolicy: 'Error' },
      { Instance: instances.value },
    )
  } catch (error) {
    bindingError.value = error instanceof Error ? error.message : t.value('fieldMissing')
    return undefined
  }
}
const rows = computed(() =>
  dataRows(String(props.node.props.instanceId ?? props.node.props.datasetInstanceId ?? '')),
)
const dataError = computed(() => {
  if (!['Table', 'Tree', 'Chart'].includes(props.node.type)) return ''
  const id = String(props.node.props.instanceId ?? props.node.props.datasetInstanceId ?? '')
  if (!id || !(id in instances.value)) return t.value('fieldMissing') + ': ' + t.value('instanceId')
  if (
    !Array.isArray(instances.value[id]) &&
    props.node.props.collectionPath === undefined &&
    !bindings.value.some((binding) => binding.property === 'rows')
  )
    return t.value('fieldMissing') + ': ' + t.value('collection')
  return ''
})
const columns = computed(() => (props.node.props.columns as AppDataTableColumn[]) ?? [])
const allNodes = computed(() => nodes(props.runtime.document.layout as unknown as LayoutNode))
const children = computed(() => props.node.children ?? [])
const tree = computed(() => {
  const p = props.node.props
  const map = new Map<string, Json>(
    rows.value.map((row) => [String(row[String(p.keyPath)]), { ...row, children: [] as Json[] }]),
  )
  const roots: Json[] = []
  for (const row of map.values()) {
    const parent = map.get(String(row[String(p.parentPath)]))
    if (parent && parent !== row) parent.children.push(row)
    else roots.push(row)
  }
  return roots
})
function forward(value: Json) {
  emit('update:form', value)
}
</script>
<template>
  <section
    v-if="
      visibility.error ||
      (visibility.visible && (!['Container', 'Grid'].includes(node.type) || children.length))
    "
    class="studio-runtime-node"
    :style="{
      gridColumn: `span ${Math.max(1, Math.min(24, node.grid?.span ?? 24))}`,
      minHeight: node.grid.height ? `${node.grid.height}px` : undefined,
    }"
  >
    <el-alert
      v-if="bindingError || visibility.error"
      :title="bindingError || visibility.error"
      type="error"
      :closable="false"
    />
    <el-alert v-if="dataError" :title="dataError" type="error" :closable="false" />
    <div v-if="node.registryVersion !== '1'" role="alert">
      {{ t('unsupportedVersion') }}: {{ node.registryVersion }}
    </div>
    <div v-else-if="node.type === 'Text'">{{ value('text', node.props.text) }}</div>
    <div
      v-else-if="node.type === 'Container' || node.type === 'Grid'"
      class="studio-runtime-grid"
      :style="{ gap: (node.props.gapPx ?? 12) + 'px' }"
    >
      <StudioLayoutRenderer
        v-for="child in [...children].sort((a, b) => a.grid.order - b.grid.order)"
        :key="child.id"
        :node="child"
        :runtime="runtime"
        :form="form"
        :parameters="parameters"
        :row="row"
        :preview="preview"
        :read-only="readOnly"
        @update:form="forward"
        @row="(row, instanceId) => emit('row', row, instanceId)"
      />
    </div>
    <el-form-item v-else-if="node.type === 'FormField'" :label="label" :required="required">
      <StudioFileField
        v-if="field?.type === 'FileReference'"
        v-model="modelValue as Json"
        :model-n-id="runtime.ledgerModel?.nId ?? runtime.resource.nId"
        :disabled="disabled || preview"
      />
      <el-switch
        v-else-if="node.props.control === 'Checkbox' || field?.type === 'Boolean'"
        v-model="modelValue"
        :disabled="disabled"
      />
      <el-date-picker
        v-else-if="node.props.control === 'Date' || field?.type === 'Date'"
        v-model="modelValue"
        type="date"
        value-format="YYYY-MM-DD"
        :disabled="disabled"
        :aria-label="String(label)"
      />
      <el-date-picker
        v-else-if="node.props.control === 'DateTime' || field?.type === 'DateTime'"
        v-model="modelValue"
        type="datetime"
        value-format="YYYY-MM-DDTHH:mm:ssZ"
        :disabled="disabled"
        :aria-label="String(label)"
      />
      <el-select
        v-else-if="node.props.control === 'Select' || field?.type === 'Enum'"
        v-model="modelValue"
        :disabled="disabled"
        :aria-label="String(label)"
        ><el-option
          v-for="v in (field?.enumValues as unknown[]) ?? []"
          :key="String(v)"
          :label="String(v)"
          :value="v as string"
      /></el-select>
      <el-input-number
        v-else-if="field?.type === 'Int32'"
        v-model="modelValue"
        :disabled="disabled"
        :aria-label="String(label)"
      />
      <el-input
        v-else
        v-model="modelValue"
        :type="field?.type === 'Text' ? 'textarea' : 'text'"
        :disabled="disabled"
        :aria-label="String(label)"
        :placeholder="node.props.placeholder as string"
      />
    </el-form-item>
    <AppDataTable
      v-else-if="node.type === 'Table'"
      :table-key="'studio.runtime.' + runtime.resource.nId + '.' + node.id"
      :columns="columns"
      :rows="rows"
      :row-key="
        Array.isArray(node.props.rowKeyPath)
          ? node.props.rowKeyPath.join('.')
          : String(node.props.rowKeyPath ?? 'nId')
      "
      :query-mode="(node.props.queryMode as 'top' | 'header') ?? 'top'"
      :selection="(node.props.selection as 'none' | 'single' | 'multiple') ?? 'none'"
      @row-click="(row) => emit('row', row as Json, String(node.props.instanceId))"
    />
    <el-tree
      v-else-if="node.type === 'Tree'"
      :data="tree"
      :node-key="String(node.props.keyPath)"
      :props="{ label: String(node.props.labelPath), children: 'children' }"
      @node-click="(row: Json) => emit('row', row as Json, String(node.props.instanceId))"
    />
    <el-tabs v-else-if="node.type === 'Tabs'" v-model="activeTab"
      ><el-tab-pane
        v-for="item in node.props.items as Json[]"
        :key="String(item.id)"
        :name="String(item.id)"
        :label="String(item.label)"
        ><StudioLayoutRenderer
          v-if="allNodes.find((n) => n.id === item.childLayoutId) && item.childLayoutId !== node.id"
          :node="allNodes.find((n) => n.id === item.childLayoutId)!"
          :runtime="runtime"
          :form="form"
          :parameters="parameters"
          :row="row"
          :preview="preview"
          :read-only="readOnly"
          @update:form="forward"
          @row="(row, instanceId) => emit('row', row, instanceId)" /></el-tab-pane
    ></el-tabs>
    <StudioFileField
      v-else-if="node.type === 'File'"
      v-model="modelValue as Json"
      :model-n-id="runtime.ledgerModel?.nId ?? runtime.resource.nId"
      :disabled="disabled || preview"
    />
    <ChartRenderer v-else-if="node.type === 'Chart'" :config="node.props" :rows="rows" />
    <div v-else-if="node.type === 'RegisteredComponent' || node.type === 'RegisteredPage'">
      <template
        v-if="node.props.registryKey === 'studio.summary' && node.props.registryVersion === '1'"
        ><h3>{{ t('codeSummary') }}</h3>
        <p>{{ runtime.resource.name }} · v{{ runtime.revisionNId }}</p>
        <dl>
          <template v-for="(instance, id) in runtime.instances" :key="id"
            ><dt>{{ id }}</dt>
            <dd>
              {{ Array.isArray(instance.data) ? instance.data.length : typeof instance.data }}
            </dd></template
          >
        </dl></template
      ><el-alert v-else :title="t('codeMissing')" type="error" :closable="false" />
    </div>
    <el-alert v-else :title="t('unsupported') + ': ' + node.type" type="error" :closable="false" />
  </section>
</template>
