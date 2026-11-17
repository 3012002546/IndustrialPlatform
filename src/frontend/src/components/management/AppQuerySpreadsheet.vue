<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, ref, shallowRef, useTemplateRef } from 'vue'
import { ElMessageBox } from 'element-plus'
import SheetCanvas from './querySpreadsheet/SheetCanvas.vue'
import QueryChartPanel from './querySpreadsheet/QueryChartPanel.vue'
import { readRows, validatePage, visibleCapture } from './querySpreadsheet/model'
import { SheetError, useSheetCopy } from './querySpreadsheet/copy'
import type {
  QueryColumn,
  QueryRow,
  QuerySpreadsheetProps,
  SheetCapture,
} from './querySpreadsheet/types'
const props = defineProps<QuerySpreadsheetProps>()
const t = useSheetCopy()
const canvas = useTemplateRef<InstanceType<typeof SheetCanvas>>('canvas')
const frame = useTemplateRef<HTMLIFrameElement>('frame')
const loading = ref(false),
  outputBusy = ref(false),
  dirty = ref(false),
  error = ref(''),
  preview = ref(false)
const pageIndex = ref(1),
  pageSize = ref(10000),
  total = ref(0),
  loaded = ref(0),
  editing = ref(false)
const chartKind = ref<'Bar' | 'Line' | 'Pie'>('Bar'),
  chartReady = ref(false),
  chartData = shallowRef<QueryRow[]>([]),
  sortColumn = ref(0),
  ascending = ref(true)
const chartColumns = shallowRef<QueryColumn[]>([])
const currentColumns = shallowRef<QueryColumn[]>(props.columns)
const visibleRows = ref(0)
function sheetSummary(data: { columns: QueryColumn[]; visible: number }) {
  currentColumns.value = data.columns
  visibleRows.value = data.visible
  if (sortColumn.value >= data.columns.length) sortColumn.value = 0
}
const printData = shallowRef<SheetCapture>()
const title = computed(() => props.title || t.value('result'))
let generation = 0,
  alive = true,
  controller: AbortController | undefined
