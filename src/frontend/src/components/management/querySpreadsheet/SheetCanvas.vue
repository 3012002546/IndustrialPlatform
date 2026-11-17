<script setup lang="ts">
import { onBeforeUnmount, onMounted, shallowRef, useTemplateRef, watch } from 'vue'
import {
  CommandType,
  LocaleType,
  mergeLocales,
  type ICellData,
  type IWorkbookData,
} from '@univerjs/core'
import { createUniver } from '@univerjs/presets'
import { IRibbonService, UniverSheetsCorePreset } from '@univerjs/preset-sheets-core'
import { UniverSheetsFilterPreset } from '@univerjs/preset-sheets-filter'
import { UniverSheetsSortPreset } from '@univerjs/preset-sheets-sort'
import coreZh from '@univerjs/preset-sheets-core/locales/zh-CN'
import coreEn from '@univerjs/preset-sheets-core/locales/en-US'
import filterZh from '@univerjs/preset-sheets-filter/locales/zh-CN'
import filterEn from '@univerjs/preset-sheets-filter/locales/en-US'
import sortZh from '@univerjs/preset-sheets-sort/locales/zh-CN'
import sortEn from '@univerjs/preset-sheets-sort/locales/en-US'
import '@univerjs/preset-sheets-core/lib/index.css'
import '@univerjs/preset-sheets-filter/lib/index.css'
import '@univerjs/preset-sheets-sort/lib/index.css'
import { useLocalizationStore } from '@/stores/localizationStore'
import { useThemeStore } from '@/stores/themeStore'
import { createSheet } from './model'
import { SheetError } from './copy'
import type { QueryColumn, QueryRow, SheetCapture, FilterColumn } from './types'
const props = defineProps<{ columns: QueryColumn[]; title: string }>()
const emit = defineEmits<{
  ready: []
  summary: [data: { columns: QueryColumn[]; visible: number }]
  change: []
  chart: [kind: 'Bar' | 'Line' | 'Pie']
  view: [chart: boolean]
}>()
const chartView = shallowRef(false)
const chartTarget = shallowRef<HTMLElement>()
let ribbonSubscription: { unsubscribe(): void } | undefined
let layoutObserver: MutationObserver | undefined
const host = useTemplateRef<HTMLDivElement>('host')
const locale = useLocalizationStore(),
  theme = useThemeStore()
let owner: ReturnType<typeof createUniver> | undefined
let count = 0,
  generation = 0
