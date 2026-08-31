<script setup lang="ts" generic="T extends object">
import {
  computed,
  defineComponent,
  h,
  nextTick,
  onBeforeUnmount,
  onMounted,
  onUpdated,
  ref,
  render,
  watch,
} from 'vue'
import type { PropType } from 'vue'
import { VxeColumn, VxeTable, VxeToolbar, VxeUI } from 'vxe-table'
import type { VxeTableInstance, VxeTablePropTypes, VxeToolbarInstance } from 'vxe-table'
import { ElDatePicker } from 'element-plus'
import type { QueryDescriptor } from '@/querying'
import {
  Brush,
  Connection,
  Download,
  FullScreen,
  Operation,
  Printer,
  Refresh,
  Filter,
  Search,
  Setting,
  SortUp,
} from '@element-plus/icons-vue'

import { getCurrentSession } from '@/auth/gateway'
import { localeMessages } from '@/localization/i18n'
import { useLocalizationStore } from '@/stores/localizationStore'
import type {
  AppDataTableColumn,
  AppDataTableDensity,
  AppDataTableExportRequest,
  AppDataTableQuickExportFormat,
  AppDataTableQuickExportMode,
  AppDataTableLoader,
  AppDataTableMode,
  AppDataTableQueryMode,
  AppDataTableRequest,
  AppDataTableSort,
  AppDataTableTreeOptions,
} from './AppDataTable'
import {
  buildAppDataTablePreferenceKey,
  buildScopedAppDataTableUserKey,
  createDefaultAppDataTablePreferences,
  readAppDataTablePreferences,
  writeAppDataTablePreferences,
  type AppDataTablePreferences,
} from './appDataTable/preferences'
import { buildAppDataTableExportRequest } from './appDataTable/exporting'
import {
  findVxeClosest,
  findVxeElement,
  findVxeElements,
  markVxeElementDecorative,
} from './appDataTable/vxeDomAdapter'

interface AppDataTableGroupRow {
  __appDataTableGroup: true
  __appDataTableGroupId: string
  __appDataTableGroupLabel: string
  __appDataTableGroupLevel: number
  [key: string]: unknown
}

type AppDataTableRenderRow<T> = T | AppDataTableGroupRow
type AppDataTableExportType = AppDataTableQuickExportFormat | 'xlsx'
type AppDataTableExportScope = AppDataTableQuickExportMode | 'all' | 'custom'
type AppDataTablePrintWidthMode = 'current' | 'adaptive'

// vxe-table bundles the table custom panel, while its optional PC button is not
// registered by the table-only package. Register a tiny platform button so the
// native panel keeps its confirm/cancel/fixed actions without adding a second UI library.
const PlatformVxeButton = defineComponent({
  name: 'VxeButton',
  inheritAttrs: false,
  props: {
    content: String,
    disabled: Boolean,
    icon: String,
    title: String,
  },
  emits: ['click'],
  setup(buttonProps, { emit, slots }) {
    return () =>
      h(
        'button',
        {
          type: 'button',
          class: 'app-data-table__native-button',
          disabled: buttonProps.disabled,
          title: buttonProps.title,
          onClick: (event: MouseEvent) => emit('click', { $event: event }),
        },
        [
          buttonProps.icon === undefined ? null : h('i', { class: buttonProps.icon }),
          buttonProps.content,
          slots.default?.(),
        ],
      )
  },
})

const PlatformVxeNumberInput = defineComponent({
  name: 'VxeNumberInput',
  inheritAttrs: false,
  props: { modelValue: { type: [Number, String], default: '' } },
  emits: ['update:modelValue'],
  setup(inputProps, { emit }) {
    return () =>
      h('input', {
        type: 'number',
        value: inputProps.modelValue,
        onInput: (event: Event) =>
          emit('update:modelValue', Number((event.target as HTMLInputElement).value)),
      })
  },
})

const PlatformVxeRadioGroup = defineComponent({
  name: 'VxeRadioGroup',
  inheritAttrs: false,
  props: { modelValue: { type: [Number, String, Boolean], default: '' } },
  emits: ['update:modelValue'],
  setup(_, { slots }) {
    return () => h('div', slots.default?.())
  },
})

const PlatformDateRangeFilter = defineComponent({
  name: 'AppDataTableDateRangeFilter',
  props: {
    modelValue: {
      type: Array as PropType<string[]>,
      default: () => ['', ''],
    },
    disabled: Boolean,
  },
  emits: ['update:modelValue', 'change'],
  setup(dateProps, { emit }) {
    const localization = useLocalizationStore()
    const copy = computed(() => localeMessages[localization.locale].common.table)
    const update = (value: string[] | null) => emit('update:modelValue', value ?? ['', ''])
    return () =>
      h(ElDatePicker, {
        modelValue: dateProps.modelValue,
        type: 'daterange',
        valueFormat: 'YYYY-MM-DD',
        rangeSeparator: copy.value.rangeSeparator,
        startPlaceholder: copy.value.rangeStart,
        endPlaceholder: copy.value.rangeEnd,
        disabled: dateProps.disabled,
        clearable: false,
        onUpdateModelValue: update,
        onChange: (value: string[] | null) => emit('change', value ?? ['', '']),
      })
  },
})

if (!VxeUI.getComponent('VxeButton')) VxeUI.component(PlatformVxeButton)
if (!VxeUI.getComponent('VxeNumberInput')) VxeUI.component(PlatformVxeNumberInput)
if (!VxeUI.getComponent('VxeRadioGroup')) VxeUI.component(PlatformVxeRadioGroup)

const props = withDefaults(
  defineProps<{
    tableKey: string
    routeKey?: string
    userKey?: string
    columns: readonly AppDataTableColumn[]
    rows?: readonly T[]
    total?: number
    loading?: boolean
    mode?: AppDataTableMode
    rowKey?: string
    queryMode?: AppDataTableQueryMode
    pageSize?: number
    loader?: AppDataTableLoader<T>
    exporter?: (request: AppDataTableExportRequest) => Promise<void> | void
    tree?: AppDataTableTreeOptions<T>
    selection?: 'none' | 'single' | 'multiple'
    toolbarTitle?: string
    toolbarLabels?: boolean
  }>(),
  {
    rows: () => [],
    total: 0,
    loading: false,
    mode: 'list',
    rowKey: 'id',
    queryMode: 'top',
    pageSize: 25,
    selection: 'none',
    toolbarLabels: false,
  },
)

const localization = useLocalizationStore()
const copy = computed(() => localeMessages[localization.locale].common.table)
const emptyText = computed(() => localeMessages[localization.locale].common.state.empty)

const emit = defineEmits([
  'update:rows',
  'loaded',
  'load-error',
  'query-change',
  'query-mode-change',
  'selection-change',
  'export',
  'group-change',
])

const routeName = (() => {
  if (props.routeKey !== undefined) return props.routeKey
  try {
    // Use the browser path when no route key is supplied so isolated mounts stay dependency-light.
    return String((globalThis as { location?: Location }).location?.pathname ?? 'unknown')
  } catch {
    return 'unknown'
  }
})()
const session = getCurrentSession()
const userName =
  props.userKey ??
  (session === null ? 'anonymous' : buildScopedAppDataTableUserKey(session.user))
const preferenceKey = buildAppDataTablePreferenceKey(userName, routeName, props.tableKey)

function defaultPreferences(): AppDataTablePreferences {
  return createDefaultAppDataTablePreferences(props.columns)
}

function readPreferences(): AppDataTablePreferences {
  return readAppDataTablePreferences(localStorage, preferenceKey, defaultPreferences())
}

const preferences = ref<AppDataTablePreferences>(readPreferences())
const activeQueryMode = ref<AppDataTableQueryMode>(props.queryMode)
const topQuery = ref<Record<string, unknown>>({})
const quickSearch = ref('')
const headerFilters = ref<Record<string, unknown>>({})
const currentPage = ref(1)
const currentPageSize = ref(props.pageSize)
const serverRows = ref<T[]>([...props.rows])
const serverTotal = ref(props.total)
const loaderLoading = ref(false)
const sort = ref<AppDataTableSort | undefined>()
const settingsOpen = ref(false)
const sortOpen = ref(false)
const groupOpen = ref(false)
const exportMenuOpen = ref(false)
const printOpen = ref(false)
const quickExportMode = ref<AppDataTableExportScope>('current')
const customExportQuantity = ref(10000)
const exportFilename = ref(props.tableKey)
const exportType = ref<AppDataTableExportType>(props.exporter === undefined ? 'csv' : 'xlsx')
const exportFields = ref<string[]>(props.columns.map((column) => column.field))
const printFields = ref<string[]>([])
const printTitle = ref(`${props.tableKey}${copy.value.printTitleSuffix}`)
const printDataMode = ref<AppDataTableQuickExportMode>('current')
const printWidthMode = ref<AppDataTablePrintWidthMode>('current')
const selectedRows = ref<T[]>([])
const actionColumnWidth = ref(220)
const tableRef = ref<VxeTableInstance<T> | null>(null)
const toolbarRef = ref<VxeToolbarInstance | null>(null)
const tableFullscreen = ref(false)
const tableSettingsTrigger = ref<HTMLElement | null>(null)
const tableSettingsPanel = ref<HTMLElement | null>(null)
const columnSettingsTrigger = ref<HTMLElement | null>(null)
const sortTrigger = ref<HTMLElement | null>(null)
const sortPanel = ref<HTMLElement | null>(null)
const groupTrigger = ref<HTMLElement | null>(null)
const groupPanel = ref<HTMLElement | null>(null)
const exportTrigger = ref<HTMLElement | null>(null)
const exportPanel = ref<HTMLElement | null>(null)
const printTrigger = ref<HTMLElement | null>(null)
const printPanel = ref<HTMLElement | null>(null)
const tallUtilityPanelHeight = 'min(550px, calc(100vh - 240px))'
let headerObserver: MutationObserver | undefined
let actionColumnObserver: ResizeObserver | undefined
const mountedDateRangeFilters = new Set<HTMLElement>()

const exportTypeOptions = computed<readonly { value: AppDataTableExportType; label: string }[]>(
  () => [
    ...(props.exporter === undefined
      ? []
      : [{ value: 'xlsx' as const, label: copy.value.excel }]),
    { value: 'csv', label: copy.value.csv },
    { value: 'html', label: copy.value.html },
    { value: 'xml', label: copy.value.xml },
    { value: 'txt', label: copy.value.txt },
  ],
)

const isServerExportScope = computed(
  () => quickExportMode.value === 'all' || quickExportMode.value === 'custom',
)

function canUseExportScope(scope: AppDataTableExportScope): boolean {
  if (scope === 'selected') return selectedRows.value.length > 0
  if (scope === 'all' || scope === 'custom') return props.exporter !== undefined
  return true
}

function canUseExportType(type: AppDataTableExportType): boolean {
  if (type === 'xlsx') return props.exporter !== undefined && isServerExportScope.value
  return !isServerExportScope.value
}

const exportScopeHint = computed(() => {
  if (isServerExportScope.value) {
    return props.exporter === undefined
      ? copy.value.serverExportRequired
      : copy.value.serverExportUsed
  }
  return copy.value.loadedDataOnly
})

function normalizeExportSelection(): void {
  if (!canUseExportScope(quickExportMode.value)) quickExportMode.value = 'current'
  if (canUseExportType(exportType.value)) return
  exportType.value =
    exportTypeOptions.value.find((option) => canUseExportType(option.value))?.value ?? 'csv'
}

function onExportScopeChange(): void {
  normalizeExportSelection()
}

async function printHtmlInFrame(
  html: string,
  title: string,
  widthMode: AppDataTablePrintWidthMode,
): Promise<void> {
  const frame = document.createElement('iframe')
  frame.className = 'app-data-table__print-frame'
  frame.setAttribute('aria-hidden', 'true')
  frame.style.position = 'fixed'
  frame.style.width = '0'
  frame.style.height = '0'
  frame.style.border = '0'
  frame.style.visibility = 'hidden'
  document.body.append(frame)
  const frameWindow = frame.contentWindow
  const frameDocument = frame.contentDocument
  if (frameWindow === null || frameDocument === null) {
    frame.remove()
    return
  }
  const cleanup = () => frame.remove()
  frameWindow.addEventListener('afterprint', cleanup, { once: true })
  frameDocument.open()
  frameDocument.write(
    `<!doctype html><html><head><meta charset="utf-8"><title>${title}</title>` +
      '<style>body{margin:24px;font-family:Arial,"Microsoft YaHei",sans-serif}' +
      `table{border-collapse:collapse;width:100%;table-layout:${widthMode === 'adaptive' ? 'auto' : 'fixed'}}` +
      'th,td{padding:6px 8px;border:1px solid #dcdfe6;text-align:left}' +
      'th{background:#f5f7fa}</style></head><body>' +
      html +
      '</body></html>',
  )
  frameDocument.close()
  frameWindow.print()
  window.setTimeout(cleanup, 1000)
}

const orderedColumns = computed(() => [...props.columns])
const visibleColumns = computed(() => orderedColumns.value)
const groupFields = ref<string[]>([])
const groupableColumns = computed(() =>
  visibleColumns.value.filter((column) => column.groupable !== false),
)
function hasFilterValue(value: unknown): boolean {
  return !(
    value === undefined ||
    value === null ||
    value === '' ||
    (Array.isArray(value) && value.every((item) => item === ''))
  )
}

function localRowsForRequest(): T[] {
  const filters = activeQueryMode.value === 'top' ? topQuery.value : headerFilters.value
  const quickSearchValue =
    activeQueryMode.value === 'top' ? quickSearch.value.trim().toLowerCase() : ''
  const filtered = (props.rows as T[]).filter(
    (row) =>
      (quickSearchValue === '' ||
        props.columns.some((column) =>
          String(cellValue(row, column.field) ?? '')
            .toLowerCase()
            .includes(quickSearchValue),
        )) &&
      Object.entries(filters).every(([field, value]) => {
        if (field === 'keyword') return true
        if (!hasFilterValue(value)) return true
        const rowValue = cellValue(row, field)
        const filterKind = columnFilter(
          props.columns.find((column) => column.field === field) ?? {
            field,
            title: field,
          },
        )?.kind
        if (filterKind === 'date-range') {
          const [from = '', to = ''] = Array.isArray(value) ? value.map(String) : []
          const candidate = String(rowValue ?? '')
          return (!from || candidate >= from) && (!to || candidate <= to)
        }
        if (filterKind === 'select') {
          if (typeof value === 'boolean') return rowValue === value
          if (typeof value === 'number') {
            return typeof rowValue === 'number'
              ? rowValue === value
              : String(rowValue ?? '') === String(value)
          }
          return String(rowValue ?? '') === String(value)
        }
        if (typeof value === 'boolean') return rowValue === value
        if (typeof value === 'number') {
          return typeof rowValue === 'number'
            ? rowValue === value
            : String(rowValue ?? '') === String(value)
        }
        return String(rowValue ?? '')
          .toLowerCase()
          .includes(String(value).toLowerCase())
      }),
  )
  if (sort.value === undefined) return filtered
  const direction = sort.value.order === 'asc' ? 1 : -1
  return [...filtered].sort((left, right) => {
    const a = cellValue(left, sort.value!.field)
    const b = cellValue(right, sort.value!.field)
    if (a === b) return 0
    if (a === undefined || a === null) return -direction
    if (b === undefined || b === null) return direction
    return String(a).localeCompare(String(b), undefined, { numeric: true }) * direction
  })
}

const localRows = computed(() => localRowsForRequest())
const serverRowsForDisplay = computed(() => {
  const query = quickSearch.value.trim().toLowerCase()
  if (query === '') return serverRows.value
  return serverRows.value.filter((row) =>
    props.columns.some((column) =>
      String(cellValue(row as T, column.field) ?? '')
        .toLowerCase()
        .includes(query),
    ),
  )
})
const tableRows = computed<T[]>(() => {
  if (props.loader !== undefined) return serverRowsForDisplay.value as T[]
  const start = (currentPage.value - 1) * currentPageSize.value
  return localRows.value.slice(start, start + currentPageSize.value)
})
function isGroupRow(row: AppDataTableRenderRow<T>): row is AppDataTableGroupRow {
  return (row as AppDataTableGroupRow).__appDataTableGroup === true
}