const urls = new Map<string, ReturnType<typeof setTimeout>>()
function cancel() {
  controller?.abort()
  generation++
  loading.value = false
  outputBusy.value = false
}
function showError(reason: unknown) {
  error.value =
    reason instanceof SheetError
      ? t.value(reason.key)
      : reason instanceof Error
        ? reason.message
        : t.value('failed')
}
async function confirmDiscard() {
  if (!dirty.value) return true
  try {
    await ElMessageBox.confirm(t.value('discard'), t.value('changed'), {
      confirmButtonText: t.value('confirm'),
      cancelButtonText: t.value('stay'),
      type: 'warning',
    })
    return true
  } catch {
    return false
  }
}
async function reload(nextPage = 1, nextSize = pageSize.value) {
  if (!canvas.value || !(await confirmDiscard()) || !alive) return
  cancel()
  const version = generation
  controller = new AbortController()
  const signal = AbortSignal.any([controller.signal, AbortSignal.timeout(30000)])
  loading.value = true
  error.value = ''
  preview.value = false
  try {
    const result = await props.loader({ pageIndex: nextPage, pageSize: nextSize, signal })
    if (!alive || signal.aborted || version !== generation) return
    validatePage(result, props.columns, nextPage, nextSize)
    if (result.items.length !== result.total) throw new SheetError('invalid')
    await canvas.value.replace(result.items)
    result.accepted?.()
    editing.value = false
    pageIndex.value = nextPage
    pageSize.value = nextSize
    total.value = result.total
    loaded.value = result.items.length
    chartReady.value = false
    chartData.value = []
    dirty.value = false
  } catch (reason) {
    if (alive && version === generation && !controller.signal.aborted) showError(reason)
  } finally {
    if (alive && version === generation) loading.value = false
  }
}
async function captureAction(
  action: (data: SheetCapture, version: number, signal: AbortSignal) => Promise<void>,
) {
  if (!canvas.value || outputBusy.value || loading.value) return
  const version = generation
  controller = new AbortController()
  const signal = AbortSignal.any([controller.signal, AbortSignal.timeout(30000)])
  outputBusy.value = true
  error.value = ''
  try {
    const data = await canvas.value.capture()
    if (alive && version === generation) await action(data, version, signal)
  } catch (reason) {
    if (
      alive &&
      version === generation &&
      !(reason instanceof DOMException && reason.name === 'AbortError')
    )
      showError(reason)
  } finally {
    if (alive && version === generation) outputBusy.value = false
  }
}
async function perform(action: () => unknown) {
  if (loading.value || outputBusy.value) return
  outputBusy.value = true
  error.value = ''
  try {
    await action()
  } catch (reason) {
    if (alive) showError(reason)
  } finally {
    if (alive) outputBusy.value = false
  }
}
async function outputAllowed(signal: AbortSignal) {
  if (!props.canExport) throw new SheetError('permission')
  await props.authorizeOutput?.(signal)
  if (!props.canExport) throw new SheetError('permission')
}
function download() {
  return captureAction(async (data, version, signal) => {
    const { exportXlsx } = await import('./querySpreadsheet/output')
    const bytes = await exportXlsx(visibleCapture(data), true)
    await outputAllowed(signal)
    if (!alive || version !== generation) return
    const url = URL.createObjectURL(
      new Blob([bytes.slice().buffer], {
        type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
      }),
    )
    const anchor = document.createElement('a')
    anchor.href = url
    anchor.download = title.value.replace(/[\\/:*?"<>|]/g, '_') + '.xlsx'
    anchor.click()
    urls.set(
      url,
      setTimeout(() => {
        URL.revokeObjectURL(url)
        urls.delete(url)
      }, 1000),
    )
  })
}
function showPrint() {
  return captureAction(async (data, version, signal) => {
    await outputAllowed(signal)
    if (!alive || version !== generation) return
    printData.value = data
    preview.value = true
  })
}
async function renderPreview() {
  const data = printData.value,
    version = generation
  const { renderPrint } = await import('./querySpreadsheet/output')
  await nextTick()
  if (data && frame.value?.contentDocument && alive && version === generation && preview.value) {
    try {
      renderPrint(frame.value.contentDocument, data, title.value)
    } catch (reason) {
      preview.value = false
      showError(reason)
    }
  }
}
async function print() {
  const version = generation
  try {
    await outputAllowed(AbortSignal.timeout(30000))
    if (alive && version === generation) frame.value?.contentWindow?.print()
  } catch (reason) {
    preview.value = false
    showError(reason)
  }
}
function updateChart(kind?: 'Bar' | 'Line' | 'Pie') {
  return captureAction(async (data) => {
    if (kind) chartKind.value = kind
    chartData.value = readRows(data)
    chartColumns.value = data.columns
    chartReady.value = true
  })
}
function beforeUnload(event: BeforeUnloadEvent) {
  if (dirty.value) {
    event.preventDefault()
    event.returnValue = ''
  }
}
window.addEventListener('beforeunload', beforeUnload)
onBeforeUnmount(() => {
  alive = false
  cancel()
  window.removeEventListener('beforeunload', beforeUnload)
  for (const [url, timer] of urls) {
    clearTimeout(timer)
    URL.revokeObjectURL(url)
  }
  urls.clear()
})
async function exportSnapshot() {
  if (!canvas.value || loading.value || outputBusy.value) throw new SheetError('loading')
  const version = generation
  const data = await canvas.value.capture()
  if (!alive || version !== generation) throw new DOMException('Replaced', 'AbortError')
  await outputAllowed(AbortSignal.timeout(30000))
  if (!alive || version !== generation) throw new DOMException('Replaced', 'AbortError')
  const rows = readRows(data)
  return {
    columns: data.columns.map((c) => c.title),
    rows: rows.map((row) => data.columns.map((c) => row[c.field])),
  }
}
async function toggleEditing() {
  await perform(async () => {
    await canvas.value?.setEditing(!editing.value)
    editing.value = !editing.value
  })
}
defineExpose({ reload, confirmDiscard, exportSnapshot })
</script>
<template>
  <section class="app-query-spreadsheet" :aria-label="title">
    <el-alert v-if="error" :title="error" type="error" :closable="false" />
    <div class="query-sheet-tools">
      <el-button :disabled="outputBusy" :loading="loading" @click="reload()">{{
        t('refresh')
      }}</el-button>
      <el-button v-if="loading" @click="cancel">{{ t('cancel') }}</el-button>
      <el-select v-model="sortColumn" :aria-label="t('sortColumn')" style="width: 150px"
        ><el-option
          v-for="(column, index) in currentColumns"
          :key="column.field"
          :label="column.title"
          :value="index"
      /></el-select>
      <el-select v-model="ascending" :aria-label="t('sortOrder')" style="width: 90px"
        ><el-option :label="t('asc')" :value="true" /><el-option :label="t('desc')" :value="false"
      /></el-select>
      <el-button
        :disabled="loading || outputBusy || !loaded"
        @click="perform(() => canvas?.sort(sortColumn, ascending))"
        >{{ t('sort') }}</el-button
      >
      <el-button
        :disabled="loading || outputBusy"
        :type="editing ? 'primary' : 'default'"
        @click="toggleEditing"
        >{{ t(editing ? 'finishEdit' : 'startEdit') }}</el-button
      >
      <el-button
        v-if="editing"
        :disabled="loading || outputBusy"
        @click="perform(() => canvas?.grow('columns'))"
        >{{ t('addColumns') }}</el-button
      >
      <el-button
        v-if="editing"
        :disabled="loading || outputBusy"
        @click="perform(() => canvas?.grow('rows'))"
        >{{ t('addRows') }}</el-button
      >
      <template v-if="canExport">
        <el-button :disabled="loading || outputBusy" @click="download">{{ t('export') }}</el-button>
        <el-button :disabled="loading || outputBusy" @click="showPrint">{{ t('print') }}</el-button>
      </template>
    </div>
    <p class="query-sheet-summary" role="status">
      {{ t('loaded') }}: {{ loaded }} / {{ t('total') }}: {{ total }} · {{ t('visibleRows') }}:
      {{ visibleRows }}
      <span v-if="dirty"> · {{ t('changed') }}</span>
      <el-tooltip :content="t('scope')"
        ><el-button link size="small">{{ t('scopeHelp') }}</el-button></el-tooltip
      >
    </p>
    <el-empty v-if="!columns.length" :description="t('noColumns')" />
    <div v-else v-loading="loading || outputBusy" class="query-sheet-workspace">
      <div :inert="loading || outputBusy" class="query-sheet-interaction">
        <SheetCanvas
          ref="canvas"
          :columns="columns"
          :title="title"
          @ready="reload()"
          @change="dirty = true"
          @summary="sheetSummary"
          @chart="updateChart($event)"
          @view="$event && updateChart()"
        >
          <template #chart>
            <QueryChartPanel
              v-if="chartReady"
              :columns="chartColumns"
              :rows="chartData"
              :kind="chartKind"
              @refresh="updateChart()"
            />
          </template>
        </SheetCanvas>
      </div>
    </div>
    <el-dialog
      v-model="preview"
      :title="t('preview')"
      width="92%"
      destroy-on-close
      append-to-body
      @opened="renderPreview"
      @closed="printData = undefined"
    >
      <iframe ref="frame" :title="t('preview')" class="query-print-frame" />
      <template #footer
        ><el-button @click="preview = false">{{ t('close') }}</el-button
        ><el-button type="primary" @click="print">{{ t('print') }}</el-button></template
      >
    </el-dialog>
  </section>
</template>
<style scoped>
.app-query-spreadsheet {
  display: flex;
  flex-direction: column;
  gap: 10px;
  min-width: 0;
}
.query-sheet-tools {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: 8px;
}
.query-sheet-tools :deep(.el-button + .el-button) {
  margin-left: 0;
}
.query-sheet-hint,
.query-sheet-summary {
  margin: 0;
  font-size: 12px;
  color: var(--el-text-color-secondary);
}
.query-sheet-summary span {
  color: var(--el-color-warning);
}
.query-print-frame {
  width: 100%;
  height: 68vh;
  border: 0;
  background: white;
}
</style>