let subscription: { dispose(): void } | undefined
let disposed = false
let editing = false
let synchronizing = false
let filterRefresh: ReturnType<typeof setTimeout> | undefined
async function replace(rows: QueryRow[]) {
  const snapshot = createSheet(props.columns, rows, props.title)
  generation++
  dispose()
  owner = createUniver({
    locale: locale.locale === 'en-US' ? LocaleType.EN_US : LocaleType.ZH_CN,
    locales: {
      [LocaleType.ZH_CN]: mergeLocales(coreZh, filterZh, sortZh, {
        queryTable: { title: '表格', undo: '撤销', redo: '重做' },
        queryCharts: { title: '图表', Bar: '柱状图', Line: '折线图', Pie: '饼图' },
      }),
      [LocaleType.EN_US]: mergeLocales(coreEn, filterEn, sortEn, {
        queryTable: { title: 'Table', undo: 'Undo', redo: 'Redo' },
        queryCharts: { title: 'Charts', Bar: 'Bar chart', Line: 'Line chart', Pie: 'Pie chart' },
      }),
    },
    presets: [
      UniverSheetsCorePreset({
        container: host.value!,
        header: true,
        toolbar: true,
        ribbonType: 'classic',
        footer: false,
        contextMenu: false,
        formulaBar: true,
        disableAutoFocus: true,
      }),
      UniverSheetsFilterPreset(),
      UniverSheetsSortPreset(),
    ],
  })
  owner.univerAPI.toggleDarkMode(theme.effectiveColorMode === 'dark')
  for (const action of ['undo', 'redo'] as const)
    owner.univerAPI
      .createMenu({
        id: `query.table.${action}`,
        title: `queryTable.${action}`,
        action: () => owner?.univerAPI[action](),
      })
      .appendTo(['ribbon', 'queryTable.title', 'queryTable.actions'])
  for (const kind of ['Bar', 'Line', 'Pie'] as const)
    owner.univerAPI
      .createMenu({
        id: `query.chart.${kind}`,
        title: `queryCharts.${kind}`,
        action: () => emit('chart', kind),
      })
      .appendTo(['ribbon', 'queryCharts.title', 'queryCharts.actions'])
  owner.univerAPI.updateMenuConfig({
    'queryTable.title': { order: 5 },
    'queryCharts.title': { order: 6 },
  })
  ribbonSubscription = owner.univer
    .__getInjector()
    .get(IRibbonService)
    .activatedTab$.subscribe((tab) => {
      chartView.value = tab === 'queryCharts.title'
      emit('view', chartView.value)
    })
  // Keep the workbook mounted beneath the chart so selection, edits and filters survive tab switches.
  layoutObserver = new MutationObserver(() => {
    const content = host.value?.querySelector<HTMLElement>(
      '[data-u-comp="workbench-layout"] > section',
    )
    if (content) {
      chartTarget.value = content
      layoutObserver?.disconnect()
    }
  })
  layoutObserver.observe(host.value!, { childList: true, subtree: true })
  const book = owner.univerAPI.createWorkbook(snapshot)
  owner.univer.__getInjector().get(IRibbonService).setActivatedTab('queryTable.title')
  count = rows.length
  if (props.columns.length)
    book
      .getActiveSheet()
      .getRange(0, 0, Math.max(31, count + 31), props.columns.length + 10)
      .createFilter()
  editing = false
  await book.getActiveSheet().getWorksheetPermission().protect()
  await setEditing(false)
  subscription = owner.univerAPI.addEvent(owner.univerAPI.Event.CommandExecuted, (event) => {
    if (
      !synchronizing &&
      (event.id === 'sheet.mutation.set-range-values' ||
        (event.type === CommandType.MUTATION && event.id.includes('filter')))
    ) {
      clearTimeout(filterRefresh)
      filterRefresh = setTimeout(() => {
        void capture().catch(() => {
          /* Output reports invalid formulas. */
        })
      }, 200)
    }
    if (
      !synchronizing &&
      event.type === CommandType.MUTATION &&
      event.id.startsWith('sheet') &&
      !event.id.includes('permission')
    )
      emit('change')
    if (
      event.type === CommandType.COMMAND &&
      event.id.startsWith('doc.command.') &&
      !event.id.includes('refresh')
    )
      emit('change')
  })
  emit('summary', { columns: props.columns, visible: rows.length })
}
async function capture(): Promise<SheetCapture> {
  const current = owner,
    version = generation
  if (!current || disposed) throw new DOMException('Disposed', 'AbortError')
  const book = current.univerAPI.getActiveWorkbook()!
  await book.endEditingAsync(true)
  await current.univerAPI.getFormula().onCalculationResultApplied(15000)
  if (disposed || owner !== current || version !== generation)
    throw new DOMException('Replaced', 'AbortError')
  const snapshot = book.save() as IWorkbookData
  const sheet = snapshot.sheets.result
  if (
    !sheet ||
    snapshot.sheetOrder.length !== 1 ||
    (sheet.columnCount ?? 0) > 1100 ||
    (sheet.rowCount ?? 0) > 11000
  )
    throw new SheetError('unsupported')
  let rowCount = count,
    width = props.columns.length
  for (const [r, cells] of Object.entries(sheet.cellData ?? {}))
    for (const [c, cell] of Object.entries(cells as Record<string, ICellData>))
      if (
        cell &&
        (cell.f || (cell.v != null && cell.v !== '') || cell.p?.body?.dataStream.trim())
      ) {
        rowCount = Math.max(rowCount, Number(r))
        width = Math.max(width, Number(c) + 1)
      }
  if (rowCount > 10000 || width > 1000 || rowCount * width > 500000) throw new SheetError('limit')
  const columns = Array.from({ length: width }, (_, c) => ({
    ...props.columns[c],
    field: props.columns[c]?.field ?? 'temporary-' + c,
    title: String(
      sheet.cellData?.[0]?.[c]?.p?.body?.dataStream.trim() ||
        sheet.cellData?.[0]?.[c]?.v ||
        props.columns[c]?.title ||
        'Column ' + (c + 1),
    ),
  }))
  const filter = book.getActiveSheet().getFilter()
  const filters: FilterColumn[] = []
  for (let c = 0; c < width; c++) {
    const criteria = filter?.getColumnFilterCriteria(c)
    if (criteria) filters.push(criteria as FilterColumn)
  }
  synchronizing = true
  try {
    for (const criteria of filters)
      filter?.setColumnFilterCriteria(
        criteria.colId,
        criteria as Parameters<NonNullable<typeof filter>['setColumnFilterCriteria']>[1],
      )
  } finally {
    synchronizing = false
  }
  const data: SheetCapture = structuredClone({
    snapshot,
    columns,
    rowCount,
    hidden: [
      ...new Set([
        ...(filter ? filter.getFilteredOutRows() : []),
        ...Array.from({ length: Math.max(0, rowCount - count) }, (_, i) => count + i + 1).filter(
          (r) =>
            !Object.values(sheet.cellData?.[r] ?? {}).some(
              (cell) =>
                cell &&
                (cell.f || (cell.v != null && cell.v !== '') || cell.p?.body?.dataStream.trim()),
            ),
        ),
      ]),
    ],
    filters,
  })
  const hidden = new Set(data.hidden)
  emit('summary', {
    columns,
    visible: Array.from({ length: rowCount }, (_, i) => i + 1).filter(
      (r) => !hidden.has(r) && !sheet.rowData?.[r]?.hd,
    ).length,
  })
  return data
}
async function setEditing(value: boolean) {
  const book = owner?.univerAPI.getActiveWorkbook()
  if (!book) return
  await book.endEditingAsync(true)
  const permission = book.getActiveSheet().getWorksheetPermission()
  synchronizing = true
  try {
    if (value) {
      await permission.unprotect()
      editing = true
      return
    }
    if (!permission.isProtected()) await permission.protect()
    await permission.setMode('filterOnly')
    for (const point of ['Copy', 'SelectProtectedCells', 'SelectUnProtectedCells'] as const)
      await permission.setPoint(owner!.univerAPI.Enum.WorksheetPermissionPoint[point], true)
  } finally {
    synchronizing = false
  }
  editing = value
}
async function grow(kind: 'rows' | 'columns') {
  if (!editing) return
  const data = await capture(),
    sheet = owner!.univerAPI.getActiveWorkbook()!.getActiveSheet()
  const rows = data.snapshot.sheets.result!.rowCount!,
    columns = data.snapshot.sheets.result!.columnCount!
  if ((kind === 'rows' && rows + 30 > 11000) || (kind === 'columns' && columns + 10 > 1100))
    throw new SheetError('limit')
  if (kind === 'rows') sheet.insertRowsAfter(rows - 1, 30)
  else sheet.insertColumnsAfter(columns - 1, 10)
  sheet.getFilter()?.remove()
  const filter = sheet
    .getRange(0, 0, rows + (kind === 'rows' ? 30 : 0), columns + (kind === 'columns' ? 10 : 0))
    .createFilter()
  for (const criteria of data.filters)
    filter?.setColumnFilterCriteria(
      criteria.colId,
      criteria as Parameters<NonNullable<typeof filter>['setColumnFilterCriteria']>[1],
    )
}
async function sort(column: number, ascending: boolean) {
  const current = owner,
    version = generation
  const data = await capture()
  if (!current || data.rowCount < 2) return
  await current.univerAPI.getActiveWorkbook()!.endEditingAsync(true)
  if (disposed || owner !== current || version !== generation) return
  current.univerAPI
    .getActiveWorkbook()!
    .getActiveSheet()
    .getRange(1, 0, data.rowCount, data.columns.length)
    .sort({ column, ascending })
}
function dispose() {
  clearTimeout(filterRefresh)
  layoutObserver?.disconnect()
  ribbonSubscription?.unsubscribe()
  chartTarget.value = undefined
  chartView.value = false
  subscription?.dispose()
  subscription = undefined
  owner?.univerAPI.dispose()
  owner?.univer.dispose()
  owner = undefined
}
watch(
  () => locale.locale,
  (value) => owner?.univerAPI.setLocale(value === 'en-US' ? LocaleType.EN_US : LocaleType.ZH_CN),
)
watch(
  () => theme.effectiveColorMode,
  (value) => owner?.univerAPI.toggleDarkMode(value === 'dark'),
)
onMounted(() => {
  emit('ready')
})
onBeforeUnmount(() => {
  disposed = true
  generation++
  dispose()
})
defineExpose({
  replace,
  setEditing,
  grow,
  capture,
  sort,
  undo: () => owner?.univerAPI.undo(),
  redo: () => owner?.univerAPI.redo(),
})
</script>
<template>
  <div ref="host" class="query-sheet-canvas" :class="{ 'chart-view': chartView }" />
  <Teleport v-if="chartTarget" :to="chartTarget">
    <div v-show="chartView" class="query-chart-view" data-query-chart @keydown.stop>
      <slot name="chart" />
    </div>
  </Teleport>
</template>
<style scoped>
.query-sheet-canvas {
  width: 100%;
  height: clamp(420px, 62vh, 900px);
  min-width: 0;
  border: 1px solid var(--el-border-color);
}
.chart-view :deep([data-u-comp='workbench-layout'] > section > div:not([data-query-chart])) {
  visibility: hidden;
}
.query-chart-view {
  position: absolute;
  inset: 0;
  z-index: 5;
  overflow: auto;
  padding: 16px;
  background: var(--el-bg-color, white);
  color: var(--el-text-color-primary);
}
</style>