const groupSpanMethod: VxeTablePropTypes.SpanMethod<AppDataTableRenderRow<T>> = ({
  row,
  column,
  fixed,
}) => {
  if (!isGroupRow(row)) return undefined
  if (fixed === 'left' || fixed === 'right') return { rowspan: 0, colspan: 0 }
  if (column.field === '__actions') return { rowspan: 0, colspan: 0 }
  if (column.type === 'seq' || column.type === 'checkbox' || column.type === 'radio') {
    return { rowspan: 1, colspan: 1 }
  }
  const dataColumns = visibleColumns.value.filter((column) => column.fixed === undefined)
  if (column.field === dataColumns[0]?.field) {
    return { rowspan: 1, colspan: dataColumns.length }
  }
  if (dataColumns.some((dataColumn) => dataColumn.field === column.field)) {
    return { rowspan: 0, colspan: 0 }
  }
  return undefined
}

function groupValue(row: T, field: string): string {
  const value = cellValue(row, field)
  return value === undefined || value === null || value === '' ? copy.value.unset : String(value)
}

const groupedTableRows = computed<AppDataTableRenderRow<T>[]>(() => {
  if (groupFields.value.length === 0) return tableRows.value
  const groupOrders = new Map<string, Map<string, number>>()
  groupFields.value.forEach((field, level) => {
    const order = new Map<string, number>()
    tableRows.value.forEach((row) => {
      const parentKey = groupFields.value
        .slice(0, level)
        .map((parentField) => groupValue(row, parentField))
        .join('\u0000')
      const key = `${parentKey}\u0000${groupValue(row, field)}`
      if (!order.has(key)) order.set(key, order.size)
    })
    groupOrders.set(field, order)
  })
  const rows = tableRows.value
    .map((row, index) => ({ row, index }))
    .sort((left, right) => {
      for (const [level, field] of groupFields.value.entries()) {
        const order = groupOrders.get(field)
        const leftParentKey = groupFields.value
          .slice(0, level)
          .map((parentField) => groupValue(left.row, parentField))
          .join('\u0000')
        const rightParentKey = groupFields.value
          .slice(0, level)
          .map((parentField) => groupValue(right.row, parentField))
          .join('\u0000')
        const leftKey = `${leftParentKey}\u0000${groupValue(left.row, field)}`
        const rightKey = `${rightParentKey}\u0000${groupValue(right.row, field)}`
        const result =
          level === 0
            ? groupValue(left.row, field).localeCompare(groupValue(right.row, field), undefined, {
                numeric: true,
              })
            : (order?.get(leftKey) ?? 0) - (order?.get(rightKey) ?? 0)
        if (result !== 0) return result
      }
      return left.index - right.index
    })
  const result: AppDataTableRenderRow<T>[] = []
  let previous: T | undefined
  for (const item of rows) {
    groupFields.value.forEach((field, level) => {
      if (
        previous === undefined ||
        groupFields.value
          .slice(0, level + 1)
          .some(
            (parentField) =>
              groupValue(previous as T, parentField) !== groupValue(item.row, parentField),
          )
      ) {
        const column = visibleColumns.value.find((candidate) => candidate.field === field)
        const groupId = `group-${field}-${level}-${result.length}`
        result.push({
          __appDataTableGroup: true,
          __appDataTableGroupId: groupId,
          __appDataTableGroupLabel: `${column?.title ?? field}：${groupValue(item.row, field)}`,
          __appDataTableGroupLevel: level,
          [props.rowKey]: groupId,
        })
      }
    })
    result.push(item.row)
    previous = item.row
  }
  return result
})
const tableTotal = computed(() =>
  props.loader === undefined ? localRows.value.length : serverTotal.value,
)
const densityClass = computed(() => `app-data-table--${preferences.value.density}`)
const densityRowHeight = computed(() =>
  preferences.value.density === 'comfortable'
    ? 44
    : preferences.value.density === 'medium'
      ? 38
      : 32,
)
const tableLoading = computed(() => props.loading || loaderLoading.value)

function columnFilter(column: AppDataTableColumn) {
  if (column.filter === false) return undefined
  return column.filter ?? { kind: 'text' as const }
}

function cellValue(row: T, field: string): unknown {
  return (row as Record<string, unknown>)[field]
}

function persist(): void {
  // Browser storage is a preference, not a data dependency.
  writeAppDataTablePreferences(localStorage, preferenceKey, preferences.value)
}

function buildQueryDescriptor(columns: string[]): QueryDescriptor {
  const activeFilters = activeQueryMode.value === 'top' ? topQuery.value : headerFilters.value
  const filters = Object.entries(activeFilters)
    .filter(([, value]) => {
      if (value === undefined || value === null || value === '') return false
      return !(Array.isArray(value) && value.every((item) => item === ''))
    })
    .map(([field, value]) => {
      const filter = columnFilter(props.columns.find((column) => column.field === field) ?? { field, title: field })
      return {
        field,
        operator: Array.isArray(value)
          ? ('between' as const)
          : filter?.kind === 'select'
            ? ('eq' as const)
            : ('contains' as const),
        value,
      }
    })
  return {
    filters,
    orderBy:
      sort.value === undefined
        ? []
        : [{ field: sort.value.field, direction: sort.value.order }],
    select: [...columns],
    pageIndex: currentPage.value,
    pageSize: currentPageSize.value,
    ...(quickSearch.value.trim() === '' ? {} : { search: quickSearch.value.trim() }),
  }
}

function request(): AppDataTableRequest {
  const dataFields = new Set(visibleColumns.value.map((column) => column.field))
  const nativeColumns = tableRef.value?.getColumns?.() as
    Array<{ field?: string; visible?: boolean }> | undefined
  const columns =
    nativeColumns && nativeColumns.length > 0
      ? nativeColumns
          .filter((column) => column.visible !== false && column.field !== undefined)
          .map((column) => column.field as string)
          .filter((field) => dataFields.has(field))
      : visibleColumns.value.map((column) => column.field)
  const descriptor = buildQueryDescriptor(columns)
  return {
    pageIndex: currentPage.value,
    pageSize: currentPageSize.value,
    queryMode: activeQueryMode.value,
    filters: {
      ...(activeQueryMode.value === 'top' ? topQuery.value : headerFilters.value),
    },
    ...(sort.value === undefined ? {} : { sort: sort.value }),
    columns,
    descriptor,
  }
}

const nativeCustomConfig = {
  storage: true,
  allowVisible: true,
  allowSort: true,
  allowFixed: true,
  allowResizable: true,
  trigger: 'manual' as const,
  mode: 'simple' as const,
  placement: 'bottom-right' as const,
  immediate: false,
  showFooter: true,
  resetButtonText: copy.value.resetDefault,
  cancelButtonText: copy.value.cancel,
  confirmButtonText: localeMessages[localization.locale].common.action.confirm,
}

async function reload(): Promise<void> {
  if (props.loader === undefined) {
    emit('query-change', request())
    return
  }
  loaderLoading.value = true
  try {
    const next = await props.loader(request())
    serverRows.value = next.items
    serverTotal.value = next.total
    currentPage.value = next.pageIndex
    currentPageSize.value = next.pageSize
    emit('loaded', next)
    emit('query-change', request())
  } catch (error) {
    emit('load-error', error)
  } finally {
    loaderLoading.value = false
  }
}

function switchQueryMode(mode: AppDataTableQueryMode): void {
  if (activeQueryMode.value === mode) return
  activeQueryMode.value = mode
  if (mode === 'top') headerFilters.value = {}
  else topQuery.value = {}
  currentPage.value = 1
  emit('query-mode-change', mode)
  void reload()
}

function setTopQuery(next: Record<string, unknown>): void {
  topQuery.value = { ...next }
  currentPage.value = 1
  void reload()
}

function setQuickSearch(value: string): void {
  quickSearch.value = value
  currentPage.value = 1
}

function setHeaderFilter(field: string, value: unknown): void {
  headerFilters.value = { ...headerFilters.value, [field]: value }
}

function applyHeaderFilter(): void {
  currentPage.value = 1
  void reload()
}

function onHeaderFilterChange(field: string, event: Event): void {
  const column = props.columns.find((item) => item.field === field)
  const rawValue = (event.target as HTMLSelectElement).value
  const option = columnFilter(column ?? { field, title: field })?.options?.find(
    (item) => String(item.value) === rawValue,
  )
  setHeaderFilter(field, option?.value ?? rawValue)
  applyHeaderFilter()
}

function headerRangePart(field: string, index: 0 | 1): string {
  const value = headerFilters.value[field]
  return Array.isArray(value) ? String(value[index] ?? '') : ''
}

function clearConditions(): void {
  topQuery.value = {}
  headerFilters.value = {}
  quickSearch.value = ''
  currentPage.value = 1
  void reload()
}

function onSortChange(event: { field?: string; order?: string | null }): void {
  sort.value =
    event.field && (event.order === 'asc' || event.order === 'desc')
      ? { field: event.field, order: event.order }
      : undefined
  currentPage.value = 1
  void reload()
}

function onPageChange(page: number): void {
  currentPage.value = page
  void reload()
}

function onPageSizeChange(size: number): void {
  currentPageSize.value = size
  currentPage.value = 1
  void reload()
}

function onColumnResizeChange(event: unknown): void {
  const value = event as {
    resizeColumn?: { field?: string }
    column?: { field?: string }
    resizeWidth?: number
  }
  const field = value.resizeColumn?.field ?? value.column?.field
  if (field === undefined || typeof value.resizeWidth !== 'number') return
  if (field === '__actions') {
    actionColumnWidth.value = Math.max(120, Math.round(value.resizeWidth))
    void nextTick(syncActionColumnWidth)
    return
  }
  preferences.value.widths[field] = Math.max(40, Math.round(value.resizeWidth))
  persist()
}

async function resetColumnWidths(): Promise<void> {
  const table = tableRef.value as
    | (VxeTableInstance<T> & {
        resetCustom?: (options?: { resizable?: boolean }) => Promise<unknown>
        recalculate?: (full?: boolean) => Promise<unknown>
      })
    | null
  preferences.value.widths = {}
  actionColumnWidth.value = 220
  persist()
  await nextTick()
  await table?.resetCustom?.({ resizable: true })
  await table?.recalculate?.(true)
  syncActionColumnWidth()
  syncNativeCustomPanel()
}

function syncActionColumnWidth(): void {
  const root = (tableRef.value as unknown as { $el?: HTMLElement } | null)?.$el
  if (root === undefined) return
  const candidates = findVxeElements<HTMLElement>(
    root,
    '.app-data-table__actions-column-header, .app-data-table__actions-column',
  )
  let renderedWidth = 0
  candidates.forEach((element) => {
    renderedWidth = Math.max(renderedWidth, element.getBoundingClientRect().width)
  })
  if (renderedWidth <= 0) return
  const nextWidth = Math.max(120, Math.round(renderedWidth))
  if (nextWidth !== actionColumnWidth.value) actionColumnWidth.value = nextWidth
}

function observeActionColumn(): void {
  if (actionColumnObserver !== undefined || typeof ResizeObserver === 'undefined') return
  const root = (tableRef.value as unknown as { $el?: HTMLElement } | null)?.$el
  if (root === undefined) return
  actionColumnObserver = new ResizeObserver(() => syncActionColumnWidth())
  actionColumnObserver.observe(root)
  const surface = findVxeClosest<HTMLElement>(root, '.app-data-table__surface')
  if (surface !== null) actionColumnObserver.observe(surface)
}

function setDensity(value: AppDataTableDensity): void {
  preferences.value.density = value
  persist()
  void nextTick(() => tableRef.value?.recalculate?.(true))
}

function onSelectionChange(event: { records?: T[]; row?: T }): void {
  const records = event.records ?? (event.row === undefined ? [] : [event.row])
  selectedRows.value = records.filter((row) => !isGroupRow(row as AppDataTableRenderRow<T>))
  emit('selection-change', selectedRows.value)
}

function clearSelection(): void {
  const table = tableRef.value as
    | (VxeTableInstance<T> & {
        clearCheckboxRow?: () => Promise<unknown> | unknown
        clearRadioRow?: () => Promise<unknown> | unknown
      })
    | null
  void table?.clearCheckboxRow?.()
  void table?.clearRadioRow?.()
  selectedRows.value = []
  emit('selection-change', selectedRows.value)
}

function toggleSortPanel(): void {
  sortOpen.value = !sortOpen.value
  groupOpen.value = false
  settingsOpen.value = false
  exportMenuOpen.value = false
  printOpen.value = false
  if (sortOpen.value) void nextTick(syncToolbarTallPanelsPosition)
}

function applyToolbarSort(field: string, order: AppDataTableSort['order']): void {
  void setSort(field, order)
}

async function setSort(field: string, order?: AppDataTableSort['order']): Promise<void> {
  const table = tableRef.value as
    | (VxeTableInstance<T> & {
        sort?: (sortConfs: { field: string; order: AppDataTableSort['order'] }) => Promise<unknown>
        clearSort?: () => Promise<unknown>
      })
    | null
  if (order === undefined) await table?.clearSort?.()
  else await table?.sort?.({ field, order })
  sort.value = order === undefined ? undefined : { field, order }
  currentPage.value = 1
  void reload()
}

function clearToolbarSort(): void {
  void setSort(sort.value?.field ?? '', undefined)
}

function cycleHeaderSort(event: MouseEvent, field: string): void {
  event.stopPropagation()
  const currentOrder = sort.value?.field === field ? sort.value.order : undefined
  const nextOrder: AppDataTableSort['order'] | undefined =
    currentOrder === undefined ? 'asc' : currentOrder === 'asc' ? 'desc' : undefined
  void setSort(field, nextOrder)
}

function toggleGroupPanel(): void {
  groupOpen.value = !groupOpen.value
  sortOpen.value = false
  settingsOpen.value = false
  exportMenuOpen.value = false
  printOpen.value = false
  if (groupOpen.value) void nextTick(syncToolbarTallPanelsPosition)
}

function toggleGroupField(field: string): void {
  groupFields.value = groupFields.value.includes(field)
    ? groupFields.value.filter((item) => item !== field)
    : [...groupFields.value, field]
  emit('group-change', [...groupFields.value])
}

function clearGroupFields(): void {
  groupFields.value = []
  emit('group-change', [])
}

async function exportData(): Promise<void> {
  if (props.exporter === undefined || !isServerExportScope.value) return
  const quantity =
    quickExportMode.value === 'custom'
      ? Math.max(1, Math.floor(customExportQuantity.value || 10000))
      : 'all'
  if (quantity === 'all' && !window.confirm(copy.value.exportConfirm)) return
  const tableRequest = request()
  const culture = document.documentElement.lang || 'zh-CN'
  const timeZone = Intl.DateTimeFormat().resolvedOptions().timeZone || 'UTC'
  const exportRequest: AppDataTableExportRequest = {
    ...tableRequest,
    ...buildAppDataTableExportRequest({
      descriptor: tableRequest.descriptor ?? buildQueryDescriptor(tableRequest.columns),
      filename: exportFilename.value.trim() || props.tableKey,
      columns: exportFields.value,
      quantity,
      culture,
      timeZone,
    }),
    quantity,
    rows: undefined,
  }
  emit('export', exportRequest)
  if (props.exporter !== undefined) await props.exporter(exportRequest)
  exportMenuOpen.value = false
}

function isQuickExportType(value: AppDataTableExportType): value is AppDataTableQuickExportFormat {
  return value !== 'xlsx'
}

async function quickExport(format: AppDataTableQuickExportFormat): Promise<void> {
  if (quickExportMode.value === 'all' || quickExportMode.value === 'custom') return
  await tableRef.value?.exportData({
    type: format,
    mode: quickExportMode.value,
    filename: exportFilename.value.trim() || props.tableKey,
    includeFields: exportFields.value,
    download: true,
  })
  exportMenuOpen.value = false
}

