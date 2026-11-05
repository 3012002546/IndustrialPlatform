<script setup lang="ts">
import { computed, onBeforeUnmount, ref, watch } from 'vue'
import AppDataTable from '@/components/management/AppDataTable.vue'
import AppQueryPanel from '@/components/management/AppQueryPanel.vue'
import GraphWorkbench from '../graph/GraphWorkbench.vue'
import { getTraceApi, type TraceResult, type TraceStartRow } from '@/api/studioTrace'
import type { TraceDirection, TraceDocument } from './model'
import { projectTraceResult } from './adapter'
import { tracePreviewResult } from './preview'
import TraceSearchForm from './TraceSearchForm.vue'
import { useTraceCopy } from './copy'
const props = defineProps<{
  document: TraceDocument
  resourceNId: string
  relationId?: string | undefined
}>()
const api = getTraceApi(),
  t = useTraceCopy(),
  parameters = ref<Record<string, unknown>>({}),
  rows = ref<TraceStartRow[]>([]),
  sample = ref<TraceStartRow>(),
  result = ref<TraceResult>(),
  diagnostics = ref<unknown[]>([]),
  error = ref(''),
  busy = ref(false),
  direction = ref<TraceDirection>('Downstream'),
  resolvedInput = ref<unknown>(),
  sourceRows = ref<unknown[]>([])
let generation = 0
let controller: AbortController | undefined
const contextKey = crypto.randomUUID()
const graph = computed(() =>
  result.value
    ? projectTraceResult(result.value, {
        contextKey,
        definition: {
          kind: 'draft',
          resourceNId: props.resourceNId,
          documentStamp: String(generation),
        },
      })
    : null,
)
const columns = computed(() =>
  props.document.start.columns
    .filter((c) => c.visible)
    .map((c, index) => ({
      field: `traceColumn${index}`,
      title: c.label,
      width: c.width,
      filter: false as const,
    })),
)
const displayRows = computed(() =>
  rows.value.map((row) => ({
    ...row,
    ...Object.fromEntries(
      props.document.start.columns
        .filter((c) => c.visible)
        .map((c, index) => [`traceColumn${index}`, row.values[c.fieldId]]),
    ),
  })),
)
function cancel() {
  generation++
  controller?.abort()
  busy.value = false
}
watch(
  () => props.document,
  () => {
    cancel()
    rows.value = []
    sample.value = undefined
    result.value = undefined
  },
)
onBeforeUnmount(cancel)
async function preview(relation = false) {
  if (busy.value || (relation && (!props.relationId || !sample.value))) return
  const current = ++generation
  controller = new AbortController()
  busy.value = true
  error.value = ''
  diagnostics.value = []
  result.value = undefined
  try {
    if (relation && props.relationId && sample.value) {
      const value = await api.previewRelation(
        props.document,
        props.relationId,
        direction.value,
        { objectTypeId: sample.value.objectTypeId, identity: sample.value.identity, summary: [] },
        controller.signal,
      )
      if (current !== generation) return
      diagnostics.value = value.diagnostics
      resolvedInput.value = value.resolvedInput
      sourceRows.value = value.sourceRows
      result.value = tracePreviewResult(
        value,
        sample.value,
        props.document,
        props.relationId,
        direction.value,
      )
    } else {
      const value = await api.previewStart(props.document, parameters.value, 1, controller.signal)
      if (current === generation) {
        rows.value = value.items
        diagnostics.value = value.diagnostics
        sample.value = undefined
      }
    }
  } catch (e) {
    if (current === generation) error.value = e instanceof Error ? e.message : String(e)
  } finally {
    if (current === generation) busy.value = false
  }
}
</script>
<template>
  <el-alert :title="t('previewHint')" type="info" :closable="false" /><el-alert
    v-if="error"
    :title="error"
    type="error"
    :closable="false"
  />
  <AppQueryPanel :title="t('search')" show-actions @submit="preview()" @reset="parameters = {}"
    ><TraceSearchForm
      v-model="parameters"
      :fields="document.start.searchFields"
      :load-options="(name, params, signal) => api.previewOptions(document, name, params, signal)"
  /></AppQueryPanel>
  <AppDataTable
    table-key="studio-trace-preview-starts"
    :columns="columns"
    :rows="displayRows"
    row-key="startKey"
    selection="single"
    toolbar-profile="compact"
    :loading="busy"
    :selected-row-key="sample?.startKey ?? null"
    @row-click="(row: TraceStartRow) => (sample = row)"
    @selection-change="(selected: TraceStartRow[]) => (sample = selected[0])"
  />
  <template v-if="relationId"
    ><p>{{ t('sampleHint') }}</p>
    <el-select v-model="direction" :aria-label="t('direction')"
      ><el-option value="Downstream" :label="t('forward')" /><el-option
        value="Upstream"
        :label="t('reverse')" /></el-select
    ><el-button :disabled="!sample || busy" @click="preview(true)">{{
      t('previewRelation')
    }}</el-button></template
  ><el-button v-if="busy" @click="cancel">{{ t('cancel') }}</el-button>
  <GraphWorkbench v-if="graph" :view="graph" mode="preview" read-only class="trace-preview-graph" />
  <details v-if="resolvedInput">
    <summary>{{ t('mapping') }}</summary>
    <pre>{{ JSON.stringify(resolvedInput, null, 2) }}</pre>
  </details>
  <details v-if="sourceRows.length">
    <summary>{{ t('result') }}</summary>
    <pre>{{ JSON.stringify(sourceRows, null, 2) }}</pre>
  </details>
  <ul v-if="diagnostics.length">
    <li v-for="(diagnostic, i) in diagnostics" :key="i">{{ diagnostic }}</li>
  </ul>
</template>
<style scoped>
.trace-preview-graph {
  height: 420px;
}
pre {
  white-space: pre-wrap;
  overflow-wrap: anywhere;
}
</style>
