<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref } from 'vue'
import AppFormDrawer from '@/components/management/AppFormDrawer.vue'
import AppQueryPanel from '@/components/management/AppQueryPanel.vue'
import AppDataTable from '@/components/management/AppDataTable.vue'
import type { AppDataTableRequest } from '@/components/management/AppDataTable'
import { getTraceApi, type TraceContract, type TraceStartRow } from '@/api/studioTrace'
import { ApiError } from '@/api/errors'
import TraceSearchForm from './TraceSearchForm.vue'
import { useTraceCopy } from './copy'

const props = defineProps<{ resourceNId: string; contract: TraceContract }>()
const emit = defineEmits<{
  close: []
  confirm: [rows: TraceStartRow[]]
  denied: [message: string]
}>()
const api = getTraceApi(),
  t = useTraceCopy()
function initialParameters() {
  return Object.fromEntries(
    props.contract.parameters
      .filter((p) => p.source !== 'TrustedContext')
      .map((p) => [
        p.name,
        !p.required &&
        props.contract.start.searchFields.some(
          (field) => field.parameterName === p.name && field.control === 'Text',
        )
          ? ''
          : p.defaultValue,
      ]),
  )
}
const parameters = ref<Record<string, unknown>>(initialParameters())
const table = ref<{
  setTopQuery: (query: Record<string, unknown>) => void
  clearSelection: () => void
}>()
const selected = ref<TraceStartRow[]>([]),
  currentRows = ref<TraceStartRow[]>([]),
  busy = ref(false)
let controller: AbortController | undefined
const columns = computed(() =>
  props.contract.start.columns
    .filter((c) => c.visible)
    .map((c, i) => ({
      field: `traceColumn${i}`,
      title: c.label,
      width: c.width,
      filter: false as const,
      sortable: false,
    })),
)
async function load(request: AppDataTableRequest) {
  controller?.abort()
  const active = new AbortController()
  controller = active
  selected.value = []
  currentRows.value = []
  table.value?.clearSelection()
  busy.value = true
  try {
    const value = await api.starts(
      props.resourceNId,
      {
        revisionNId: props.contract.revisionNId,
        parameters: { ...parameters.value },
        page: request.pageIndex,
        pageSize: request.pageSize,
      },
      active.signal,
    )
    if (active.signal.aborted) throw new DOMException('Aborted', 'AbortError')
    currentRows.value = value.items
    return {
      items: value.items.map((row) => ({
        ...row,
        ...Object.fromEntries(
          props.contract.start.columns
            .filter((c) => c.visible)
            .map((c, i) => [`traceColumn${i}`, row.values[c.fieldId]]),
        ),
      })),
      total: Number(value.totalCount),
      pageIndex: request.pageIndex,
      pageSize: request.pageSize,
    }
  } catch (error) {
    if (
      !active.signal.aborted &&
      error instanceof ApiError &&
      (['unauthorized', 'forbidden', 'notFound'].includes(error.kind) ||
        error.details.code === 'TRACE_CREDENTIAL_EXPIRED')
    )
      emit('denied', error.message)
    throw error
  } finally {
    if (controller === active) busy.value = false
  }
}
function search() {
  table.value?.setTopQuery({})
}
function reset() {
  parameters.value = initialParameters()
  search()
}
function choose(rows: TraceStartRow[]) {
  selected.value = currentRows.value.filter((row) =>
    rows.some((item) => item.startKey === row.startKey),
  )
}
function confirm() {
  if (!busy.value && selected.value.length) emit('confirm', selected.value)
}
onBeforeUnmount(() => controller?.abort())
onMounted(search)
</script>
<template>
  <AppFormDrawer
    :model-value="true"
    :title="t('graphList')"
    mode="modal"
    size="extra-wide"
    storage-key="industrial-platform:trace-start-picker-mode"
    @update:model-value="emit('close')"
  >
    <div class="trace-start-picker">
      <AppQueryPanel show-actions grid @submit="search" @reset="reset">
        <TraceSearchForm
          v-model="parameters"
          :fields="contract.start.searchFields"
          :load-options="
            (parameterName, params, signal) =>
              api.options(
                resourceNId,
                { revisionNId: contract.revisionNId, parameterName, parameters: params },
                signal,
              )
          "
        />
      </AppQueryPanel>
      <AppDataTable
        ref="table"
        table-key="studio-trace-start-picker"
        :columns="columns"
        :loader="load"
        row-key="startKey"
        selection="multiple"
        :page-size="20"
        toolbar-profile="compact"
        :quick-search-enabled="false"
        @selection-change="choose"
      />
    </div>
    <template #footer>
      <el-button @click="emit('close')">{{ t('cancel') }}</el-button>
      <el-button type="primary" :disabled="busy || !selected.length" @click="confirm">{{
        t('confirm')
      }}</el-button>
    </template>
  </AppFormDrawer>
</template>
<style scoped>
.trace-start-picker {
  display: grid;
  gap: 16px;
  min-height: 380px;
  align-content: start;
}
.trace-start-picker :deep(.trace-search-fields) {
  flex: 1;
}
</style>