async function submitExport(): Promise<void> {
  normalizeExportSelection()
  if (isServerExportScope.value) {
    if (exportType.value === 'xlsx' && props.exporter !== undefined) await exportData()
    return
  }
  if (isQuickExportType(exportType.value) && canUseExportScope(quickExportMode.value)) {
    await quickExport(exportType.value)
  }
}

function openNativeCustom(event: MouseEvent): void {
  settingsOpen.value = false
  sortOpen.value = false
  groupOpen.value = false
  exportMenuOpen.value = false
  printOpen.value = false
  const table = tableRef.value as
    | (VxeTableInstance<T> & {
        triggerCustomEvent?: (event: MouseEvent) => void
      })
    | null
  table?.triggerCustomEvent?.(event)
  void nextTick(syncNativeCustomPanel)
}

/*
 * The VXE simple custom panel is rendered inside the table root, so its
 * built-in placement is relative to the table rather than the toolbar
 * trigger. Position it in the viewport after VXE has rendered it so the
 * button remains the stable anchor at every table size.
 */
function syncNativeCustomPanelPosition(): void {
  const root = (tableRef.value as unknown as { $el?: HTMLElement } | null)?.$el
  const trigger = columnSettingsTrigger.value
  if (root === undefined || trigger === null) return
  const panel = findVxeElement<HTMLElement>(root, '.vxe-table-custom-wrapper.is--active')
  if (panel === null) return

  const triggerRect = trigger.getBoundingClientRect()
  const panelRect = panel.getBoundingClientRect()
  const viewportWidth = window.innerWidth || document.documentElement.clientWidth
  const viewportHeight = window.innerHeight || document.documentElement.clientHeight
  const viewportGap = 12
  const triggerGap = 8
  const panelWidth = Math.max(
    0,
    Math.min(420, viewportWidth - viewportGap * 2) || panelRect.width || panel.offsetWidth,
  )
  const panelHeight = Math.max(0, panelRect.height || panel.offsetHeight || 360)
  const left = Math.min(
    Math.max(viewportGap, triggerRect.right - panelWidth),
    Math.max(viewportGap, viewportWidth - viewportGap - panelWidth),
  )
  const belowTop = triggerRect.bottom + triggerGap
  const canFitBelow = belowTop + panelHeight <= viewportHeight - viewportGap
  const aboveTop = triggerRect.top - triggerGap - panelHeight
  const canFitAbove = aboveTop >= viewportGap
  const placement = canFitBelow || !canFitAbove ? 'bottom' : 'top'
  const top = placement === 'bottom' ? belowTop : aboveTop
  const clampedTop = Math.min(
    Math.max(viewportGap, top),
    Math.max(viewportGap, viewportHeight - viewportGap - panelHeight),
  )

  panel.style.setProperty('width', `${Math.round(panelWidth)}px`, 'important')
  panel.style.setProperty('position', 'fixed', 'important')
  panel.style.setProperty('top', `${Math.round(clampedTop)}px`, 'important')
  panel.style.setProperty('right', 'auto', 'important')
  panel.style.setProperty('bottom', 'auto', 'important')
  panel.style.setProperty('left', `${Math.round(left)}px`, 'important')
  panel.style.setProperty('transform', 'none', 'important')
  panel.dataset.appDataTablePlacement = placement
  panel.dataset.appDataTableColumnLayout = 'single'
}

function syncToolbarTallPanelsPosition(): void {
  const viewportWidth = window.innerWidth || document.documentElement.clientWidth
  const viewportHeight = window.innerHeight || document.documentElement.clientHeight
  const viewportGap = 12
  const triggerGap = 8
  for (const [panel, trigger] of [
    [sortPanel.value, sortTrigger.value],
    [groupPanel.value, groupTrigger.value],
  ] as const) {
    if (panel === null || trigger === null) continue
    const triggerRect = trigger.getBoundingClientRect()
    const panelRect = panel.getBoundingClientRect()
    const panelWidth = Math.max(0, panelRect.width || panel.offsetWidth || 236)
    const panelHeight = Math.max(0, panelRect.height || panel.offsetHeight || 360)
    const left = Math.min(
      Math.max(viewportGap, triggerRect.left),
      Math.max(viewportGap, viewportWidth - viewportGap - panelWidth),
    )
    const belowTop = triggerRect.bottom + triggerGap
    const aboveTop = triggerRect.top - triggerGap - panelHeight
    const canFitBelow = belowTop + panelHeight <= viewportHeight - viewportGap
    const canFitAbove = aboveTop >= viewportGap
    const top = canFitBelow || !canFitAbove ? belowTop : aboveTop
    const clampedTop = Math.min(
      Math.max(viewportGap, top),
      Math.max(viewportGap, viewportHeight - viewportGap - panelHeight),
    )
    panel.style.position = 'fixed'
    panel.style.top = `${Math.round(clampedTop)}px`
    panel.style.right = 'auto'
    panel.style.bottom = 'auto'
    panel.style.left = `${Math.round(left)}px`
    panel.style.transform = 'none'
  }
}

function toggleTableSettings(): void {
  void closeNativeCustom()
  sortOpen.value = false
  groupOpen.value = false
  exportMenuOpen.value = false
  printOpen.value = false
  settingsOpen.value = !settingsOpen.value
}

function toggleExportDialog(): void {
  void closeNativeCustom()
  sortOpen.value = false
  groupOpen.value = false
  settingsOpen.value = false
  printOpen.value = false
  normalizeExportSelection()
  exportMenuOpen.value = !exportMenuOpen.value
}

async function closeNativeCustom(): Promise<void> {
  const table = tableRef.value as
    | (VxeTableInstance<T> & {
        closeCustom?: () => Promise<unknown>
        customCloseEvent?: (event: MouseEvent) => void
      })
    | null
  if (table?.customCloseEvent !== undefined) {
    table.customCloseEvent(new MouseEvent('mousedown', { bubbles: true }))
    return
  }
  await table?.closeCustom?.()
}

function nativeHeaderFieldClass(field: string): string {
  return `app-data-table__header-field-${field.replace(/[^A-Za-z0-9_-]/g, '_')}`
}

function appendFilterControl(
  cell: HTMLElement,
  column: AppDataTableColumn,
  interactive = true,
): void {
  const filter = columnFilter(column)
  if (filter === undefined) return
  const testId = (suffix = '') =>
    interactive ? `app-data-table-header-filter-${column.field}${suffix}` : undefined
  if (filter.kind === 'select') {
    const select = document.createElement('select')
    select.value = String(headerFilters.value[column.field] ?? '')
    select.style.height = '26px'
    select.style.minHeight = '26px'
    const selectTestId = testId()
    if (selectTestId !== undefined) select.dataset.testid = selectTestId
    select.setAttribute('aria-label', `${column.title}${copy.value.querySuffix}`)
    if (!interactive) {
      select.disabled = true
      select.tabIndex = -1
    }
    const placeholder = document.createElement('option')
    placeholder.value = ''
    placeholder.textContent = column.title
    select.append(placeholder)
    filter.options?.forEach((option) => {
      const item = document.createElement('option')
      item.value = String(option.value)
      item.textContent = option.label
      select.append(item)
    })
    if (interactive) {
      select.addEventListener('change', (event) => onHeaderFilterChange(column.field, event))
    }
    cell.append(select)
    return
  }
  if (filter.kind === 'date-range') {
    const range = document.createElement('div')
    range.className = 'app-data-table__date-range-control'
    range.style.height = '26px'
    range.setAttribute('aria-label', `${column.title}${copy.value.dateRange}`)
    const rangeTestId = testId('-range')
    if (rangeTestId !== undefined) range.dataset.testid = rangeTestId
    render(
      h(PlatformDateRangeFilter, {
        modelValue: [headerRangePart(column.field, 0), headerRangePart(column.field, 1)],
        disabled: !interactive,
        'onUpdate:modelValue': (value: string[]) => {
          setHeaderFilter(column.field, value)
        },
        onChange: (value: string[]) => {
          setHeaderFilter(column.field, value)
          applyHeaderFilter()
        },
      }),
      range,
    )
    if (!interactive) markVxeElementDecorative(range)
    mountedDateRangeFilters.add(range)
    cell.append(range)
    return
  }
  const input = document.createElement('input')
  input.type = 'search'
  input.style.height = '26px'
  input.style.minHeight = '26px'
  input.value = String(headerFilters.value[column.field] ?? '')
  input.placeholder = `${column.title}${copy.value.querySuffix}`
  input.setAttribute('aria-label', `${column.title}${copy.value.querySuffix}`)
  const inputTestId = testId()
  if (inputTestId !== undefined) input.dataset.testid = inputTestId
  input.tabIndex = interactive ? 0 : -1
  input.readOnly = !interactive
  if (interactive) {
    input.addEventListener('input', (event) => {
      setHeaderFilter(column.field, (event.target as HTMLInputElement).value)
      applyHeaderFilter()
    })
    input.addEventListener('keyup', (event) => {
      if ((event as KeyboardEvent).key === 'Enter') applyHeaderFilter()
    })
  }
  cell.append(input)
}

function isMainVxeHeader(headerTable: HTMLTableElement): boolean {
  const wrapper = findVxeClosest(headerTable, '.vxe-table--header-wrapper')
  return wrapper?.classList.contains('body--wrapper') === true
}

function syncVxeDuplicateAccessibility(): void {
  const root = (tableRef.value as unknown as { $el?: HTMLElement } | null)?.$el
  if (root === undefined) return
  findVxeElements<HTMLElement>(root, '.vxe-table--column.fixed--hidden').forEach(
    markVxeElementDecorative,
  )
}

function syncNativeHeaderFilterRows(): void {
  const root = (tableRef.value as unknown as { $el?: HTMLElement } | null)?.$el
  if (root === undefined) return
  const tables = findVxeElements<HTMLTableElement>(root, 'table.vxe-table--header')
  if (
    activeQueryMode.value === 'header' &&
    tables.length > 0 &&
    tables.every((headerTable) => {
      const row = findVxeElement(headerTable, '.app-data-table__header-filter-row')
      return row !== null && row.children.length > 0
    })
  ) {
    return
  }
  tables.forEach((headerTable) => {
    findVxeElements(headerTable, '.app-data-table__header-filter-row').forEach((row) => {
      findVxeElements<HTMLElement>(row, '.app-data-table__date-range-control').forEach((filter) => {
        render(null, filter)
        mountedDateRangeFilters.delete(filter)
      })
      row.remove()
    })
  })
  if (activeQueryMode.value !== 'header') return
  tables.forEach((headerTable) => {
    const headerRow = findVxeElement<HTMLTableRowElement>(headerTable, 'thead > .vxe-header--row')
    const headerWrapper = findVxeClosest(headerTable, '.vxe-table--header-wrapper')
    if (headerRow === null || headerWrapper === null) return
    if (findVxeElement(headerTable, '.app-data-table__header-filter-row') !== null) return
    const row = document.createElement('tr')
    row.className = 'app-data-table__header-filter-row'
    Array.from(headerRow.children)
      .filter((child): child is HTMLElement => child instanceof HTMLElement)
      .forEach((headerCell) => {
        const cell = document.createElement('th')
        cell.className = 'vxe-header--column app-data-table__header-filter-cell'
        cell.style.padding = '5px 6px'
        const width = headerCell.getBoundingClientRect().width
        if (width > 0) cell.style.width = `${width}px`
        const column = visibleColumns.value.find((candidate) =>
          headerCell.classList.contains(nativeHeaderFieldClass(candidate.field)),
        )
        if (column !== undefined) appendFilterControl(cell, column, isMainVxeHeader(headerTable))
        row.append(cell)
      })
    if (!isMainVxeHeader(headerTable)) markVxeElementDecorative(row)
    headerTable.tHead?.append(row)
  })
  syncVxeDuplicateAccessibility()
}

function createNativePreferenceToggle(
  testId: string,
  labelText: string,
  checked: boolean,
  onChange: (value: boolean) => void,
): HTMLLabelElement {
  const label = document.createElement('label')
  label.className = 'app-data-table__native-toggle'
  const input = document.createElement('input')
  input.type = 'checkbox'
  input.checked = checked
  input.dataset.testid = testId
  input.setAttribute('aria-label', labelText)
  input.addEventListener('change', (event) => {
    event.stopPropagation()
    onChange(input.checked)
  })
  const text = document.createElement('span')
  text.textContent = labelText
  label.append(input, text)
  return label
}

function syncNativeCustomHeader(panel: HTMLElement): void {
  const header = findVxeElement<HTMLElement>(panel, '.vxe-table-custom--header')
  if (header === null) return
  header.dataset.columnSettingsTitle = copy.value.columnSettings
  header.dataset.columnSettingsDescription = copy.value.columnSettingsHint

  const allLabel = findVxeElement<HTMLElement>(
    header,
    '.vxe-table-custom--panel-list .vxe-checkbox--label',
  )
  if (allLabel !== null && allLabel.textContent !== copy.value.columnVisible) {
    allLabel.textContent = copy.value.columnVisible
  }

  let tools = findVxeElement<HTMLElement>(header, '.app-data-table__native-header-tools')
  if (tools === null) {
    tools = document.createElement('div')
    tools.className = 'app-data-table__native-header-tools'
    tools.append(
      createNativePreferenceToggle(
        'app-data-table-native-show-index',
        copy.value.indexColumn,
        preferences.value.showIndex,
        (value) => {
          preferences.value.showIndex = value
          persist()
        },
      ),
      createNativePreferenceToggle(
        'app-data-table-native-border',
        copy.value.border,
        preferences.value.border,
        (value) => {
          preferences.value.border = value
          persist()
        },
      ),
    )
    const resetWidths = document.createElement('button')
    resetWidths.type = 'button'
    resetWidths.className = 'app-data-table__native-reset'
    resetWidths.dataset.testid = 'app-data-table-native-reset-widths'
    resetWidths.textContent = copy.value.resetWidth
    resetWidths.addEventListener('click', (event) => {
      event.preventDefault()
      event.stopPropagation()
      void resetColumnWidths()
    })
    const reset = document.createElement('button')
    reset.type = 'button'
    reset.className = 'app-data-table__native-reset'
    reset.dataset.testid = 'app-data-table-native-reset'
    reset.textContent = copy.value.resetDefault
    reset.addEventListener('click', (event) => {
      event.preventDefault()
      event.stopPropagation()
      const table = tableRef.value as
        | (VxeTableInstance<T> & {
            resetCustom?: () => Promise<unknown>
          })
        | null
      void table?.resetCustom?.()
      preferences.value = defaultPreferences()
      persist()
      void nextTick(() => syncNativeCustomPanel())
    })
    tools.append(resetWidths, reset)
    header.append(tools)
  }

  const showIndex = findVxeElement<HTMLInputElement>(
    tools,
    '[data-testid="app-data-table-native-show-index"]',
  )
  const border = findVxeElement<HTMLInputElement>(
    tools,
    '[data-testid="app-data-table-native-border"]',
  )
  if (showIndex !== null) showIndex.checked = preferences.value.showIndex
  if (border !== null) border.checked = preferences.value.border
}

function syncNativeCustomPanel(): void {
  const root = (tableRef.value as unknown as { $el?: HTMLElement } | null)?.$el
  if (root === undefined) return
  const panel = findVxeElement<HTMLElement>(root, '.vxe-table-custom-wrapper')
  if (panel === null) return

  if (!panel.classList.contains('is--active')) {
    panel.style.display = 'none'
    return
  }

  panel.style.removeProperty('display')

  syncNativeCustomHeader(panel)

  panel.classList.add('app-data-table__native-column-settings')
  panel.style.height = tallUtilityPanelHeight
  panel.style.maxHeight = tallUtilityPanelHeight
  panel.style.flexDirection = 'column'
  panel.style.fontSize = '12px'
  panel.dataset.fontSize = '12px'
  const body = findVxeElement<HTMLElement>(panel, '.vxe-table-custom--body')
  if (body !== null) {
    body.style.height = 'auto'
    body.style.minHeight = '0'
    body.style.maxHeight = 'none'
    body.style.flex = '1 1 auto'
    body.style.overflowX = 'hidden'
    body.style.overflowY = 'auto'
  }
  syncNativeCustomPanelPosition()
}

const printableColumns = computed(() => {
  const currentFields = new Set(request().columns)
  return visibleColumns.value.filter((column) => currentFields.has(column.field))
})

function openPrintDialog(): void {
  void closeNativeCustom()
  sortOpen.value = false
  groupOpen.value = false
  settingsOpen.value = false
  exportMenuOpen.value = false
  printFields.value = printableColumns.value.map((column) => column.field)
  if (selectedRows.value.length === 0) printDataMode.value = 'current'
  printOpen.value = true
}

async function printData(): Promise<void> {
  const table = tableRef.value as
    | (VxeTableInstance<T> & {
        getPrintHtml?: (options?: { columns?: string[] }) => Promise<{ html: string }>
        print?: (options?: { columns?: string[] }) => Promise<unknown>
      })
    | null
  try {
    const options = {
      columns: printFields.value,
      mode: printDataMode.value,
      sheetName: printTitle.value.trim() || `${props.tableKey}${copy.value.printTitleSuffix}`,
    }
    if (table?.getPrintHtml !== undefined) {
      const result = await table.getPrintHtml(options)
      await printHtmlInFrame(result.html, options.sheetName, printWidthMode.value)
    } else {
      await table?.print?.(options)
    }
  } finally {
    printOpen.value = false
  }
}

function dismissPanels(event: MouseEvent): void {
  const target = event.target as Node
  const inside = (element: HTMLElement | null): boolean => element?.contains(target) ?? false
  if (inside(tableSettingsPanel.value) || inside(tableSettingsTrigger.value)) {
    sortOpen.value = false
    groupOpen.value = false
    exportMenuOpen.value = false
    printOpen.value = false
    return
  }
  if (inside(sortPanel.value) || inside(sortTrigger.value)) {
    settingsOpen.value = false
    groupOpen.value = false
    exportMenuOpen.value = false
    printOpen.value = false
    return
  }
  if (inside(groupPanel.value) || inside(groupTrigger.value)) {
    settingsOpen.value = false
    sortOpen.value = false
    exportMenuOpen.value = false
    printOpen.value = false
    return
  }
  if (inside(exportPanel.value) || inside(exportTrigger.value)) {
    settingsOpen.value = false
    sortOpen.value = false
    groupOpen.value = false
    printOpen.value = false
    return
  }
  if (inside(printPanel.value) || inside(printTrigger.value)) {
    settingsOpen.value = false
    sortOpen.value = false
    groupOpen.value = false
    exportMenuOpen.value = false
    return
  }
  settingsOpen.value = false
  sortOpen.value = false
  groupOpen.value = false
  exportMenuOpen.value = false
  printOpen.value = false
}

function onKeydown(event: KeyboardEvent): void {
  if (event.key !== 'Escape') return
  settingsOpen.value = false
  sortOpen.value = false
  groupOpen.value = false
  exportMenuOpen.value = false
  printOpen.value = false
  closeNativeCustom()
}

function clearUiCacheState(): void {
  topQuery.value = {}
  headerFilters.value = {}
  quickSearch.value = ''
  currentPage.value = 1
  sort.value = undefined
  selectedRows.value = []
  void reload()
}

onMounted(() => {
  const table = tableRef.value
  const toolbar = toolbarRef.value
  if (table && toolbar) void table.connectToolbar(toolbar)
  void nextTick(() => {
    syncNativeHeaderFilterRows()
    syncVxeDuplicateAccessibility()
    syncNativeCustomPanel()
    syncActionColumnWidth()
    observeActionColumn()
  })
  window.setTimeout(() => {
    syncNativeHeaderFilterRows()
    syncVxeDuplicateAccessibility()
    syncNativeCustomPanel()
    syncActionColumnWidth()
    observeActionColumn()
  }, 0)
  const tableRoot = (table as unknown as { $el?: HTMLElement } | null)?.$el
  if (tableRoot !== undefined && typeof MutationObserver !== 'undefined') {
    headerObserver = new MutationObserver(() => {
      syncNativeHeaderFilterRows()
      syncVxeDuplicateAccessibility()
      syncNativeCustomPanel()
      syncActionColumnWidth()
    })
    headerObserver.observe(tableRoot, { childList: true, subtree: true })
  }
  window.addEventListener('resize', syncNativeCustomPanelPosition)
  window.addEventListener('resize', syncToolbarTallPanelsPosition)
  document.addEventListener('mousedown', dismissPanels)
  document.addEventListener('keydown', onKeydown)
  document.addEventListener('industrial-platform:ui-cache-cleared', clearUiCacheState)
})

onUpdated(() => {
  void nextTick(() => {
    syncNativeHeaderFilterRows()
    syncVxeDuplicateAccessibility()
    syncNativeCustomPanel()
    syncActionColumnWidth()
    observeActionColumn()
  })
  window.setTimeout(() => {
    syncNativeHeaderFilterRows()
    syncVxeDuplicateAccessibility()
    syncNativeCustomPanel()
    syncActionColumnWidth()
    observeActionColumn()
  }, 0)
})

onBeforeUnmount(() => {
  headerObserver?.disconnect()
  headerObserver = undefined
  actionColumnObserver?.disconnect()
  actionColumnObserver = undefined
  window.removeEventListener('resize', syncNativeCustomPanelPosition)
  window.removeEventListener('resize', syncToolbarTallPanelsPosition)
  mountedDateRangeFilters.forEach((filter) => render(null, filter))
  mountedDateRangeFilters.clear()
  document.removeEventListener('mousedown', dismissPanels)
  document.removeEventListener('keydown', onKeydown)
  document.removeEventListener('industrial-platform:ui-cache-cleared', clearUiCacheState)
})

const treeConfig = computed<Record<string, unknown> | undefined>(() => {
  if (props.mode !== 'tree') return undefined
  return {
    transform: false,
    rowField: props.rowKey,
    childrenField: props.tree?.childrenField ?? 'children',
    hasChild: props.tree?.hasChildrenField ?? 'hasChildren',
    ...(props.tree?.loadChildren === undefined
      ? {}
      : {
          loadMethod: ({ row }: { row: T }) => props.tree!.loadChildren!(row),
        }),
  }
})
const checkboxConfig = computed(() => ({
  reserve: true,
  checkStrictly: props.tree?.checkStrictly ?? false,
  checkMethod: ({ row }: { row: AppDataTableRenderRow<T> }) => !isGroupRow(row),
}))
const tableBindings = computed(() => ({
  id: preferenceKey,
  data: groupedTableRows.value,
  border: preferences.value.border,
  customConfig: nativeCustomConfig,
  rowConfig: { keyField: props.rowKey },
  cellConfig: { height: densityRowHeight.value },
  columnConfig: { resizable: true },
  sortConfig: { iconLayout: 'vertical' as const },
  rowClassName: ({ row }: { row: AppDataTableRenderRow<T> }) =>
    isGroupRow(row) ? 'app-data-table__group-row' : '',
  spanMethod: groupSpanMethod,
  ...(treeConfig.value === undefined ? {} : { treeConfig: treeConfig.value }),
  ...(props.mode === 'detail' ? { expandConfig: { trigger: 'row' as const } } : {}),
  ...(props.selection === 'single'
    ? {
        radioConfig: {
          reserve: true,
          checkMethod: ({ row }: { row: AppDataTableRenderRow<T> }) => !isGroupRow(row),
        },
      }
    : {}),
  ...(props.selection === 'multiple' ? { checkboxConfig: checkboxConfig.value } : {}),
  emptyText: emptyText.value,
  showOverflow: 'title' as const,
  showHeaderOverflow: 'title' as const,
}))

watch(
  () => props.queryMode,
  (value) => switchQueryMode(value),
)
watch(
  () => props.total,
  (value) => {
    if (props.loader === undefined) serverTotal.value = value
  },
)
watch(
  () => props.rows,
  (value) => {
    if (props.loader !== undefined) {
      serverRows.value = [...value]
      serverTotal.value = props.total
    }
  },
)

defineExpose({
  topQuery,
  headerFilters,
  activeQueryMode,
  selectedRows,
  request,
  reload,
  switchQueryMode,
  setTopQuery,
  setHeaderFilter,
  clearConditions,
  setDensity,
  exportData,
  printData,
  tableFullscreen,
})
</script>

<template>
  <section
    class="app-data-table"
    :class="[
      densityClass,
      'app-data-table--compact-stack',
      { 'app-data-table--fullscreen': tableFullscreen },
    ]"
    data-testid="app-data-table"
    :data-column-settings-title="copy.columnSettings"
    :data-column-settings-description="copy.columnSettingsHint"
    @mousedown="dismissPanels"
  >
    <div v-if="activeQueryMode === 'top' && $slots.toolbar" class="app-data-table__top-query">
      <slot name="toolbar" :query="topQuery" :set-query="setTopQuery" />
    </div>

    <div class="app-data-table__card">
      <div
        v-if="$slots['toolbar-actions']"
        class="app-data-table__business-actions app-data-table__toolbar-actions app-data-table__toolbar-actions--styled"
        :aria-label="copy.businessActions"
      >
        <slot name="toolbar-actions" />
      </div>

      <div class="app-data-table__toolbar">
        <div class="app-data-table__toolbar-left" role="group" :aria-label="copy.primaryTools">
          <strong v-if="toolbarTitle" class="app-data-table__toolbar-title">{{ toolbarTitle }}</strong>
          <button
            type="button"
            class="app-data-table__icon-button"
            :class="{ 'is-active': activeQueryMode === 'header' }"
            data-testid="app-data-table-query-toggle"
            :aria-label="activeQueryMode === 'top' ? copy.queryHeader : copy.queryTop"
            :title="activeQueryMode === 'top' ? copy.queryHeader : copy.queryTop"
            :aria-pressed="activeQueryMode === 'header'"
            @click="switchQueryMode(activeQueryMode === 'top' ? 'header' : 'top')"
          >
            <Filter class="app-data-table__query-filter-icon" aria-hidden="true" />
            <span v-if="toolbarLabels" class="app-data-table__toolbar-label">
              {{ activeQueryMode === 'top' ? copy.queryHeaderLabel : copy.queryTopLabel }}
            </span>
          </button>
          <div class="app-data-table__toolbar-popover">
            <button
              ref="sortTrigger"
              type="button"
              class="app-data-table__icon-button"
              data-testid="app-data-table-sort"
              :aria-expanded="sortOpen"
              :aria-label="copy.sort"
              :title="copy.sort"
              @click="toggleSortPanel"
            >
              <SortUp aria-hidden="true" />
              <span v-if="toolbarLabels" class="app-data-table__toolbar-label">{{ copy.sort }}</span>
            </button>
            <div
              v-if="sortOpen"
              ref="sortPanel"
              class="app-data-table__popover app-data-table__panel app-data-table__sort-panel"
              data-testid="app-data-table-sort-panel"
              data-font-size="12px"
              :style="{ height: tallUtilityPanelHeight }"
              @mousedown.stop
            >
              <div class="app-data-table__dialog-header">
                <strong>{{ copy.sortSettings }}</strong>
                <span>{{ copy.sortHint }}</span>
              </div>
              <div class="app-data-table__panel-list">
                <div
                  v-for="column in visibleColumns.filter((item) => item.sortable !== false)"
                  :key="column.field"
                  class="app-data-table__sort-option"
                  :class="{ 'is-active': sort?.field === column.field }"
                >
                  <span class="app-data-table__sort-label">{{ column.title }}</span>
                  <span
                    class="app-data-table__sort-directions"
                    role="group"
                    :aria-label="`${column.title}${copy.sortDirection}`"
                  >
                    <button
                      type="button"
                      :data-testid="`app-data-table-sort-${column.field}-asc`"
                      :class="{
                        'is-active': sort?.field === column.field && sort.order === 'asc',
                      }"
                      @click="applyToolbarSort(column.field, 'asc')"
                    >
                      {{ copy.ascending }}
                    </button>
                    <button
                      type="button"
                      :data-testid="`app-data-table-sort-${column.field}-desc`"
                      :class="{
                        'is-active': sort?.field === column.field && sort.order === 'desc',
                      }"
                      @click="applyToolbarSort(column.field, 'desc')"
                    >
                      {{ copy.descending }}
                    </button>
                  </span>
                </div>
              </div>
              <div class="app-data-table__panel-footer">
                <button
                  type="button"
                  data-testid="app-data-table-sort-clear"
                  :disabled="sort === undefined"
                  @click="clearToolbarSort"
                >
                  {{ copy.clearSort }}
                </button>
              </div>
            </div>
          </div>
          <div class="app-data-table__toolbar-popover">
            <button
              ref="groupTrigger"
              type="button"
              class="app-data-table__icon-button"
              data-testid="app-data-table-group"
              :aria-expanded="groupOpen"
              :aria-label="copy.group"
              :title="copy.group"
              @click="toggleGroupPanel"
            >
              <Connection aria-hidden="true" />
              <span v-if="toolbarLabels" class="app-data-table__toolbar-label">{{ copy.group }}</span>
            </button>
            <div
              v-if="groupOpen"
              ref="groupPanel"
              class="app-data-table__popover app-data-table__panel app-data-table__group-panel"
              data-testid="app-data-table-group-panel"
              data-font-size="12px"
              :style="{ height: tallUtilityPanelHeight }"
              @mousedown.stop
            >
              <div class="app-data-table__dialog-header">
                <strong>{{ copy.groupSettings }}</strong>
                <span>{{ copy.groupHint }}</span>
              </div>
              <div class="app-data-table__panel-list">
                <button
                  v-for="column in groupableColumns"
                  :key="column.field"
                  type="button"
                  class="app-data-table__group-option"
                  :data-testid="`app-data-table-group-field-${column.field}`"
                  :class="{ 'is-active': groupFields.includes(column.field) }"
                  @click="toggleGroupField(column.field)"
                >
                  <span class="app-data-table__group-order">{{
                    groupFields.indexOf(column.field) + 1 || ''
                  }}</span>
                  <span class="app-data-table__group-label">{{ column.title }}</span>
                </button>
              </div>
              <div class="app-data-table__panel-footer">
                <button
                  type="button"
                  data-testid="app-data-table-group-clear"
                  :disabled="groupFields.length === 0"
                  @click="clearGroupFields"
                >
                  {{ copy.clearGroup }}
                </button>
              </div>
            </div>
          </div>
          <label
            class="app-data-table__quick-search"
            :class="{ 'is-disabled': activeQueryMode === 'header' }"
            :aria-label="copy.quickSearch"
          >
            <Search aria-hidden="true" />
            <input
              data-testid="app-data-table-quick-search"
              type="search"
              :value="quickSearch"
              :placeholder="copy.quickSearch"
              :disabled="activeQueryMode === 'header'"
              @input="setQuickSearch(($event.target as HTMLInputElement).value)"
            />
          </label>
          <div class="app-data-table__export">
            <button
              ref="exportTrigger"
              type="button"
              class="app-data-table__icon-button"
              data-testid="app-data-table-export"
              :aria-expanded="exportMenuOpen"
              :aria-label="copy.download"
              :title="copy.download"
              @click="toggleExportDialog"
            >
              <Download aria-hidden="true" />
            </button>
            <div
              v-if="exportMenuOpen"
              ref="exportPanel"
              class="app-data-table__popover app-data-table__panel app-data-table__form-panel app-data-table__form-panel--shared app-data-table__form-panel--dense app-data-table__popover--below-trigger app-data-table__export-menu"
              data-testid="app-data-table-export-menu"
              data-font-size="12px"
              style="--app-data-table-form-panel-width: 520px; overflow-y: hidden"
              role="dialog"
              :aria-label="copy.downloadSettings"
              @mousedown.self="exportMenuOpen = false"
              @mousedown.stop
              @click.stop
            >
              <div class="app-data-table__dialog-header">
                <strong>{{ copy.downloadData }}</strong>
                <span>{{ copy.downloadHint }}</span>
              </div>
              <div class="app-data-table__form-grid">
                <label class="app-data-table__form-row">
                  <span>{{ copy.fileName }}</span>
                  <input v-model="exportFilename" data-testid="app-data-table-export-filename" />
                </label>
                <label class="app-data-table__form-row">
                  <span>{{ copy.saveType }}</span>
                  <select v-model="exportType" data-testid="app-data-table-export-type">
                    <option
                      v-for="option in exportTypeOptions"
                      :key="option.value"
                      :value="option.value"
                      :disabled="!canUseExportType(option.value)"
                    >
                      {{ option.label }}
                    </option>
                  </select>
                </label>
                <label class="app-data-table__form-row">
                  <span>{{ copy.saveData }}</span>
                  <div class="app-data-table__export-scope-field">
                    <div class="app-data-table__export-scope-control">
                      <select
                        v-model="quickExportMode"
                        data-testid="app-data-table-export-scope"
                        @change="onExportScopeChange"
                      >
                      <option value="current">{{ copy.currentPage }}</option>
                      <option value="selected" :disabled="selectedRows.length === 0">
                        {{ copy.selectedRows }}（{{ selectedRows.length }}）
                        </option>
                        <option value="all" :disabled="props.exporter === undefined">
                          {{ copy.allData }}
                        </option>
                        <option value="custom" :disabled="props.exporter === undefined">
                          {{ copy.customData }}
                        </option>
                      </select>
                      <input
                        v-if="quickExportMode === 'custom'"
                        v-model.number="customExportQuantity"
                        type="number"
                        min="1"
                        step="1"
                        :aria-label="copy.customExportQuantity"
                        data-testid="app-data-table-export-custom-quantity"
                      />
                    </div>
                    <small
                      class="app-data-table__export-scope-hint"
                      data-testid="app-data-table-export-scope-hint"
                    >
                      {{ exportScopeHint }}
                    </small>
                  </div>
                </label>
              </div>
              <section class="app-data-table__form-section">
                <div class="app-data-table__form-section-heading">
                  <strong>{{ copy.selectFields }}</strong>
                  <small>{{ copy.selectFieldsHint }}</small>
                </div>
                <fieldset
                  class="app-data-table__export-fields app-data-table__field-picker app-data-table__field-picker--single-column"
                >
                  <legend class="app-data-table__sr-only">{{ copy.selectFields }}</legend>
                  <label v-for="column in visibleColumns" :key="column.field">
                    <input v-model="exportFields" type="checkbox" :value="column.field" />
                    <span>{{ column.title }}</span>
                  </label>
                </fieldset>
              </section>
              <div
                class="app-data-table__panel-footer app-data-table__dialog-actions app-data-table__form-footer"
              >
                <button
                  type="button"
                  data-testid="app-data-table-export-cancel"
                  @click="exportMenuOpen = false"
                >
                  {{ copy.cancel }}
                </button>
                <button
                  type="button"
                  data-testid="app-data-table-export-confirm"
                  @click="submitExport"
                >
                  {{ copy.downloadConfirm }}
                </button>
              </div>
            </div>
          </div>
          <div class="app-data-table__toolbar-popover">
            <button
              ref="printTrigger"
              type="button"
              class="app-data-table__icon-button"
              data-testid="app-data-table-print"
              :aria-label="copy.print"
              :title="copy.printCurrentTitle"
              @click="openPrintDialog"
            >
              <Printer aria-hidden="true" />
            </button>
            <div
              v-if="printOpen"
              ref="printPanel"
              class="app-data-table__popover app-data-table__panel app-data-table__form-panel app-data-table__form-panel--shared app-data-table__form-panel--dense app-data-table__popover--below-trigger app-data-table__print-dialog"
              data-testid="app-data-table-print-dialog"
              data-font-size="12px"
              style="--app-data-table-form-panel-width: 520px; overflow-y: hidden"
              role="dialog"
              :aria-label="copy.printSettings"
              @mousedown.stop
              @click.stop
            >
              <div class="app-data-table__dialog-header">
                <strong>{{ copy.printData }}</strong>
                <span>{{ copy.printHint }}</span>
              </div>
              <div class="app-data-table__form-grid">
                <label class="app-data-table__form-row">
                  <span>{{ copy.title }}</span>
                  <input v-model="printTitle" data-testid="app-data-table-print-title" />
                </label>
                <label class="app-data-table__form-row">
                  <span>{{ copy.selectData }}</span>
                  <select v-model="printDataMode" data-testid="app-data-table-print-scope">
                    <option value="current">{{ copy.currentPage }}</option>
                    <option value="selected" :disabled="selectedRows.length === 0">
                      {{ copy.selectionSummary.replace('{count}', String(selectedRows.length)) }}
                    </option>
                  </select>
                </label>
                <label class="app-data-table__form-row">
                  <span>{{ copy.columnWidth }}</span>
                  <select v-model="printWidthMode" data-testid="app-data-table-print-width">
                    <option value="current">{{ copy.currentColumnWidth }}</option>
                    <option value="adaptive">{{ copy.adaptiveWidth }}</option>
                  </select>
                </label>
              </div>
              <section class="app-data-table__form-section">
                <div class="app-data-table__form-section-heading">
                  <strong>{{ copy.selectFields }}</strong>
                  <small>{{ copy.printFieldsHint }}</small>
                </div>
                <fieldset
                  class="app-data-table__export-fields app-data-table__field-picker app-data-table__field-picker--single-column"
                >
                  <legend class="app-data-table__sr-only">{{ copy.selectFields }}</legend>
                  <label v-for="column in printableColumns" :key="column.field">
                    <input
                      v-model="printFields"
                      type="checkbox"
                      :value="column.field"
                      :data-testid="`app-data-table-print-field-${column.field}`"
                    />
                    <span>{{ column.title }}</span>
                  </label>
                </fieldset>
              </section>
              <div
                class="app-data-table__panel-footer app-data-table__dialog-actions app-data-table__form-footer"
              >
                <button
                  type="button"
                  data-testid="app-data-table-print-cancel"
                  @click="printOpen = false"
                >
                  {{ copy.cancel }}
                </button>
                <button type="button" data-testid="app-data-table-print-confirm" @click="printData">
                  {{ copy.printData }}
                </button>
              </div>
            </div>
          </div>
        </div>
        <div class="app-data-table__toolbar-right" role="group" :aria-label="copy.auxiliaryTools">
          <button
            type="button"
            class="app-data-table__icon-button"
            data-testid="app-data-table-clear"
            :aria-label="copy.clearQuery"
            :title="copy.clearQuery"
            @click="clearConditions"
          >
            <Brush aria-hidden="true" />
          </button>
          <button
            type="button"
            class="app-data-table__icon-button"
            data-testid="app-data-table-refresh"
            :aria-label="copy.refresh"
            :title="copy.refresh"
            @click="reload"
          >
            <Refresh aria-hidden="true" />
          </button>
          <button
            type="button"
            class="app-data-table__icon-button"
            data-testid="app-data-table-fullscreen"
            :aria-label="tableFullscreen ? copy.exitFullscreen : copy.fullscreen"
            :title="tableFullscreen ? copy.exitFullscreen : copy.fullscreen"
            @click="tableFullscreen = !tableFullscreen"
          >
            <FullScreen aria-hidden="true" />
          </button>
          <button
            type="button"
            class="app-data-table__icon-button"
            data-testid="app-data-table-column-settings"
            ref="columnSettingsTrigger"
            :aria-label="copy.columnSettings"
            :title="copy.columnSettings"
            @click="openNativeCustom($event)"
          >
            <Setting aria-hidden="true" />
          </button>
          <div class="app-data-table__toolbar-popover">
            <button
              ref="tableSettingsTrigger"
              type="button"
              class="app-data-table__icon-button"
              data-testid="app-data-table-table-settings"
              :aria-label="copy.rowSettings"
              :title="copy.rowSettings"
              :aria-expanded="settingsOpen"
              @click="toggleTableSettings"
            >
              <Operation aria-hidden="true" />
            </button>
            <div
              v-if="settingsOpen"
              ref="tableSettingsPanel"
              class="app-data-table__popover app-data-table__panel app-data-table__settings"
              data-testid="app-data-table-settings"
              data-font-size="12px"
              @mousedown.stop
              @click.stop
            >
              <div class="app-data-table__dialog-header">
                <strong>{{ copy.rowSettings }}</strong>
                <span>{{ copy.rowSettingsHint }}</span>
              </div>
              <div class="app-data-table__settings-options">
                <label
                  ><span>{{ copy.showIndex }}</span
                  ><input v-model="preferences.showIndex" type="checkbox" @change="persist"
                /></label>
                <label
                  ><span>{{ copy.showBorder }}</span
                  ><input v-model="preferences.border" type="checkbox" @change="persist"
                /></label>
              </div>
              <div class="app-data-table__settings-section">
                <span>{{ copy.density }}</span>
                <div class="app-data-table__densities" role="group" :aria-label="copy.density">
                  <button
                    v-for="density in ['comfortable', 'medium', 'compact'] as const"
                    :key="density"
                    type="button"
                    :data-testid="`app-data-table-density-${density}`"
                    :class="{ 'is-active': preferences.density === density }"
                    @click="setDensity(density)"
                  >
                    {{
                      density === 'comfortable'
                        ? copy.defaultDensity
                        : density === 'medium'
                          ? copy.mediumDensity
                          : copy.compactDensity
                    }}
                  </button>
                </div>
              </div>
              <div class="app-data-table__panel-footer">
                <button
                  type="button"
                  data-testid="app-data-table-settings-close"
                  @click="settingsOpen = false"
                >
                  {{ copy.done }}
                </button>
              </div>
            </div>
          </div>
        </div>
      </div>

      <VxeToolbar
        ref="toolbarRef"
        class="app-data-table__native-toolbar"
        custom
        :buttons="[]"
        :tools="[]"
      />

      <div class="app-data-table__surface" :aria-busy="tableLoading">
        <VxeTable
          ref="tableRef"
          v-bind="tableBindings"
          @sort-change="onSortChange"
          @checkbox-change="onSelectionChange"
          @checkbox-all="onSelectionChange"
          @radio-change="onSelectionChange"
          @resizable-change="onColumnResizeChange"
        >
          <VxeColumn v-if="mode === 'detail'" type="expand" width="60" :title="copy.detail">
            <template #content="{ row }"><slot name="detail" :row="row" /></template>
          </VxeColumn>
          <VxeColumn
            v-if="preferences.showIndex"
            type="seq"
            title="#"
            width="60"
            header-class-name="app-data-table__header-structural"
          />
          <VxeColumn
            v-if="selection === 'single'"
            type="radio"
            :title="copy.select"
            width="64"
            header-class-name="app-data-table__header-structural"
          />
          <VxeColumn
            v-if="selection === 'multiple'"
            type="checkbox"
            :title="copy.select"
            width="64"
            header-class-name="app-data-table__header-structural"
          />
          <VxeColumn
            v-for="column in visibleColumns"
            :key="column.field"
            v-bind="{
              field: column.field,
              title: column.title,
              visible: column.visible !== false,
              sortable: column.sortable !== false,
              ...(preferences.widths[column.field] === undefined && column.width === undefined
                ? {}
                : { width: preferences.widths[column.field] ?? column.width }),
              ...(column.minWidth === undefined ? {} : { minWidth: column.minWidth }),
              ...(column.sortable === undefined ? {} : { sortable: column.sortable }),
              ...(column.fixed === undefined ? {} : { fixed: column.fixed }),
              headerClassName: nativeHeaderFieldClass(column.field),
            }"
          >
            <template #header>
              <div
                class="app-data-table__header-title"
                :class="{ 'is-sortable': column.sortable !== false }"
                @click="
                  column.sortable === false ? undefined : cycleHeaderSort($event, column.field)
                "
              >
                {{ column.title }}
              </div>
            </template>
            <template #default="{ row }">
              <template
                v-if="
                  isGroupRow(row as AppDataTableRenderRow<T>) &&
                  column.field === visibleColumns[0]?.field
                "
              >
                <span
                  class="app-data-table__group-label"
                  :style="{
                    paddingLeft: `${(row as AppDataTableGroupRow).__appDataTableGroupLevel * 18}px`,
                  }"
                >
                  {{ (row as AppDataTableGroupRow).__appDataTableGroupLabel }}
                </span>
              </template>
              <slot v-else :name="`cell-${column.field}`" :row="row" :column="column">{{
                cellValue(row, column.field)
              }}</slot>
            </template>
          </VxeColumn>
          <VxeColumn
            v-if="$slots.actions"
            field="__actions"
            :title="copy.actions"
            fixed="right"
            :width="actionColumnWidth"
            :min-width="120"
            :show-overflow="false"
            header-class-name="app-data-table__actions-column-header"
            class-name="app-data-table__actions-column"
          >
            <template #default="{ row }"
              ><slot
                v-if="!isGroupRow(row as AppDataTableRenderRow<T>)"
                name="actions"
                :row="row"
                :available-width="actionColumnWidth"
            /></template>
          </VxeColumn>
        </VxeTable>
        <div
          v-if="tableLoading"
          class="app-data-table__loading"
          data-testid="app-data-table-loading"
          role="status"
          aria-live="polite"
        >
          <span class="app-data-table__loading-spinner" aria-hidden="true" />
          <span>{{ copy.loading }}</span>
        </div>
      </div>

      <div class="app-data-table__footer">
        <div v-if="selection !== 'none'" class="app-data-table__selection-summary">
          <span>{{ copy.selectionSummary.replace('{count}', String(selectedRows.length)) }}</span>
          <button
            type="button"
            data-testid="app-data-table-clear-selection"
            :disabled="selectedRows.length === 0"
            @click="clearSelection"
          >
            {{ copy.clearSelection }}
          </button>
        </div>
        <el-pagination
          class="app-data-table__pagination"
          :current-page="currentPage"
          :page-size="currentPageSize"
          :page-sizes="[10, 25, 50, 100, 150, 200]"
          :pager-count="5"
          :total="tableTotal"
          layout="total, sizes, prev, pager, next, jumper"
          @current-change="onPageChange"
          @size-change="onPageSizeChange"
        />
      </div>
    </div>
  </section>
</template>

<style scoped>
.app-data-table {
  position: relative;
  display: flex;
  min-width: 0;
  flex-direction: column;
  gap: var(--ip-space-1);
}
.app-data-table--compact-stack {
  gap: var(--ip-space-1);
}
.app-data-table--fullscreen {
  position: fixed;
  z-index: 1200;
  inset: 0;
  padding: var(--ip-space-4);
  overflow: auto;
  background: var(--ip-color-bg-page);
}
.app-data-table__card {
  position: relative;
  min-width: 0;
  padding: 0;
  background: var(--ip-color-bg-container);
  border: 0;
  border-radius: 0;
  box-shadow: none;
}
.app-data-table__surface {
  position: relative;
  flex: 1 1 auto;
  min-height: 0;
  min-width: 0;
  max-width: 100%;
  margin: 0 var(--ip-space-5);
  border-top: 1px solid var(--ip-color-border);
  overflow: hidden;
}
.app-data-table__loading {
  position: absolute;
  z-index: 2;
  inset: 0;
  display: flex;
  align-items: center;
  justify-content: center;
  gap: var(--ip-space-2);
  color: var(--ip-color-text-secondary);
  background: color-mix(in srgb, var(--ip-color-bg-container) 72%, transparent);
  pointer-events: all;
}
.app-data-table__loading-spinner {
  width: 18px;
  height: 18px;
  border: 2px solid var(--ip-color-border);
  border-top-color: var(--ip-color-primary);
  border-radius: 50%;
  animation: app-data-table-loading-spin 0.8s linear infinite;
}
@keyframes app-data-table-loading-spin {
  to {
    transform: rotate(360deg);
  }
}
.app-data-table__toolbar,
.app-data-table__toolbar-left,
.app-data-table__toolbar-right,
.app-data-table__toolbar-popover,
.app-data-table__toolbar-actions,
.app-data-table__settings,
.app-data-table__column-setting,
.app-data-table__densities,
.app-data-table__footer {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: var(--ip-space-2);
}
.app-data-table__toolbar {
  justify-content: space-between;
  min-height: var(--ip-density-control-height);
  box-sizing: border-box;
  min-height: 56px;
  padding: 12px var(--ip-space-5);
}
.app-data-table__toolbar-left,
.app-data-table__toolbar-right {
  display: flex;
  align-items: center;
  gap: var(--ip-space-2);
}
.app-data-table__toolbar-title {
  margin-right: var(--ip-space-2);
  color: var(--ip-color-text-primary);
  font-size: var(--ip-font-size-sm);
  font-weight: 600;
  white-space: nowrap;
}
.app-data-table__toolbar-label {
  white-space: nowrap;
}
.app-data-table__business-actions {
  margin-bottom: 10px;
  padding-bottom: 10px;
  border-bottom: 1px solid var(--ip-color-border);
}
.app-data-table__toolbar-popover {
  position: relative;
}
.app-data-table__toolbar > button,
.app-data-table__toolbar-left > button,
.app-data-table__toolbar-left .app-data-table__toolbar-popover > button,
.app-data-table__toolbar-left .app-data-table__export > button,
.app-data-table__toolbar-right > button {
  min-height: 29px;
  height: 29px;
  padding: 0 var(--ip-space-2);
  color: var(--ip-color-text-secondary);
  background: transparent;
  border: 0;
  border-radius: var(--ip-radius-sm);
  cursor: pointer;
  font-size: var(--ip-font-size-xs);
}
.app-data-table__popover button,
.app-data-table__settings button,
.app-data-table__export-formats button {
  min-height: var(--ip-density-control-height);
  padding: 0 var(--ip-space-2);
  color: var(--ip-color-text-secondary);
  background: var(--ip-color-bg-container);
  border: 1px solid var(--ip-color-border);
  border-radius: var(--ip-radius-sm);
  cursor: pointer;
}
.app-data-table__icon-button {
  display: inline-flex;
  width: var(--ip-density-control-height);
  height: var(--ip-density-control-height);
  align-items: center;
  justify-content: center;
  padding: 0;
  border-radius: 50%;
  cursor: pointer;
  transition:
    color 120ms ease,
    background-color 120ms ease,
    border-color 120ms ease,
    box-shadow 120ms ease;
}
.app-data-table__toolbar .app-data-table__icon-button {
  min-height: 29px;
  height: 29px;
  width: auto;
  min-width: 0;
  padding: 0 var(--ip-space-2);
  border-radius: var(--ip-radius-sm);
}
.app-data-table__toolbar-right .app-data-table__icon-button {
  width: 28px;
  height: 28px;
  min-height: 28px;
  padding: 0;
}
.app-data-table__icon-button:hover {
  color: var(--ip-color-primary);
  background: var(--ip-color-bg-muted);
}
.app-data-table__icon-button:focus-visible {
  outline: 2px solid var(--ip-color-primary);
  outline-offset: 2px;
}
.app-data-table__icon-button.is-active {
  color: var(--ip-color-primary);
  background: color-mix(in srgb, var(--ip-color-primary) 12%, var(--ip-color-bg-container));
  border-color: transparent;
  box-shadow: none;
}
.app-data-table__icon-button :deep(svg),
.app-data-table__icon-button :deep(.el-icon) {
  width: 16px;
  height: 16px;
  flex: 0 0 16px;
  color: currentColor;
  fill: currentColor;
  stroke: currentColor;
}
.app-data-table__toolbar-actions--styled :deep(.el-button) {
  min-height: var(--ip-density-control-height);
  padding: 0 var(--ip-space-3);
  border-radius: var(--ip-radius-sm);
  font: inherit;
  line-height: 1.2;
}
.app-data-table__toolbar-actions--styled :deep(button:not(.el-button)) {
  min-height: var(--ip-density-control-height);
  padding: 0 var(--ip-space-3);
  color: var(--ip-color-text-primary);
  font: inherit;
  line-height: 1.2;
  background: var(--ip-color-bg-container);
  border: 1px solid var(--ip-color-border);
  border-radius: var(--ip-radius-sm);
  cursor: pointer;
}
.app-data-table__quick-search {
  display: inline-flex;
  align-items: center;
  gap: var(--ip-space-1);
  width: 180px;
  box-sizing: border-box;
  min-height: var(--ip-density-control-height);
  padding: 0 var(--ip-space-2);
  color: var(--ip-color-text-primary);
  background: var(--ip-color-bg-container);
  border: 1px solid var(--ip-color-border);
  border-radius: var(--ip-radius-sm);
  font-size: var(--ip-font-size-sm);
  line-height: 1;
}
.app-data-table__quick-search :deep(svg) {
  width: 16px;
  height: 16px;
  flex: 0 0 16px;
  color: var(--ip-color-text-tertiary);
}
.app-data-table__quick-search input {
  min-width: 0;
  flex: 1;
  padding: 0;
  color: inherit;
  background: transparent;
  border: 0;
  outline: 0;
  font: inherit;
  font-size: var(--ip-font-size-sm);
}
.app-data-table__quick-search.is-disabled {
  color: var(--ip-color-text-secondary);
  background: color-mix(in srgb, var(--ip-color-bg-muted) 48%, var(--ip-color-bg-container));
  border-color: var(--ip-color-border);
  opacity: 0.82;
}
.app-data-table__quick-search.is-disabled input {
  color: var(--ip-color-text-secondary);
  cursor: not-allowed;
  opacity: 0.9;
}
.app-data-table__popover {
  position: absolute;
  z-index: 20;
  top: calc(100% + var(--ip-space-2));
  left: 0;
  display: grid;
  min-width: 180px;
  gap: var(--ip-space-1);
  padding: var(--ip-space-2);
  color: var(--ip-color-text-primary);
  background: var(--ip-color-bg-container);
  border: 1px solid var(--ip-color-border);
  border-radius: var(--ip-radius-sm);
  box-shadow: var(--ip-shadow-md);
}
.app-data-table__popover button {
  text-align: left;
}
.app-data-table__popover button span {
  display: inline-block;
  min-width: 18px;
  color: var(--ip-color-primary);
}
.app-data-table__panel {
  gap: var(--ip-space-2);
  padding: var(--ip-space-3);
  border-radius: var(--ip-radius-md);
  box-shadow: var(--ip-shadow-md);
}
.app-data-table__panel-list {
  display: grid;
  max-height: 224px;
  gap: var(--ip-space-1);
  overflow: auto;
}
.app-data-table__panel-list > button {
  display: flex;
  width: 100%;
  align-items: center;
  justify-content: space-between;
  gap: var(--ip-space-2);
  text-align: left;
}
.app-data-table__panel-list > button.is-active {
  color: var(--ip-color-primary);
  background: color-mix(in srgb, var(--ip-color-primary) 8%, var(--ip-color-bg-container));
}

.app-data-table__sort-option {
  display: flex;
  min-height: 34px;
  align-items: center;
  justify-content: space-between;
  gap: var(--ip-space-2);
  padding: 0 8px;
  color: var(--ip-color-text-primary);
  border: 1px solid transparent;
  border-radius: var(--ip-radius-sm);
}

.app-data-table__sort-option.is-active {
  background: color-mix(in srgb, var(--ip-color-primary) 9%, var(--ip-color-bg-container));
  border-color: color-mix(in srgb, var(--ip-color-primary) 28%, var(--ip-color-border));
}

.app-data-table__sort-label {
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.app-data-table__sort-directions {
  display: inline-grid;
  flex: 0 0 auto;
  grid-template-columns: repeat(2, auto);
  gap: 4px;
}

.app-data-table__sort-directions button {
  min-height: 26px;
  padding: 0 6px;
  color: var(--ip-color-text-secondary);
  background: var(--ip-color-bg-container);
  border: 1px solid var(--ip-color-border);
  border-radius: var(--ip-radius-sm);
  cursor: pointer;
  font: inherit;
  font-size: var(--ip-font-size-xs);
  line-height: 1;
}

.app-data-table__sort-directions button:hover,
.app-data-table__sort-directions button.is-active {
  color: var(--ip-color-primary);
  border-color: var(--ip-color-primary);
}

.app-data-table__sort-directions button.is-active {
  background: color-mix(in srgb, var(--ip-color-primary) 10%, var(--ip-color-bg-container));
}
.app-data-table__panel-footer {
  display: flex;
  justify-content: flex-end;
  padding-top: var(--ip-space-2);
  border-top: 1px solid var(--ip-color-border);
}
.app-data-table__native-toolbar {
  display: none;
}
.app-data-table__native-button {
  min-height: var(--ip-density-control-height);
  padding: 0 var(--ip-space-2);
  color: var(--ip-color-text-secondary);
  background: var(--ip-color-bg-container);
  border: 1px solid var(--ip-color-border);
  border-radius: var(--ip-radius-sm);
  cursor: pointer;
}
.app-data-table :deep(.vxe-table-custom-wrapper) {
  width: 304px;
  color: var(--ip-color-text-primary);
  background: var(--ip-color-bg-container);
  border-color: var(--ip-color-border);
  border-radius: var(--ip-radius-md);
  box-shadow: var(--ip-shadow-md);
  overflow: hidden;
}
.app-data-table :deep(.vxe-table-custom--header) {
  height: 40px;
  color: var(--ip-color-text-primary);
  background: var(--ip-color-bg-muted);
  border-bottom-color: var(--ip-color-border);
}
.app-data-table :deep(.vxe-table-custom--panel-list > li) {
  width: 100%;
  min-width: 0;
  max-width: none;
  box-sizing: border-box;
  padding: 0 var(--ip-space-3);
}
.app-data-table :deep(.vxe-table-custom--body .vxe-table-custom--option) {
  min-height: 36px;
  border-bottom: 1px solid var(--ip-color-border-secondary, var(--ip-color-border));
}
.app-data-table :deep(.vxe-table-custom--body .vxe-table-custom--option:last-child) {
  border-bottom: 0;
}
.app-data-table :deep(.vxe-table-custom--body .vxe-table-custom--option:hover) {
  background: var(--ip-color-bg-muted);
}
.app-data-table :deep(.vxe-table-custom--sort-btn) {
  padding: 0 var(--ip-space-2) 0 0;
  color: var(--ip-color-text-tertiary);
}
.app-data-table :deep(.vxe-table-custom--fixed-option) {
  gap: 2px;
  padding-left: var(--ip-space-2);
}
.app-data-table :deep(.vxe-table-custom--fixed-option .app-data-table__native-button) {
  width: 28px;
  min-height: 28px;
  padding: 0;
  color: var(--ip-color-text-secondary);
  background: transparent;
  border: 0;
  border-radius: var(--ip-radius-sm);
}
.app-data-table :deep(.vxe-table-custom--fixed-option .app-data-table__native-button:hover) {
  color: var(--ip-color-primary);
  background: var(--ip-color-primary-bg);
}
.app-data-table :deep(.vxe-table-custom--footer-buttons) {
  gap: var(--ip-space-2);
  padding: var(--ip-space-2) var(--ip-space-3);
  background: var(--ip-color-bg-container);
  border-top-color: var(--ip-color-border);
}
.app-data-table :deep(.vxe-table-custom--footer-buttons .app-data-table__native-button) {
  height: 32px;
  min-height: 32px;
  padding: 0 var(--ip-space-3);
  border-radius: var(--ip-radius-sm);
}
.app-data-table :deep(.vxe-table-custom--footer-buttons .app-data-table__native-button:last-child) {
  color: var(--ip-color-text-inverse, #fff);
  background: var(--ip-color-primary);
  border-color: var(--ip-color-primary);
}
.app-data-table :deep(.vxe-table-custom--footer-buttons .app-data-table__native-button:disabled) {
  cursor: not-allowed;
  opacity: 0.5;
}
.app-data-table__export {
  position: relative;
  display: inline-flex;
}
.app-data-table__export-menu {
  display: grid;
  gap: var(--ip-space-3);
}
.app-data-table__dialog {
  width: min(440px, calc(100vw - 2 * var(--ip-space-4)));
  max-height: calc(100vh - 96px);
  overflow: auto;
  padding: var(--ip-space-3);
  background: var(--ip-color-bg-container);
  border: 1px solid var(--ip-color-border);
  border-radius: var(--ip-radius-md);
  box-shadow: var(--ip-shadow-md);
}
.app-data-table__export-menu {
  width: min(420px, calc(100vw - 2 * var(--ip-space-4)));
  max-height: min(70vh, 620px);
  overflow-y: auto;
  overscroll-behavior: contain;
}
.app-data-table__print-dialog {
  right: 0;
  left: auto;
  width: min(320px, calc(100vw - 2 * var(--ip-space-4)));
}
.app-data-table__dialog-header {
  display: grid;
  gap: var(--ip-space-1);
}
.app-data-table__dialog-header span {
  color: var(--ip-color-text-secondary);
  font-size: var(--ip-font-size-sm);
}
.app-data-table__dialog-actions {
  display: flex;
  justify-content: flex-end;
  gap: var(--ip-space-2);
}
.app-data-table__export-group {
  display: grid;
  gap: var(--ip-space-2);
}
.app-data-table__export-group + .app-data-table__export-group {
  padding-top: var(--ip-space-3);
  border-top: 1px solid var(--ip-color-border);
}
.app-data-table__export-group small {
  color: var(--ip-color-text-secondary);
  font-size: var(--ip-font-size-sm);
  line-height: 1.4;
}
.app-data-table__export-formats {
  display: flex;
  flex-wrap: wrap;
  gap: var(--ip-space-2);
}
.app-data-table button.is-active {
  color: var(--ip-color-primary);
  border-color: var(--ip-color-primary);
}
.app-data-table__header-filter-row {
  height: 38px;
  background: var(--ip-color-bg-container);
}
.app-data-table :deep(.app-data-table__header-filter-row th) {
  box-sizing: border-box;
  padding: 4px;
  border-top: 1px solid var(--ip-color-border);
  border-right: 1px solid var(--ip-color-border);
}
.app-data-table :deep(.app-data-table__header-filter-cell input),
.app-data-table :deep(.app-data-table__header-filter-cell select) {
  box-sizing: border-box;
  width: 100%;
  min-width: 0;
  height: 28px;
  padding: 0 6px;
  color: var(--ip-color-text-primary);
  background: var(--ip-color-bg-container);
  border: 1px solid var(--ip-color-border);
  border-radius: var(--ip-radius-sm);
}
.app-data-table :deep(.app-data-table__date-range-control) {
  display: flex;
  width: 100%;
  min-width: 0;
  gap: 3px;
  overflow: hidden;
}
.app-data-table :deep(.app-data-table__date-range-control .el-date-editor--daterange) {
  width: 100%;
  min-width: 0;
  padding: 0 6px;
}
.app-data-table :deep(.app-data-table__date-range-control .el-range-input) {
  min-width: 0;
  width: 0;
  flex: 1 1 0;
}
.app-data-table :deep(.app-data-table__date-range-control .el-range-separator) {
  flex: 0 0 auto;
  padding: 0 2px;
}
.app-data-table__header-filter-row input,
.app-data-table__header-filter-row select,
.app-data-table__export-quantity select,
.app-data-table__dialog > label > input,
.app-data-table__dialog > label > select {
  box-sizing: border-box;
  width: 100%;
  min-width: 0;
  min-height: var(--ip-density-control-height);
  padding: 0 var(--ip-space-2);
  color: var(--ip-color-text-primary);
  background: var(--ip-color-bg-container);
  border: 1px solid var(--ip-color-border);
  border-radius: var(--ip-radius-sm);
}
.app-data-table__export-fields {
  display: flex;
  flex-wrap: wrap;
  gap: var(--ip-space-2);
  margin: 0;
  padding: var(--ip-space-2);
  border: 1px solid var(--ip-color-border);
}
.app-data-table__export-fields legend {
  padding: 0 var(--ip-space-1);
  color: var(--ip-color-text-secondary);
  font-size: var(--ip-font-size-sm);
}
.app-data-table__export-fields label {
  display: inline-flex;
  align-items: center;
  gap: var(--ip-space-1);
}
.app-data-table__export-params {
  color: var(--ip-color-text-secondary);
  font-size: var(--ip-font-size-sm);
}
.app-data-table__header-title {
  display: block;
  min-width: 0;
  max-width: 100%;
  flex: 1 1 auto;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.app-data-table :deep(.vxe-table) {
  font-size: var(--ip-font-size-xs);
}

.app-data-table :deep(.vxe-table--header-wrapper .vxe-header--row),
.app-data-table :deep(.vxe-table--header-wrapper .vxe-header--column),
.app-data-table :deep(.vxe-table--header-wrapper .vxe-cell) {
  height: 38px !important;
}

.app-data-table :deep(.vxe-table--header-wrapper .vxe-cell) {
  min-height: 38px !important;
}

.app-data-table :deep(.vxe-table--header-wrapper .vxe-header--column) {
  color: var(--ip-color-text-secondary);
  font-size: var(--ip-font-size-xs);
  font-weight: 500;
  border-right-color: transparent;
}

.app-data-table :deep(.vxe-table--body-wrapper .vxe-body--column) {
  color: var(--ip-color-text-secondary);
  font-size: var(--ip-font-size-xs);
  border-right-color: transparent;
}
.app-data-table__header-title.is-sortable {
  cursor: pointer;
}
.app-data-table :deep(.vxe-header--column .vxe-cell--wrapper) {
  display: flex;
  min-width: 0;
  max-width: 100%;
  align-items: center;
  overflow: hidden;
}
.app-data-table :deep(.vxe-header--column .vxe-cell--sort-vertical-layout) {
  display: inline-flex;
  flex: 0 0 auto;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  height: 1.15em;
  margin-left: 4px;
  color: var(--ip-color-text-tertiary);
  vertical-align: middle;
}
.app-data-table :deep(.vxe-header--column .vxe-cell--sort-vertical-layout .vxe-sort--asc-btn),
.app-data-table :deep(.vxe-header--column .vxe-cell--sort-vertical-layout .vxe-sort--desc-btn) {
  width: 0.62em;
  height: 0.43em;
  color: var(--ip-color-text-tertiary);
  opacity: 0.68;
}
.app-data-table :deep(.vxe-header--column .vxe-cell--sort-vertical-layout .vxe-sort--asc-btn:hover),
.app-data-table
  :deep(.vxe-header--column .vxe-cell--sort-vertical-layout .vxe-sort--desc-btn:hover) {
  color: var(--ip-color-text-secondary);
}
.app-data-table :deep(.vxe-header--column .vxe-cell--sort-vertical-layout .sort--active) {
  color: var(--ip-color-primary);
  opacity: 1;
}
.app-data-table__settings {
  right: 0;
  left: auto;
  min-width: 220px;
}
.app-data-table__column-setting {
  padding: 0 var(--ip-space-2);
}
.app-data-table__footer {
  justify-content: space-between;
  min-height: 56px;
  box-sizing: border-box;
  padding: 0 var(--ip-space-5);
}
.app-data-table__pagination {
  margin-left: auto;
}
.app-data-table__selection-summary {
  display: inline-flex;
  align-items: center;
  gap: var(--ip-space-2);
  color: var(--ip-color-text-secondary);
  font-size: var(--ip-font-size-sm);
}
.app-data-table__selection-summary button {
  min-height: var(--ip-density-control-height);
  padding: 0 var(--ip-space-2);
  color: var(--ip-color-text-secondary);
  background: transparent;
  border: 1px solid var(--ip-color-border);
  border-radius: var(--ip-radius-sm);
  cursor: pointer;
}
.app-data-table__selection-summary button:disabled {
  cursor: not-allowed;
  opacity: 0.55;
}
.app-data-table :deep(.app-data-table__group-row) {
  position: relative;
  color: var(--ip-color-text-primary);
  background: var(--ip-color-bg-muted);
  font-weight: 600;
}
.app-data-table :deep(.app-data-table__group-row .vxe-body--column) {
  background: var(--ip-color-bg-muted);
}
.app-data-table :deep(.app-data-table__group-row .col--checkbox),
.app-data-table :deep(.app-data-table__group-row .col--radio) {
  visibility: hidden;
  pointer-events: none;
}
.app-data-table :deep(.app-data-table__actions-column .vxe-cell) {
  overflow: visible;
}
.app-data-table__group-label {
  display: block;
  color: var(--ip-color-text-primary);
}
.app-data-table--comfortable :deep(.vxe-body--row) {
  height: 44px;
}
.app-data-table--medium :deep(.vxe-body--row) {
  height: 38px;
}
.app-data-table--compact :deep(.vxe-body--row) {
  height: 32px;
}

/* Shared panel language: every table utility uses the same shell, controls and footer. */
.app-data-table__panel {
  box-sizing: border-box;
  align-content: start;
  gap: var(--ip-space-2);
  padding: var(--ip-space-3);
  border: 1px solid var(--ip-color-border);
  border-radius: var(--ip-radius-md);
  box-shadow: var(--ip-shadow-md);
  font-size: 12px;
  line-height: 1.4;
}

.app-data-table__panel button,
.app-data-table__panel input,
.app-data-table__panel select {
  font-family: inherit;
  font-size: inherit;
}

.app-data-table__sort-panel,
.app-data-table__group-panel {
  width: min(236px, calc(100vw - 2 * var(--ip-space-4)));
  min-width: min(236px, calc(100vw - 2 * var(--ip-space-4)));
  grid-template-rows: auto minmax(0, 1fr) auto;
  overflow: hidden;
}

.app-data-table__sort-panel .app-data-table__panel-list,
.app-data-table__group-panel .app-data-table__panel-list {
  min-height: 0;
  max-height: none;
  overflow-y: auto;
}

.app-data-table__export-menu {
  width: min(360px, calc(100vw - 2 * var(--ip-space-4)));
  max-height: min(60vh, 520px);
  overflow-y: auto;
}

.app-data-table__print-dialog {
  width: min(320px, calc(100vw - 2 * var(--ip-space-4)));
}

.app-data-table__settings {
  width: min(280px, calc(100vw - 2 * var(--ip-space-4)));
}

.app-data-table__dialog-header {
  gap: 3px;
  padding-bottom: var(--ip-space-2);
  border-bottom: 1px solid var(--ip-color-border);
}

.app-data-table__dialog-header strong {
  color: var(--ip-color-text-primary);
  font-size: var(--ip-font-size-md, 14px);
  font-weight: 650;
  line-height: 1.35;
}

.app-data-table__dialog-header span,
.app-data-table__export-group small {
  color: var(--ip-color-text-secondary);
  font-size: var(--ip-font-size-sm);
  line-height: 1.45;
}

.app-data-table__panel-list {
  gap: 4px;
  max-height: 224px;
}

.app-data-table__panel-list > button {
  min-height: 34px;
  padding: 0 10px;
  color: var(--ip-color-text-primary);
  background: transparent;
  border: 1px solid transparent;
  border-radius: var(--ip-radius-sm);
}

.app-data-table__panel-list > button:hover,
.app-data-table__panel-list > button:focus-visible {
  background: var(--ip-color-bg-muted);
  border-color: var(--ip-color-border);
}

.app-data-table__panel-list > button.is-active {
  color: var(--ip-color-primary);
  background: color-mix(in srgb, var(--ip-color-primary) 9%, var(--ip-color-bg-container));
  border-color: color-mix(in srgb, var(--ip-color-primary) 28%, var(--ip-color-border));
}

.app-data-table__group-panel .app-data-table__group-option {
  display: flex;
  width: 100%;
  align-items: center;
  justify-content: flex-start;
}

.app-data-table__group-order {
  display: inline-flex;
  width: 18px;
  min-width: 18px;
  height: 18px;
  align-items: center;
  justify-content: center;
  color: var(--ip-color-text-tertiary);
  font-size: var(--ip-font-size-xs);
  line-height: 1;
}

.app-data-table__group-panel .app-data-table__group-label {
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.app-data-table__group-panel .is-active .app-data-table__group-order {
  color: var(--ip-color-primary);
  font-weight: 650;
}

.app-data-table__panel-footer,
.app-data-table__dialog-actions {
  display: flex;
  align-items: center;
  justify-content: flex-end;
  gap: var(--ip-space-2);
  margin-top: -2px;
  padding-top: var(--ip-space-3);
  border-top: 1px solid var(--ip-color-border);
}

.app-data-table__panel-footer button,
.app-data-table__dialog-actions button,
.app-data-table__export-group > button,
.app-data-table__export-formats button,
.app-data-table__densities button {
  box-sizing: border-box;
  min-height: 32px;
  padding: 0 var(--ip-space-3);
  color: var(--ip-color-text-primary);
  background: var(--ip-color-bg-container);
  border: 1px solid var(--ip-color-border);
  border-radius: var(--ip-radius-sm);
  cursor: pointer;
  font: inherit;
  line-height: 1.2;
}

.app-data-table__panel-footer button:hover,
.app-data-table__dialog-actions button:hover,
.app-data-table__export-group > button:hover,
.app-data-table__export-formats button:hover,
.app-data-table__densities button:hover {
  color: var(--ip-color-primary);
  border-color: var(--ip-color-primary);
}

.app-data-table__dialog-actions button:last-child,
.app-data-table__export-group > button:last-child {
  color: var(--ip-color-text-inverse, #fff);
  background: var(--ip-color-primary);
  border-color: var(--ip-color-primary);
}

.app-data-table__panel input:not([type='checkbox']):not([type='radio']),
.app-data-table__panel select {
  box-sizing: border-box;
  width: 100%;
  min-width: 0;
  height: 32px;
  min-height: 32px;
  padding: 0 10px;
  color: var(--ip-color-text-primary);
  background: var(--ip-color-bg-container);
  border: 1px solid var(--ip-color-border);
  border-radius: var(--ip-radius-sm);
  font: inherit;
  outline: 0;
}

.app-data-table__panel input:not([type='checkbox']):not([type='radio']):focus,
.app-data-table__panel select:focus {
  border-color: var(--ip-color-primary);
  box-shadow: 0 0 0 2px color-mix(in srgb, var(--ip-color-primary) 14%, transparent);
}

.app-data-table__export-menu > label,
.app-data-table__export-quantity {
  display: flex;
  min-width: 0;
  align-items: center;
  gap: var(--ip-space-2);
  color: var(--ip-color-text-secondary);
  line-height: 1.35;
}

.app-data-table__export-menu > label > input,
.app-data-table__export-menu > label > select,
.app-data-table__export-quantity > input,
.app-data-table__export-quantity > select {
  width: auto;
  flex: 1 1 0;
}

.app-data-table__export-quantity > input,
.app-data-table__export-quantity > select {
  min-width: 0;
}

.app-data-table__export-group {
  gap: var(--ip-space-2);
}

.app-data-table__export-group + .app-data-table__export-group,
.app-data-table__export-params {
  padding-top: var(--ip-space-3);
  border-top: 1px solid var(--ip-color-border);
}

.app-data-table__export-params {
  display: block;
  color: var(--ip-color-text-secondary);
  font-size: var(--ip-font-size-sm);
}

.app-data-table__export-formats {
  display: grid;
  grid-template-columns: repeat(4, minmax(0, 1fr));
  gap: var(--ip-space-1);
}

.app-data-table__export-formats button {
  padding: 0 var(--ip-space-2);
  text-align: center;
}

.app-data-table__export-fields {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 6px;
  margin: 0;
  padding: 8px;
  border: 1px solid var(--ip-color-border);
  border-radius: var(--ip-radius-sm);
}

.app-data-table__export-fields legend {
  grid-column: 1 / -1;
  padding: 0 4px;
}

.app-data-table__export-fields label {
  min-width: 0;
  min-height: 30px;
  box-sizing: border-box;
  padding: 0 8px;
  color: var(--ip-color-text-primary);
  background: var(--ip-color-bg-muted);
  border: 1px solid transparent;
  border-radius: var(--ip-radius-sm);
  white-space: nowrap;
}

.app-data-table__export-fields label:hover {
  border-color: var(--ip-color-border);
}

.app-data-table__export-fields input[type='checkbox'],
.app-data-table__settings-options input[type='checkbox'] {
  width: 15px;
  height: 15px;
  flex: 0 0 15px;
  accent-color: var(--ip-color-primary);
}

.app-data-table__settings-options {
  display: grid;
  gap: 6px;
}

.app-data-table__settings-options label {
  display: flex;
  min-height: 34px;
  align-items: center;
  justify-content: space-between;
  gap: var(--ip-space-2);
  box-sizing: border-box;
  padding: 0 10px;
  color: var(--ip-color-text-primary);
  background: var(--ip-color-bg-muted);
  border: 1px solid var(--ip-color-border);
  border-radius: var(--ip-radius-sm);
}

.app-data-table__settings-section {
  display: grid;
  gap: var(--ip-space-2);
  padding-top: var(--ip-space-3);
  color: var(--ip-color-text-secondary);
  border-top: 1px solid var(--ip-color-border);
}

.app-data-table__densities {
  display: grid;
  grid-template-columns: repeat(3, minmax(0, 1fr));
  gap: 6px;
}

.app-data-table__densities button {
  padding: 0 var(--ip-space-2);
  color: var(--ip-color-text-secondary);
  text-align: center;
}

.app-data-table__densities button.is-active {
  color: var(--ip-color-primary);
  background: color-mix(in srgb, var(--ip-color-primary) 9%, var(--ip-color-bg-container));
  border-color: var(--ip-color-primary);
}

/* Keep VXE's complete column controls, but bring its native panel into the shared shell. */
.app-data-table :deep(.vxe-table-custom-wrapper) {
  width: min(304px, calc(100vw - 2 * var(--ip-space-4)));
  max-width: calc(100vw - 2 * var(--ip-space-4));
  height: min(360px, calc(100vh - 96px));
  max-height: min(360px, calc(100vh - 96px)) !important;
  box-sizing: border-box;
  color: var(--ip-color-text-primary);
  background: var(--ip-color-bg-container);
  border: 1px solid var(--ip-color-border);
  border-radius: var(--ip-radius-md);
  box-shadow: var(--ip-shadow-md);
  overflow: hidden;
}

.app-data-table :deep(.vxe-table-custom--header) {
  display: grid;
  height: auto;
  min-height: 0;
  align-items: stretch;
  gap: 3px;
  padding: 12px 14px 8px;
  color: var(--ip-color-text-primary);
  background: var(--ip-color-bg-muted);
  border-bottom-color: var(--ip-color-border);
}

.app-data-table :deep(.vxe-table-custom--header::before) {
  content: attr(data-column-settings-title);
  font-size: var(--ip-font-size-md, 14px);
  font-weight: 650;
  line-height: 1.35;
}

.app-data-table :deep(.vxe-table-custom--header::after) {
  content: attr(data-column-settings-description);
  color: var(--ip-color-text-secondary);
  font-size: var(--ip-font-size-sm);
  font-weight: 400;
  line-height: 1.45;
}

.app-data-table :deep(.vxe-table-custom--header > .vxe-table-custom--panel-list) {
  width: 100%;
  margin-top: 5px;
  padding-top: 7px;
  border-top: 1px solid var(--ip-color-border);
}

.app-data-table :deep(.vxe-table-custom-simple--body-wrapper),
.app-data-table :deep(.vxe-table-custom--handle-wrapper) {
  min-height: 0;
  min-width: 0;
  height: 100%;
}

.app-data-table :deep(.vxe-table-custom--handle-wrapper) {
  flex: 1 1 auto;
}

.app-data-table :deep(.vxe-table-custom--panel-list) {
  width: 100%;
  min-width: 0;
}

.app-data-table :deep(.vxe-table-custom--panel-list > li) {
  width: 100%;
  min-width: 0;
  max-width: none;
  box-sizing: border-box;
  font-size: var(--ip-font-size-sm);
}

.app-data-table :deep(.vxe-table-custom--header .vxe-table-custom--option) {
  min-height: 32px;
  align-items: center;
  padding: 0 8px;
  background: var(--ip-color-bg-container);
  border: 1px solid var(--ip-color-border);
  border-radius: var(--ip-radius-sm);
}

.app-data-table :deep(.vxe-table-custom--body) {
  flex: 1 1 210px;
  height: 210px;
  min-height: 190px;
  max-height: 230px;
  padding: 8px 14px 0;
}

.app-data-table :deep(.vxe-table-custom--body .vxe-table-custom--panel-list) {
  display: grid;
  gap: 4px;
}

.app-data-table :deep(.vxe-table-custom--body .vxe-table-custom--option) {
  min-height: 32px;
  align-items: center;
  padding: 0 8px;
  border: 1px solid transparent;
  border-radius: var(--ip-radius-sm);
}

.app-data-table :deep(.vxe-table-custom--body .vxe-table-custom--option:hover) {
  background: var(--ip-color-bg-muted);
  border-color: var(--ip-color-border);
}

.app-data-table :deep(.vxe-table-custom--name-option) {
  min-width: 0;
}

.app-data-table :deep(.vxe-table-custom--checkbox-label) {
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.app-data-table :deep(.vxe-table-custom--footer) {
  padding: 0 14px 14px;
}

.app-data-table :deep(.vxe-table-custom--footer-buttons) {
  gap: var(--ip-space-2);
  padding: var(--ip-space-3) 0 0;
  background: transparent;
  border-top-color: var(--ip-color-border);
}

.app-data-table :deep(.vxe-table-custom--footer-buttons .app-data-table__native-button) {
  height: 32px;
  min-height: 32px;
  padding: 0 var(--ip-space-3);
  color: var(--ip-color-text-primary);
  background: var(--ip-color-bg-container);
  border: 1px solid var(--ip-color-border);
  border-radius: var(--ip-radius-sm);
  font: inherit;
}

.app-data-table :deep(.vxe-table-custom--footer-buttons .app-data-table__native-button:last-child) {
  color: var(--ip-color-text-inverse, #fff);
  background: var(--ip-color-primary);
  border-color: var(--ip-color-primary);
}

.app-data-table :deep(.app-data-table__header-filter-cell) {
  box-sizing: border-box;
  overflow: hidden;
  vertical-align: middle;
  background: var(--ip-color-bg-container);
  border-top: 0;
  border-right: 1px solid var(--ip-color-border);
  border-bottom: 1px solid var(--ip-color-border);
}

.app-data-table :deep(.app-data-table__header-filter-cell input),
.app-data-table :deep(.app-data-table__header-filter-cell select) {
  height: 26px;
  min-height: 26px;
  padding: 0 7px;
  font-size: 12px;
  background: var(--ip-color-bg-container);
  border-color: color-mix(in srgb, var(--ip-color-border) 84%, transparent);
  border-radius: 5px;
  box-shadow: none;
}

.app-data-table :deep(.app-data-table__date-range-control) {
  box-sizing: border-box;
  width: 100%;
  max-width: 100%;
  min-width: 0;
  height: 26px;
  overflow: hidden;
}

.app-data-table :deep(.app-data-table__date-range-control .el-date-editor--daterange) {
  --el-date-editor-width: 100%;
  display: flex;
  width: 100%;
  max-width: 100%;
  min-width: 0;
  height: 26px;
  box-sizing: border-box;
  flex: 1 1 auto;
  overflow: hidden;
  padding: 0 6px;
  background: var(--ip-color-bg-container);
  border-radius: 5px;
  box-shadow: 0 0 0 1px color-mix(in srgb, var(--ip-color-border) 84%, transparent) inset;
}

.app-data-table :deep(.app-data-table__date-range-control .el-range__icon) {
  width: 14px;
  min-width: 14px;
  flex: 0 0 14px;
  order: 5;
  margin-left: 4px;
  color: var(--ip-color-text-tertiary);
}

.app-data-table :deep(.app-data-table__date-range-control .el-range__close-icon) {
  display: none;
}

.app-data-table :deep(.app-data-table__date-range-control .el-range-input) {
  width: 0;
  min-width: 0;
  flex: 1 1 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.app-data-table :deep(.app-data-table__date-range-control .el-range-separator) {
  width: 16px;
  min-width: 16px;
  flex: 0 0 16px;
  padding: 0;
  color: var(--ip-color-text-secondary);
  font-size: 11px;
  text-align: center;
  white-space: nowrap;
}

/* Broad, shared form surface for export and print. */
.app-data-table__form-panel {
  width: min(var(--app-data-table-form-panel-width, 520px), calc(100vw - 24px));
  max-width: calc(100vw - 24px);
  height: min(540px, calc(100vh - 72px));
  min-height: min(420px, calc(100vh - 72px));
  max-height: min(560px, calc(100vh - 72px));
  overflow: hidden;
  padding: 14px 16px 12px;
  gap: 10px;
  font-size: 12px;
}

.app-data-table__form-panel--shared {
  box-sizing: border-box;
  width: min(var(--app-data-table-form-panel-width, 520px), calc(100vw - 24px));
  display: flex;
  flex-direction: column;
}

.app-data-table__form-panel--dense {
  font-size: 12px;
}

.app-data-table__popover--below-trigger {
  top: calc(100% + 8px);
  right: auto;
  left: 0;
  transform: none;
}

.app-data-table__form-grid {
  display: grid;
  gap: 8px;
}

.app-data-table__form-row {
  display: grid;
  min-width: 0;
  min-height: 30px;
  grid-template-columns: 80px minmax(0, 1fr);
  align-items: center;
  gap: 10px;
  margin: 0;
}

.app-data-table__form-row > span:first-child {
  color: var(--ip-color-text-secondary);
  text-align: right;
  white-space: nowrap;
}

.app-data-table__export-scope-field {
  display: grid;
  min-width: 0;
  gap: 4px;
}

.app-data-table__export-scope-control {
  display: flex;
  min-width: 0;
  align-items: center;
  gap: 6px;
}

.app-data-table__export-scope-control > select {
  min-width: 0;
  flex: 1 1 auto;
}

.app-data-table__form-panel .app-data-table__export-scope-control > input {
  width: 120px;
  flex: 0 0 120px;
}

.app-data-table__export-scope-hint {
  color: var(--ip-color-text-secondary);
  font-size: 12px;
  line-height: 1.3;
}

.app-data-table__form-row--static > div {
  display: flex;
  min-width: 0;
  min-height: 32px;
  align-items: center;
  padding: 0 10px;
  color: var(--ip-color-text-secondary);
  background: var(--ip-color-bg-muted);
  border: 1px solid var(--ip-color-border);
  border-radius: var(--ip-radius-sm);
}

.app-data-table__form-panel .form-row > input,
.app-data-table__form-panel .form-row > select,
.app-data-table__form-panel .app-data-table__form-row > input,
.app-data-table__form-panel .app-data-table__form-row > select {
  width: 100%;
  min-width: 0;
  height: 30px;
  min-height: 30px;
  padding: 0 8px;
  color: var(--ip-color-text-primary);
  font-size: 12px;
  background: var(--ip-color-bg-container);
  border: 1px solid var(--ip-color-border);
  border-radius: var(--ip-radius-sm);
}

.app-data-table__form-panel .app-data-table__form-row > input:focus,
.app-data-table__form-panel .app-data-table__form-row > select:focus {
  outline: none;
  border-color: var(--ip-color-primary);
  box-shadow: 0 0 0 2px color-mix(in srgb, var(--ip-color-primary) 12%, transparent);
}

.app-data-table__form-section {
  display: grid;
  min-width: 0;
  min-height: 0;
  flex: 1 1 auto;
  grid-template-rows: auto minmax(0, 1fr);
  gap: 6px;
  overflow: hidden;
  padding-top: 10px;
  border-top: 1px solid var(--ip-color-border);
}

.app-data-table__form-section-heading {
  display: flex;
  min-width: 0;
  align-items: baseline;
  justify-content: space-between;
  gap: 12px;
}

.app-data-table__form-section-heading strong {
  color: var(--ip-color-text-primary);
  font-size: 13px;
  font-weight: 650;
}

.app-data-table__form-section-heading small,
.app-data-table__form-section-label {
  color: var(--ip-color-text-secondary);
  font-size: 12px;
  line-height: 1.4;
}

.app-data-table__form-section-label {
  padding-top: 2px;
}

.app-data-table__form-panel .app-data-table__export-fields,
.app-data-table__form-panel .app-data-table__field-picker {
  display: grid;
  min-height: 0;
  max-height: none;
  align-content: start;
  grid-template-columns: minmax(0, 1fr);
  gap: 2px;
  overflow-y: auto;
  padding: 6px 8px;
}

.app-data-table__form-panel .app-data-table__export-fields label {
  display: flex;
  min-width: 0;
  min-height: 29px;
  align-items: center;
  gap: 6px;
  box-sizing: border-box;
  padding: 0 6px;
  color: var(--ip-color-text-primary);
  background: var(--ip-color-bg-muted);
  border: 1px solid transparent;
  border-radius: var(--ip-radius-sm);
  white-space: nowrap;
}

.app-data-table__form-panel .app-data-table__export-fields label span {
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
}

.app-data-table__form-panel .app-data-table__export-fields label:hover {
  border-color: var(--ip-color-border);
}

.app-data-table__form-panel .app-data-table__export-fields input[type='checkbox'] {
  width: 15px;
  height: 15px;
  flex: 0 0 15px;
  accent-color: var(--ip-color-primary);
}

.app-data-table__form-panel .app-data-table__export-quantity {
  grid-template-columns: 80px minmax(0, 220px) minmax(0, 1fr);
}

.app-data-table__form-panel .app-data-table__export-quantity > span {
  grid-column: 1;
}

.app-data-table__form-panel .app-data-table__export-quantity > select {
  grid-column: 2;
}

.app-data-table__form-panel .app-data-table__export-quantity > input {
  grid-column: 3;
}

.app-data-table__form-footer {
  min-height: 42px;
  margin-top: 0;
  padding-top: 10px;
}

.app-data-table__form-panel--shared .app-data-table__form-footer {
  margin-top: auto;
}

.app-data-table__sr-only {
  position: absolute;
  width: 1px;
  height: 1px;
  padding: 0;
  margin: -1px;
  overflow: hidden;
  clip: rect(0, 0, 0, 0);
  white-space: nowrap;
  border: 0;
}

/* Keep the native VXE column editor light and information-dense. */
.app-data-table :deep(.vxe-table-custom-wrapper) {
  position: fixed;
  width: 420px;
  max-width: calc(100vw - 24px);
  height: min(550px, calc(100vh - 240px));
  max-height: min(550px, calc(100vh - 240px)) !important;
  box-sizing: border-box;
  overflow: hidden;
  font-size: 12px;
}

.app-data-table :deep(.vxe-table-custom-wrapper.is--active) {
  display: flex;
  flex-direction: column;
}

.app-data-table :deep(.vxe-table-custom-wrapper:not(.is--active)) {
  display: none !important;
}

.app-data-table :deep(.vxe-table-custom--header) {
  display: flex;
  height: auto;
  min-height: 42px;
  flex-wrap: nowrap;
  align-items: center;
  gap: 8px;
  padding: 8px 10px;
  background: var(--ip-color-bg-container);
}

.app-data-table :deep(.vxe-table-custom--header::before),
.app-data-table :deep(.vxe-table-custom--header::after) {
  display: none;
  content: none;
}

.app-data-table :deep(.vxe-table-custom--header > .vxe-table-custom--panel-list) {
  display: block;
  width: auto;
  min-width: 0;
  margin: 0;
  padding: 0;
  border: 0;
}

.app-data-table :deep(.vxe-table-custom--header .vxe-table-custom--option) {
  min-height: 28px;
  padding: 0;
  border: 0;
  background: transparent;
}

.app-data-table :deep(.vxe-table-custom--header .vxe-checkbox--label),
.app-data-table :deep(.app-data-table__native-header-tools),
.app-data-table :deep(.app-data-table__native-toggle) {
  font-size: 12px;
}

.app-data-table :deep(.app-data-table__native-header-tools) {
  display: flex;
  min-width: 0;
  align-items: center;
  gap: 10px;
  margin-left: auto;
  white-space: nowrap;
}

.app-data-table :deep(.app-data-table__native-toggle) {
  display: inline-flex;
  align-items: center;
  gap: 4px;
  color: var(--ip-color-text-secondary);
  cursor: pointer;
}

.app-data-table :deep(.app-data-table__native-toggle input) {
  width: 14px;
  height: 14px;
  margin: 0;
  accent-color: var(--ip-color-primary);
}

.app-data-table :deep(.app-data-table__native-reset) {
  padding: 0;
  color: var(--ip-color-primary);
  background: transparent;
  border: 0;
  cursor: pointer;
  font: inherit;
}

.app-data-table :deep(.vxe-table-custom--body) {
  height: auto;
  min-height: 0;
  max-height: none;
  flex: 1 1 auto;
  overflow-x: hidden;
  overflow-y: auto;
  padding: 6px 10px 0;
}

.app-data-table :deep(.vxe-table-custom--body .vxe-table-custom--panel-list) {
  display: grid;
  grid-template-columns: minmax(0, 1fr);
  gap: 2px;
}

.app-data-table :deep(.vxe-table-custom--body .vxe-table-custom--option) {
  display: flex;
  width: 100%;
  min-width: 0;
  min-height: 30px;
  align-items: center;
  gap: 6px;
  box-sizing: border-box;
  padding: 0 6px;
  border: 1px solid transparent;
  border-radius: var(--ip-radius-sm);
}

.app-data-table :deep(.vxe-table-custom--body .vxe-table-custom--option:hover) {
  background: var(--ip-color-bg-muted);
  border-color: var(--ip-color-border);
}

.app-data-table :deep(.vxe-table-custom--body .vxe-table-custom--checkbox-option) {
  display: inline-flex;
  flex: 0 0 auto;
  align-items: center;
}

.app-data-table :deep(.vxe-table-custom--body .vxe-table-custom--name-option) {
  display: flex;
  min-width: 0;
  flex: 1 1 auto;
  align-items: center;
  gap: 6px;
}

.app-data-table :deep(.vxe-table-custom--body .vxe-table-custom--sort-option) {
  display: inline-flex;
  flex: 0 0 18px;
}

.app-data-table :deep(.vxe-table-custom--body .vxe-table-custom--checkbox-label) {
  min-width: 0;
  overflow: hidden;
  color: var(--ip-color-text-primary);
  font-size: 12px;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.app-data-table :deep(.vxe-table-custom--body .vxe-table-custom--fixed-option) {
  display: flex;
  flex: 0 0 auto;
  align-items: center;
  gap: 2px;
  margin-left: auto;
  padding-left: 6px;
}

.app-data-table :deep(.vxe-table-custom--body .vxe-table-custom--fixed-option button),
.app-data-table
  :deep(.vxe-table-custom--body .vxe-table-custom--fixed-option .app-data-table__native-button) {
  width: 24px;
  height: 28px;
  min-height: 28px;
  padding: 0;
  color: var(--ip-color-text-secondary);
  background: transparent;
  border: 0;
  border-radius: var(--ip-radius-sm);
}

.app-data-table :deep(.vxe-table-custom--body .vxe-table-custom--fixed-option button:hover),
.app-data-table :deep(.vxe-table-custom--body .vxe-table-custom--fixed-option button.is--active) {
  color: var(--ip-color-primary);
  background: var(--ip-color-primary-bg);
}

.app-data-table :deep(.vxe-table-custom-simple--body-wrapper),
.app-data-table :deep(.vxe-table-custom--handle-wrapper) {
  min-height: 0;
  height: auto;
}

@media (max-width: 720px) {
  .app-data-table :deep(.vxe-table-custom-wrapper) {
    width: calc(100vw - 24px);
  }
}
</style>
