<script setup lang="ts">
import {
  type ComponentPublicInstance,
  computed,
  nextTick,
  onBeforeUnmount,
  onMounted,
  ref,
  watch,
} from 'vue'
import { onBeforeRouteLeave, useRoute, useRouter } from 'vue-router'
import { returnFromWorkspace } from '@/workspace/returnFromWorkspace'
import { ElMessageBox } from 'element-plus'
import AppPage from '@/components/base/AppPage.vue'
import AppFormDrawer from '@/components/management/AppFormDrawer.vue'
import DashboardConstantEditor from '@/components/studio/dashboard/DashboardConstantEditor.vue'
import { PermissionGate } from '@/permissions'
import { getStudioApi, type Json, type StudioResource } from '@/api/studio'
import StudioEditorShell from '@/components/studio/shared/StudioEditorShell.vue'
import BindingEditor from '@/components/studio/shared/BindingEditor.vue'
import StudioFieldTree from '@/components/studio/shared/StudioFieldTree.vue'
import StudioFileField from '@/components/studio/shared/StudioFileField.vue'
import { schemaPaths } from '@/components/studio/shared/schemaPaths'
import {
  allowPaletteDrop,
  canvasDropPoint,
  paletteKey,
  startPaletteDrag,
} from '@/components/studio/shared/paletteDrag'
import type { Binding } from '@/components/studio/shared/model'
import ReportCollectionEditor from '@/components/studio/report/ReportCollectionEditor.vue'
import ReportMappings from '@/components/studio/report/ReportMappings.vue'
import ReportPreviewExport from '@/components/studio/report/ReportPreviewExport.vue'
import ReportPreviewPanel from '@/components/studio/report/ReportPreviewPanel.vue'
import ReportMeasuredCanvas from '@/components/studio/report/ReportMeasuredCanvas.vue'
import ReportDesignTable from '@/components/studio/report/ReportDesignTable.vue'
import ReportDocumentEditor from '@/components/studio/report/ReportDocumentEditor.vue'
import ReportAdvancedEditor from '@/components/studio/report/ReportAdvancedEditor.vue'
import ReportAdvancedDesign from '@/components/studio/report/ReportAdvancedDesign.vue'
import {
  advancedControlDefaults,
  isAdvancedControl,
} from '@/components/studio/report/advancedControls'
import {
  tableRows,
  tableCell,
  editTable,
  ensureTableRows,
  editableCell,
  tableCells,
  mergeRange,
  splitCell,
  type CellRange,
  type TableAction,
} from '@/components/studio/report/tableEditing'
import {
  resizeEdges,
  reportUnitMm,
  reportUnitValue,
  reportMillimetres,
  roundMm,
  transformReportRect,
  type ReportRect,
  type ResizeEdge,
} from '@/components/studio/report/geometry'
import ReportQueryEditor from '@/components/studio/report/ReportQueryEditor.vue'
import ReportStyleEditor from '@/components/studio/report/ReportStyleEditor.vue'
import { reportControls, reportBandKinds } from '@/components/studio/report/catalogue'
import AppDataTable from '@/components/management/AppDataTable.vue'
import { useReportCopy } from '@/components/studio/report/copy'
import { useStudioCopy } from '@/components/studio/shared/copy'
import {
  binding,
  reportBodyWidth,
  reportPaperSize,
  reportContexts,
} from '@/components/studio/report/model'
import { reportDesignStyle } from '@/components/studio/report/designStyle'
import { clone } from '@/components/studio/shared/model'
import '@/components/studio/shared/studio.css'
const route = useRoute(),
  router = useRouter(),
  t = useReportCopy(),
  studioCopy = useStudioCopy(),
  api = getStudioApi()
const resource = ref<StudioResource>(),
  doc = ref<Json>({}),
  saved = ref(''),
  busy = ref(false),
  error = ref(''),
  loading = ref(true),
  optionsLoading = ref(false),
  optionsError = ref(''),
  datasetOptionsLoading = ref(false),
  datasetOptionsError = ref(''),
  tab = ref('controls'),
  controlSearch = ref(''),
  rulerUnit = ref<'mm' | 'cm' | 'in' | 'px'>('mm'),
  interactionActive = ref(false),
  dragFeedback = ref<{
    bandId: string
    rect: ReportRect
    guideX?: number | undefined
    guideY?: number | undefined
  }>(),
  inspectorTab = ref('basic'),
  selectedColumnId = ref(''),
  selectedRow = ref(0),
  cellMode = ref(false),
  selectionEnd = ref<{ row: number; columnId: string }>(),
  tableSizeHint = ref(''),
  selection = ref(''),
  controlId = ref(''),
  drawer = ref(''),
  result = ref<Json>(),
  parameters = ref<Json>({}),
  datasets = ref<StudioResource[]>([]),
  reports = ref<StudioResource[]>([]),
  contract = ref<Json>(),
  contractLoading = ref(false),
  contractError = ref(''),
  schemaContract = ref<Json>({}),
  assetCandidate = ref<Json | null>(null),
  draftChildren = ref<Record<string, StudioResource>>({}),
  draftDocuments = ref<Record<string, Json>>({}),
  shell = ref<InstanceType<typeof StudioEditorShell>>()
const parameterTypeLabels = computed<Record<string, string>>(() => ({
  String: t.value('parameterText'),
  Int32: t.value('parameterInteger'),
  Decimal: t.value('parameterDecimal'),
  Date: t.value('parameterDate'),
  Boolean: t.value('parameterBoolean'),
  Array: t.value('parameterArray'),
}))
const dataTab = ref('instances')
const dataInstance = ref('0')
const constantEditors = new Map<number, InstanceType<typeof DashboardConstantEditor>>()
function setConstantEditor(index: number, el: Element | ComponentPublicInstance | null) {
  if (el) constantEditors.set(index, el as InstanceType<typeof DashboardConstantEditor>)
  else constantEditors.delete(index)
}
function applyData(): boolean {
  for (const [index, editor] of constantEditors) {
    if (!editor.apply()) {
      dataTab.value = 'instances'
      dataInstance.value = String(index)
      return false
    }
  }
  return true
}
function closeData() {
  if (applyData()) drawer.value = ''
}
const newId = () => crypto.randomUUID()
const nId = String(route.params.nId),
  bands = computed<Json[]>(() => doc.value.bands ?? []),
  instances = computed<Json[]>(() => doc.value.instances ?? []),
  ids = computed(() => instances.value.map((i) => String(i.instanceId))),
  band = computed(() => bands.value.find((b) => b.id === selection.value)),
  control = computed<Json | undefined>(() =>
    band.value?.controls.find((c: Json) => c.id === controlId.value),
  ),
  bundle = computed(() => resource.value?.kind === 'ReportBundle'),
  electronic = computed(() => doc.value.mode === 'ElectronicRecord'),
  driven = computed(() => electronic.value && doc.value.assembly?.stitchMode === 'DataDriven'),
  entriesKey = computed(() => (driven.value ? 'templates' : 'rules')),
  children = computed<Json[]>(() =>
    electronic.value
      ? [...(doc.value.assembly?.[entriesKey.value] ?? [])].sort(compareRules)
      : (doc.value.items ?? []),
  ),
  item = computed(() => children.value.find((i) => i.id === selection.value)),
  bodyWidth = computed(() => reportBodyWidth(doc.value)),
  paperSize = computed(() => reportPaperSize(doc.value)),
  designHeight = computed(
    () =>
      bands.value.reduce((sum, b) => sum + designBandHeight(b) * 3, 0) +
      Number(doc.value.paper?.margin?.top ?? 15) * 3 +
      Number(doc.value.paper?.margin?.bottom ?? 15) * 3,
  ),
  dirty = computed(() => JSON.stringify(doc.value) !== saved.value)
const palette = computed(() =>
  reportControls.filter((c) =>
    `${t.value(c.label)} ${c.type}`
      .toLowerCase()
      .includes(controlSearch.value.trim().toLowerCase()),
  ),
)
const currentStyle = computed(() =>
  doc.value.styles?.find((s: Json) => s.id === control.value?.styleId),
)
const currentColumn = computed<Json | undefined>(
  () =>
    control.value?.table?.columns.find((c: Json) => c.id === selectedColumnId.value) ??
    control.value?.table?.columns[0],
)
const currentCell = computed(() =>
  control.value?.table && currentColumn.value
    ? tableCell(control.value.table, Math.max(0, selectedRow.value), currentColumn.value.id)
    : undefined,
)
const cellSelected = computed(() => control.value?.type === 'Table' && cellMode.value)
watch(
  () => (control.value?.table ? tableRows(control.value.table).length : 1),
  (count) => {
    if (selectedRow.value >= count) selectedRow.value = count - 1
  },
)
const cellRange = computed<CellRange | undefined>(() => {
  const table = control.value?.table
  if (!cellSelected.value || !table || !currentColumn.value) return
  const start = table.columns.findIndex((c: Json) => c.id === currentColumn.value!.id)
  const end = selectionEnd.value
    ? table.columns.findIndex((c: Json) => c.id === selectionEnd.value!.columnId)
    : start
  const range = {
    top: Math.min(selectedRow.value, selectionEnd.value?.row ?? selectedRow.value),
    bottom: Math.max(selectedRow.value, selectionEnd.value?.row ?? selectedRow.value),
    left: Math.min(start, Math.max(0, end)),
    right: Math.max(start, end),
  }
  const cells = tableCells(table, range.top < 0)
  let changed = true
  while (changed) {
    const before = JSON.stringify(range)
    for (const c of cells)
      if (
        c.row <= range.bottom &&
        c.row + c.rowSpan - 1 >= range.top &&
        c.column <= range.right &&
        c.column + c.colSpan - 1 >= range.left
      ) {
        range.top = Math.min(range.top, c.row)
        range.bottom = Math.max(range.bottom, c.row + c.rowSpan - 1)
        range.left = Math.min(range.left, c.column)
        range.right = Math.max(range.right, c.column + c.colSpan - 1)
      }
    changed = before !== JSON.stringify(range)
  }
  return range
})
const tableHasMerges = computed(
  () =>
    control.value?.table &&
    [...tableCells(control.value.table), ...tableCells(control.value.table, true)].some(
      (c) => c.colSpan > 1 || c.rowSpan > 1,
    ),
)
function selectCell(bandId: string, id: string, row: number, columnId: string, extend = false) {
  if (extend && cellSelected.value && controlId.value === id && row < 0 === selectedRow.value < 0) {
    selectionEnd.value = { row, columnId }
    return
  }
  selectControl(bandId, id)
  cellMode.value = true
  selectedRow.value = row
  selectedColumnId.value = columnId
  inspectorTab.value = 'content'
}
function mergeCells(split = false) {
  if (!control.value?.table || !cellRange.value) return
  const range = { ...cellRange.value },
    col = control.value.table.columns[range.left].id
  edit((d) => {
    const c = d.bands
      .find((b: Json) => b.id === selection.value)
      .controls.find((c: Json) => c.id === controlId.value)
    if (split) splitCell(c.table, selectedRow.value, currentColumn.value!.id)
    else mergeRange(c.table, range)
  })
  selectCell(selection.value, controlId.value, range.top, col)
}
function resolvedCellStyle(c: Json, row: number, column: Json): Json {
  const lookup = (id?: string) => doc.value.styles?.find((s: Json) => s.id === id) ?? {}
  const cell = row < 0 ? column.header : tableCell(c.table, row, column.id)
  return {
    border: { widthMm: 0.2, style: 'Solid', color: '#000000' },
    paddingMm: { top: 1, right: 1, bottom: 1, left: 1 },
    ...lookup(c.styleId),
    ...(row < 0 ? { fontWeight: 700 } : lookup(column.styleId)),
    ...lookup(cell?.styleId),
  }
}
const currentCellStyle = computed(() =>
  control.value && currentColumn.value
    ? resolvedCellStyle(control.value, selectedRow.value, currentColumn.value)
    : {},
)
function updateCellStyle(value: Json) {
  if (!control.value?.table || !cellRange.value) return
  const range = { ...cellRange.value }
  edit((d) => {
    const c = d.bands
      .find((b: Json) => b.id === selection.value)
      .controls.find((c: Json) => c.id === controlId.value)
    for (const position of tableCells(c.table, range.top < 0).filter(
      (p) =>
        p.row >= range.top &&
        p.row <= range.bottom &&
        p.column >= range.left &&
        p.column <= range.right,
    )) {
      const column = c.table.columns[position.column]
      const cell = editableCell(c.table, position.row, column.id)
      const changed = clone(resolvedCellStyle(c, position.row, column))
      for (const [key, part] of Object.entries(value)) {
        if (part && typeof part === 'object' && !Array.isArray(part))
          changed[key] = { ...(changed[key] ?? {}), ...part }
        else if (part === undefined) delete changed[key]
        else changed[key] = part
      }
      let uses = 0
      function visit(node: unknown) {
        if (!node || typeof node !== 'object') return
        for (const [key, child] of Object.entries(node)) {
          if (key === 'styleId' && child === cell.styleId) uses++
          else visit(child)
        }
      }
      visit(d.bands)
      const id = cell.styleId && uses === 1 ? cell.styleId : `cell-${newId()}`
      const index = d.styles.findIndex((s: Json) => s.id === id)
      const style = { ...changed, id }
      if (index < 0) d.styles.push(style)
      else d.styles[index] = style
      cell.styleId = id
    }
  })
}
function editCellContent(bandId: string, id: string, row: number, columnId: string, text: string) {
  edit((d) => {
    const c = d.bands.find((b: Json) => b.id === bandId).controls.find((v: Json) => v.id === id)
    if (row < 0) c.table.columns.find((col: Json) => col.id === columnId).label = text
    else
      editableCell(c.table, row, columnId).value = {
        from: 'Constant',
        value: text,
        nullPolicy: 'Error',
      }
  })
}
function tableRowHeight(c: Json, row: number) {
  const cells = tableCells(c.table, row < 0).filter((cell) => cell.row === row)
  return Math.max(
    Number(row < 0 ? (c.table.headerHeightMm ?? 7) : (tableRows(c.table)[row]?.heightMm ?? 7)),
    ...cells.map((cell) => {
      const s = resolvedCellStyle(c, row, c.table.columns[cell.column])
      return (
        (((Number(s.fontSizePt ?? 10) * 25.4) / 72) * Number(s.lineHeight ?? 1.3) +
          Number(s.paddingMm?.top ?? 1) +
          Number(s.paddingMm?.bottom ?? 1) +
          2 * Number(s.border?.widthMm ?? 0.2)) /
        cell.rowSpan
      )
    }),
  )
}
function resizeTablePart(
  event: PointerEvent,
  b: Json,
  c: Json,
  index: number,
  axis: 'column' | 'row',
  zoom: number,
  measuredHeight?: number,
) {
  selectCell(
    b.id,
    c.id,
    axis === 'row' ? index : 0,
    c.table.columns[Math.max(0, axis === 'column' ? index : 0)].id,
  )
  const heights = tableRows(c.table).map((_: Json, r: number) => tableRowHeight(c, r)),
    header = tableRowHeight(c, -1)
  gesture(event, zoom, (d, dx, dy) => {
    const target = d.bands
      .find((v: Json) => v.id === b.id)
      .controls.find((v: Json) => v.id === c.id)
    if (axis === 'row') {
      const height = roundMm(
        Math.max(
          1,
          Math.min(1000, (measuredHeight ?? (index < 0 ? header : heights[index]!)) + dy),
        ),
      )
      if (index < 0) target.table.headerHeightMm = height
      else ensureTableRows(target.table)[index]!.heightMm = height
      tableSizeHint.value = `${t.value('dragRowHeight')}: ${measure(height)}`
    } else {
      const columns = target.table.columns,
        own = Number(columns[index].widthMm),
        next = columns[index + 1]
      const total = columns.reduce((sum: number, col: Json) => sum + Number(col.widthMm), 0)
      const max = next
        ? own + Number(next.widthMm) - 1
        : bodyWidth.value - Number(target.xMm) - total + own
      const width = roundMm(Math.max(1, Math.min(max, own + dx)))
      columns[index].widthMm = width
      if (next) next.widthMm = own + Number(next.widthMm) - width
      else target.widthMm = total - own + width
      tableSizeHint.value = `${t.value('dragColumnWidth')}: ${measure(width)}`
    }
  })
}
function tableAction(action: TableAction) {
  selectionEnd.value = undefined
  if (!control.value?.table || !currentColumn.value) return
  edit((d) => {
    const selected = d.bands
      .find((b: Json) => b.id === selection.value)
      ?.controls.find((c: Json) => c.id === controlId.value)
    if (!selected?.table) return
    const id = currentColumn.value!.id
    const next = editTable(selected.table, action, selectedRow.value, id, newId)
    if ((action === 'columnBefore' || action === 'columnAfter') && next.columnId !== id)
      selected.table.columns.find((c: Json) => c.id === next.columnId).label = t.value('newColumn')
    if (
      action === 'deleteColumn' &&
      !selected.table.columns.some((c: Json) => c.id === id) &&
      d.query
    ) {
      d.query.columns = d.query.columns.filter(
        (c: Json) => c.controlId !== selected.id || c.columnId !== id,
      )
      d.query.defaultSort = d.query.defaultSort.filter((s: Json) => s.fieldId !== id)
    }
    selectedRow.value = next.row
    selectedColumnId.value = next.columnId
  })
}
const bandLabel = (kind: string) => {
  const entry = reportBandKinds.find((b) => b.kind === kind)
  return entry ? t.value(entry.label) : kind
}
const controlLabel = (type: string) => {
  const entry = reportControls.find((c) => c.type === type)
  return entry ? t.value(entry.label) : type
}
const measuredTableHeights = ref<Record<string, number>>({})
function designControlHeight(c: Json): number {
  if (c.type !== 'Table') return Number(c.heightMm)
  return Math.max(
    Number(c.heightMm),
    measuredTableHeights.value[c.id] ?? 0,
    tableRows(c.table).reduce((sum: number, _: Json, r: number) => sum + tableRowHeight(c, r), 0) +
      (c.table.headerMode === 'Hidden' ? 0 : tableRowHeight(c, -1)),
  )
}
const designBandHeight = (b: Json) =>
  Math.max(
    Number(b.heightMm),
    ...b.controls.map((c: Json) => Number(c.yMm) + designControlHeight(c)),
  )
const rulerSections = computed(() => {
  let offset = Number(doc.value.paper?.margin?.top ?? 15) * 3
  return bands.value.map((b) => {
    const section = {
      id: String(b.id),
      label: bandLabel(b.kind),
      name: String(b.name ?? ''),
      kind: String(b.kind),
      startMm: offset / 3,
      heightMm: designBandHeight(b),
    }
    offset += section.heightMm * 3
    return section
  })
})
const styleFor = (c: Json) =>
  reportDesignStyle(doc.value.styles?.find((s: Json) => s.id === c.styleId))
function measure(value: number) {
  const unit = rulerUnit.value
  return `${Number((value / reportUnitMm[unit]).toFixed(2))} ${unit}`
}
const rectReadout = (c: Json) =>
  `X ${measure(Number(c.xMm))} · Y ${measure(Number(c.yMm))} · W ${measure(Number(c.widthMm))} · H ${measure(Number(c.heightMm))}`
function resizePosition(c: Json, edge: ResizeEdge) {
  return {
    left:
      (Number(c.xMm) +
        (edge.includes('w') ? 0 : edge.includes('e') ? Number(c.widthMm) : Number(c.widthMm) / 2)) *
        3 +
      'px',
    top:
      (Number(c.yMm) +
        (edge.includes('n')
          ? 0
          : edge.includes('s')
            ? designControlHeight(c)
            : designControlHeight(c) / 2)) *
        3 +
      'px',
    cursor: `${edge}-resize`,
  }
}
function controlCaption(c: Json): string {
  if (c.type === 'Table') return c.table.columns.map((col: Json) => col.label).join(' | ')
  if (['Line', 'Box'].includes(c.type)) return ''
  if (['Html', 'Shape', 'SubReport', 'PdfContent', 'TableOfContents'].includes(c.type))
    return controlLabel(c.type)
  if (
    ['Chart', 'Sparkline', 'PivotTable'].includes(c.type) &&
    c.value?.from === 'Constant' &&
    Array.isArray(c.value.value)
  )
    return t.value('rowCount') + ': ' + c.value.value.length
  if (c.type === 'PageNumber') return '1 / …'
  if (c.aggregate) return `${c.aggregate.function}(${c.aggregate.field?.path?.join('.') ?? ''})`
  if (c.value?.from === 'PageParam' && c.value.path?.length === 1) {
    const name = c.value.path[0]
    const value =
      parameters.value[name] ??
      doc.value.parameters?.find((p: Json) => p.name === name)?.defaultValue
    if (value !== undefined && value !== null) return String(value)
  }
  if (c.type === 'CheckBox' && c.value?.from === 'Constant')
    return c.value.value === true ? '☑' : c.value.value === false ? '☐' : '−'
  return c.value?.from === 'Constant'
    ? String(c.value.value ?? '')
    : `[${c.value?.from ?? c.type}:${c.value?.path?.join('.') ?? ''}]`
}
const ancestorBands = computed<Json[]>(() => {
  const result: Json[] = []
  const seen = new Set<string>()
  let parent = band.value?.parentBandId
  while (parent && !seen.has(String(parent))) {
    seen.add(String(parent))
    const current = bands.value.find((b) => b.id === parent)
    if (!current) break
    result.push(current)
    parent = current.parentBandId
  }
  return result
})
const fieldSources = computed(() => {
  const known = schemaContract.value.bandSchemas ?? {},
    roots = schemaContract.value.instanceSchemas ?? {}
  const result: { label: string; schema: Json; binding: Binding }[] = Object.entries(roots).map(
    ([id, schema]) => ({
      label: `${t.value('instance')} · ${id}`,
      schema: schema as Json,
      binding: { from: 'Instance', instanceId: id, path: [] },
    }),
  )
  result.push({
    label: t.value('parameters'),
    schema: {
      type: 'Object',
      children: (doc.value.parameters ?? []).map((p: Json) => ({
        ...p,
        type: p.type === 'Int32' ? 'Decimal' : p.type,
      })),
    },
    binding: { from: 'PageParam', path: [] },
  })
  const row = band.value && (known[band.value.id] ?? known[band.value.parentBandId])
  if (row) result.push({ label: t.value('row'), schema: row, binding: { from: 'Row', path: [] } })
  const parent = ancestorBands.value.find((b) => known[b.id])
  if (parent)
    result.push({
      label: t.value('parent'),
      schema: known[parent.id],
      binding: { from: 'ParentRow', path: [] },
    })
  const root = [...ancestorBands.value].reverse().find((b) => known[b.id]) ?? band.value
  if (root && known[root.id])
    result.push({
      label: t.value('rootRow'),
      schema: known[root.id],
      binding: { from: 'Root', path: [] },
    })
  for (const b of ancestorBands.value)
    if (known[b.id])
      result.push({
        label: `${t.value('bands')} · ${b.name}`,
        schema: known[b.id],
        binding: { from: 'ParentRow', contextBandId: String(b.id), path: [] },
      })
  return result
})
const sourcePaths = computed(() =>
  fieldSources.value.map((source) => ({ ...source, paths: schemaPaths(source.schema) })),
)
function bindingPaths(value: Binding) {
  const source = sourcePaths.value.find(
    (source) =>
      source.binding.from === value.from &&
      source.binding.instanceId === value.instanceId &&
      source.binding.contextBandId === value.contextBandId,
  )
  return source?.paths ?? []
}
function chooseField(value: Binding) {
  if (
    control.value &&
    !(control.value.type === 'Table' && control.value.table.rowMode === 'BandRow')
  )
    updateControl('value', clone(value))
}
function dropOnPaper(event: DragEvent, zoom: number) {
  const paper = event.currentTarget as HTMLElement
  const elements = [...paper.querySelectorAll<HTMLElement>(':scope > .report-band')]
  const targetElement =
    elements.find((element) => event.clientY < element.getBoundingClientRect().bottom) ??
    elements.at(-1)
  const target = bands.value.find((b) => b.id === targetElement?.dataset.bandId)
  if (!target || !targetElement) return
  const point = canvasDropPoint(event, targetElement, 3 * zoom)
  const type = paletteKey(event, 'report')
  if (type && reportControls.some((entry) => entry.type === type)) {
    void addControl(type, String(target.id), point)
    return
  }
  const source = event.dataTransfer?.getData('application/x-platform-studio-binding')
  if (!source) return
  try {
    const value = JSON.parse(source) as Binding
    if (!reportContexts.includes(value.from) || !Array.isArray(value.path)) return
    const id = newId()
    const x = Math.max(0, Math.min(bodyWidth.value - 50, point.x)),
      y = point.y
    edit((d) =>
      d.bands
        .find((b: Json) => b.id === target.id)
        .controls.push({
          id,
          type: 'Field',
          xMm: x,
          yMm: y,
          widthMm: Math.min(50, bodyWidth.value),
          heightMm: 8,
          styleId: 'body',
          value,
          canGrow: true,
          keepTogether: false,
          flow: { mode: 'Follow', afterControlIds: [] },
        }),
    )
    selectControl(String(target.id), id)
  } catch {
    /* Invalid field drops do not mutate the draft. */
  }
}
let schemaTimer: ReturnType<typeof setTimeout> | undefined,
  schemaGeneration = 0
watch(
  () =>
    JSON.stringify([
      bundle.value ? undefined : doc.value.instances,
      doc.value.bands?.map((b: Json) => [b.id, b.parentBandId, b.data]),
      doc.value.parameters,
      doc.value.query?.filters,
    ]),
  () => {
    if (schemaTimer) clearTimeout(schemaTimer)
    if (!resource.value || bundle.value) return
    const own = ++schemaGeneration
    schemaTimer = setTimeout(async () => {
      try {
        const result = await api.reportDraftContract({
          kind: 'Report',
          resourceNId: nId,
          document: doc.value,
          parameters: parameters.value,
        })
        if (own === schemaGeneration) schemaContract.value = result
      } catch {
        if (own === schemaGeneration) schemaContract.value = {}
      }
    }, 300)
  },
)
onBeforeUnmount(() => {
  previewRequest?.abort()
  cancelGesture?.()
  schemaGeneration++
  if (schemaTimer) clearTimeout(schemaTimer)
})
function edit(fn: (d: Json) => void) {
  shell.value?.apply(fn)
}
function updateBand(key: string, value: unknown) {
  edit((d) => {
    const b = d.bands.find((b: Json) => b.id === selection.value)
    if (b) b[key] = value
  })
}
function setBandKind(kind: string) {
  edit((d) => {
    const selected = d.bands.find((b: Json) => b.id === selection.value)
    selected.kind = kind
    if (kind === 'Detail')
      selected.data ??= { mode: 'RootPath', instanceId: ids.value[0] ?? '', path: [] }
    else delete selected.data
    if (kind.startsWith('Group')) selected.group ??= { keys: [binding()], pairedBandId: '' }
    else delete selected.group
  })
}
function groupPair(id: string) {
  edit((d) => {
    const selected = d.bands.find((b: Json) => b.id === selection.value),
      other = d.bands.find((b: Json) => b.id === id)
    selected.group.pairedBandId = id
    if (other) other.group = { keys: clone(selected.group.keys), pairedBandId: selected.id }
  })
}
function groupKeys(keys: Json[]) {
  edit((d) => {
    const selected = d.bands.find((b: Json) => b.id === selection.value)
    selected.group.keys = keys
    const other = d.bands.find((b: Json) => b.id === selected.group.pairedBandId)
    if (other?.group) other.group.keys = clone(keys)
  })
}
function aggregate(enabled: boolean | string | number) {
  edit((d) => {
    const selected = d.bands
      .find((b: Json) => b.id === selection.value)
      .controls.find((c: Json) => c.id === controlId.value)
    if (enabled) {
      selected.aggregate = {
        function: 'Count',
        scopeBandId: bands.value.find((b) => b.kind === 'Detail')?.id ?? '',
        field: binding(),
      }
      delete selected.value
    } else {
      delete selected.aggregate
      selected.value = binding()
    }
  })
}
function columnAction(index: number, direction: number) {
  if (direction === 0) {
    tableAction('deleteColumn')
    return
  }
  edit((d) => {
    const selected = d.bands
      .find((b: Json) => b.id === selection.value)
      .controls.find((c: Json) => c.id === controlId.value)
    const columns = selected.table.columns
    const next = index + direction
    if (next >= 0 && next < columns.length)
      [columns[index], columns[next]] = [columns[next], columns[index]]
  })
}
function updateControl(key: string, value: unknown) {
  edit((d) => {
    const c = d.bands
      .find((b: Json) => b.id === selection.value)
      ?.controls.find((c: Json) => c.id === controlId.value)
    if (c) c[key] = value
  })
}
function updateAdvancedControl(next: Json) {
  edit((d) => {
    const controls = d.bands.find((b: Json) => b.id === selection.value)?.controls
    const index = controls?.findIndex((c: Json) => c.id === next.id) ?? -1
    if (index >= 0) controls[index] = next
  })
}
function select(id: string) {
  selection.value = id
  controlId.value = ''
  shell.value?.select(id)
  shell.value?.showProperties()
  inspectorTab.value = 'basic'
}
function selectControl(bandId: string, id: string) {
  cellMode.value = false
  selectionEnd.value = undefined
  if (controlId.value !== id) {
    selectedRow.value = 0
    selectedColumnId.value = ''
  }
  selection.value = bandId
  shell.value?.select(bandId)
  controlId.value = id
  shell.value?.showProperties()
  if (control.value?.type === 'Table') inspectorTab.value = 'basic'
}
function addBand(child = false, kind = 'Detail') {
  const id = crypto.randomUUID()
  edit((d) => {
    const pairedId = newId()
    const grouping = kind.startsWith('Group')
    const make = (id: string, kind: string, otherId?: string): Json => ({
      id,
      kind,
      name: bandLabel(kind),
      ...(child && band.value && !kind.startsWith('Page') ? { parentBandId: band.value.id } : {}),
      heightMm: 20,
      grow: true,
      controls: [],
      ...(kind === 'Detail'
        ? {
            data: child
              ? { mode: 'ParentPath', path: [] }
              : { mode: 'RootPath', instanceId: ids.value[0] ?? '', path: [] },
          }
        : {}),
      ...(otherId ? { group: { keys: [binding()], pairedBandId: otherId } } : {}),
      emptyBehavior: 'Skip',
    })
    const order = reportBandKinds.map((b) => String(b.kind))
    const insert = (value: Json) => {
      const at = d.bands.findIndex(
        (b: Json) => !b.parentBandId && order.indexOf(b.kind) > order.indexOf(value.kind),
      )
      d.bands.splice(at < 0 ? d.bands.length : at, 0, value)
    }
    insert(make(id, kind, grouping ? pairedId : undefined))
    if (grouping) insert(make(pairedId, kind === 'GroupHeader' ? 'GroupFooter' : 'GroupHeader', id))
  })
  select(id)
}
async function removeBand() {
  if (!band.value) return
  try {
    await ElMessageBox.confirm(t.value('deleteNote'), t.value('deleteBand'))
  } catch {
    return
  }
  const affected = new Set([band.value.id])
  let count = 0
  while (affected.size !== count) {
    count = affected.size
    bands.value
      .filter((b) => affected.has(b.parentBandId) || affected.has(b.group?.pairedBandId))
      .forEach((b) => affected.add(b.id))
  }
  edit((d) => (d.bands = d.bands.filter((b: Json) => !affected.has(b.id))))
  selection.value = ''
  controlId.value = ''
}
function compareRules(a: Json, b: Json) {
  const priority = Number(a.priority ?? 0) - Number(b.priority ?? 0)
  return priority || (a.id < b.id ? -1 : a.id > b.id ? 1 : 0)
}
function move(delta: number) {
  edit((d) => {
    const list = bundle.value
      ? electronic.value
        ? d.assembly[entriesKey.value]
        : d.items
      : d.bands
    if (bundle.value && electronic.value) list.sort(compareRules)
    const i = list.findIndex((x: Json) => x.id === selection.value),
      j = i + delta
    if (i >= 0 && j >= 0 && j < list.length) {
      ;[list[i], list[j]] = [list[j], list[i]]
      if (bundle.value && electronic.value)
        list.forEach((rule: Json, index: number) => {
          rule.priority = index
        })
    }
  })
}
async function addControl(type: string, targetId?: string, point?: { x: number; y: number }) {
  if (targetId) select(targetId)
  if (!band.value) {
    const target = bands.value.find((b) => b.kind === 'Detail') ?? bands.value[0]
    if (target) select(target.id)
    else {
      addBand()
      await nextTick()
    }
  }
  if (
    band.value?.controls.some((c: Json) =>
      ['SubReport', 'PdfContent', 'TableOfContents'].includes(c.type),
    )
  ) {
    addBand(false, 'ReportHeader')
    await nextTick()
  }
  const documentControl = ['SubReport', 'PdfContent', 'TableOfContents'].includes(type)
  if (
    documentControl &&
    (band.value?.controls.length || ['PageHeader', 'PageFooter'].includes(band.value?.kind))
  ) {
    addBand(false, 'ReportHeader')
    await nextTick()
  }
  const id = crypto.randomUUID()
  const width = documentControl ? bodyWidth.value : Math.min(80, bodyWidth.value)
  const y = point
    ? roundMm(point.y)
    : Math.max(0, ...band.value!.controls.map((c: Json) => Number(c.yMm) + Number(c.heightMm) + 2))
  edit((d) =>
    d.bands
      .find((b: Json) => b.id === selection.value)
      ?.controls.push({
        id,
        type,
        xMm: point ? roundMm(Math.max(0, Math.min(bodyWidth.value - width, point.x))) : 0,
        yMm: y,
        widthMm: width,
        heightMm: 10,
        styleId: 'body',
        canGrow: true,
        flow: { mode: 'Follow', afterControlIds: [] },
        ...(type === 'Table'
          ? {
              table: {
                rowMode: 'BandRow',
                columns: [{ id: newId(), label: 'Column', widthMm: width, value: binding() }],
                headerMode: 'OncePerParent',
                repeatHeader: true,
              },
            }
          : ['Text', 'Field', 'Html', 'Image', 'CheckBox'].includes(type)
            ? {
                value: ['Text', 'Html', 'CheckBox'].includes(type)
                  ? {
                      from: 'Constant',
                      value:
                        type === 'CheckBox'
                          ? false
                          : type === 'Html'
                            ? '<p>Rich text</p>'
                            : t.value('labelControl'),
                      nullPolicy: 'Error',
                    }
                  : binding(),
              }
            : {}),
        ...advancedControlDefaults(type, t.value('labelControl')),
        ...(documentControl ? { xMm: 0, yMm: 0, flow: undefined, keepTogether: false } : {}),
        ...(type === 'SubReport'
          ? { subReport: { reportNId: '', revisionNId: '', parameterMap: [], instanceMap: [] } }
          : {}),
        ...(type === 'PdfContent'
          ? { value: { from: 'Constant', value: '', nullPolicy: 'Error' } }
          : {}),
        ...(type === 'TableOfContents' ? { toc: { title: t.value('tocControl') } } : {}),
      }),
  )
  controlId.value = id
  inspectorTab.value = 'basic'
  shell.value?.showProperties()
}
function removeControl() {
  edit((d) => {
    const controls = d.bands.find((b: Json) => b.id === selection.value).controls
    const index = controls.findIndex((c: Json) => c.id === controlId.value)
    if (index >= 0) controls.splice(index, 1)
    for (const c of controls)
      if (c.flow)
        c.flow.afterControlIds = (c.flow.afterControlIds ?? []).filter(
          (id: string) => id !== controlId.value,
        )
  })
  controlId.value = ''
}
let cancelGesture: (() => void) | undefined
function gesture(
  event: PointerEvent,
  zoom: number,
  mutate: (d: Json, dx: number, dy: number, e: PointerEvent) => void,
) {
  if (event.button !== 0) return
  cancelGesture?.()
  const target = event.currentTarget as HTMLElement
  target.setPointerCapture(event.pointerId)
  const x = event.clientX,
    y = event.clientY
  let draft: Json | undefined
  const move = (e: PointerEvent) => {
    if (e.pointerId !== event.pointerId) return
    if (!draft && Math.hypot(e.clientX - x, e.clientY - y) < 3) return
    draft ??= clone(doc.value)
    interactionActive.value = true
    const d = clone(draft)
    mutate(d, (e.clientX - x) / zoom / 3, (e.clientY - y) / zoom / 3, e)
    shell.value?.update(d, false)
  }
  const finish = (cancelled = false) => {
    target.removeEventListener('pointermove', move)
    target.removeEventListener('pointerup', end)
    target.removeEventListener('pointercancel', cancel)
    target.removeEventListener('lostpointercapture', cancel)
    document.removeEventListener('keydown', key, true)
    if (target.hasPointerCapture(event.pointerId)) target.releasePointerCapture(event.pointerId)
    if (draft) {
      if (cancelled) shell.value?.update(draft, false)
      else if (JSON.stringify(draft) !== JSON.stringify(doc.value))
        shell.value?.update(clone(doc.value), true)
    }
    interactionActive.value = false
    dragFeedback.value = undefined
    tableSizeHint.value = ''
    cancelGesture = undefined
  }
  const end = (e: PointerEvent) => {
    if (e.pointerId === event.pointerId) finish()
  }
  const cancel = () => finish(true)
  const key = (e: KeyboardEvent) => {
    if (e.key === 'Escape') {
      e.preventDefault()
      e.stopImmediatePropagation()
      cancel()
    }
  }
  cancelGesture = cancel
  target.addEventListener('pointermove', move)
  target.addEventListener('pointerup', end)
  target.addEventListener('pointercancel', cancel)
  target.addEventListener('lostpointercapture', cancel)
  document.addEventListener('keydown', key, true)
}
function isDocumentControl(type: unknown) {
  return ['SubReport', 'PdfContent', 'TableOfContents'].includes(String(type))
}
function drag(event: PointerEvent, b: Json, c: Json, zoom: number, edge: ResizeEdge = '') {
  if (event.button !== 0) return
  selectControl(String(b.id), String(c.id))
  if (isDocumentControl(c.type)) return
  const rect = (value: Json): ReportRect => ({
    xMm: Number(value.xMm),
    yMm: Number(value.yMm),
    widthMm: Number(value.widthMm),
    heightMm: Number(value.heightMm),
  })
  const start = rect(c)
  if (edge) start.heightMm = designControlHeight(c)
  const peers = b.controls.filter((v: Json) => v.id !== c.id).map(rect)
  const availableWidth = bodyWidth.value
  gesture(event, zoom, (d, dx, dy, e) => {
    const result = transformReportRect(
      start,
      dx,
      dy,
      edge,
      peers,
      availableWidth,
      e.altKey ? -1 : 5 / zoom / 3,
    )
    const current = d.bands
      .find((v: Json) => v.id === b.id)
      .controls.find((v: Json) => v.id === c.id)
    Object.assign(current, result.rect)
    // Table columns and the resized table keep the same physical total width.
    if (edge && current.table && result.rect.widthMm !== start.widthMm) {
      const total = current.table.columns.reduce(
        (sum: number, col: Json) => sum + Number(col.widthMm),
        0,
      )
      if (total > 0)
        current.table.columns.forEach((col: Json) => {
          col.widthMm = (Number(col.widthMm) * result.rect.widthMm) / total
        })
    }
    dragFeedback.value = { bandId: String(b.id), ...result }
  })
}
function dragMargin(side: 'top' | 'right' | 'bottom' | 'left', event: PointerEvent, zoom: number) {
  if (event.button !== 0) return
  select(side === 'top' || side === 'bottom' ? `${side}-margin` : '')
  const start = Number(doc.value.paper.margin?.[side] ?? 15)
  const horizontal = side === 'left' || side === 'right'
  const opposite = ({ top: 'bottom', bottom: 'top', left: 'right', right: 'left' } as const)[side]
  const max =
    (horizontal
      ? paperSize.value.width
      : paperSize.value.height -
        Number(doc.value.paper.headerHeightMm ?? 15) -
        Number(doc.value.paper.footerHeightMm ?? 10)) -
    Number(doc.value.paper.margin?.[opposite] ?? 15) -
    1
  gesture(event, zoom, (d, dx, dy) => {
    d.paper.margin ??= {}
    d.paper.margin[side] = roundMm(
      Math.max(0, Math.min(max, start + (horizontal ? dx : dy) * (side === 'right' ? -1 : 1))),
    )
  })
}
function dragBand(event: PointerEvent, b: Json, zoom: number) {
  if (event.button !== 0) return
  select(String(b.id))
  const start = designBandHeight(b)
  const minimum = Math.max(
    1,
    ...b.controls.map((c: Json) => Number(c.yMm) + designControlHeight(c)),
  )
  gesture(event, zoom, (d, _dx, dy) => {
    const current = d.bands.find((v: Json) => v.id === b.id)
    current.heightMm = roundMm(Math.max(minimum, start + dy))
  })
}
async function guarded(fn: () => Promise<void>) {
  if (busy.value) return
  busy.value = true
  error.value = ''
  try {
    await fn()
  } catch (e) {
    error.value = e instanceof Error ? e.message : t.value('error')
  } finally {
    busy.value = false
  }
}
function save() {
  if (!applyData()) return
  return guarded(async () => {
    const value = await api.save(resource.value!, doc.value)
    resource.value = value.resource
    doc.value = value.draftDocument
    saved.value = JSON.stringify(doc.value)
  })
}
const previewExportInput = ref<Json>()
let previewRequest: AbortController | undefined
function execute(action: 'plan' | 'preview') {
  if (busy.value) return
  result.value = undefined
  previewExportInput.value = undefined
  drawer.value = action
  void guarded(async () => {
    const input = {
      kind: resource.value!.kind,
      resourceNId: nId,
      document: doc.value,
      parameters: parameters.value,
      draftDependencies: Object.values(draftChildren.value)
        .filter((child) => children.value.some((item) => item.reportNId === child.nId))
        .map((child) => ({
          resourceNId: child.nId,
          optimisticVersion: child.optimisticVersion,
          concurrencyVersion: child.concurrencyVersion,
        })),
    }
    // Keep the successful preview's definition/parameters independent of later form edits.
    const captured = JSON.parse(JSON.stringify(input)) as Json
    const controller = action === 'preview' ? new AbortController() : undefined
    previewRequest = controller
    try {
      const response = controller
        ? await api.reportPreview(captured, controller.signal)
        : await api.reportPlan(input)
      if (!controller?.signal.aborted && drawer.value === action) {
        result.value = response
        if (action === 'preview' && response.status === 'Succeeded')
          previewExportInput.value = captured
      }
    } catch (e) {
      if (!controller?.signal.aborted) throw e
    } finally {
      previewRequest = undefined
    }
  })
}
function closePreview() {
  previewExportInput.value = undefined
  previewRequest?.abort()
  drawer.value = ''
  result.value = undefined
}
async function attachImage() {
  await guarded(async () => {
    if (!assetCandidate.value || !['Image', 'PdfContent'].includes(control.value?.type)) return
    const candidate = assetCandidate.value,
      bandId = selection.value,
      imageId = controlId.value
    const asset = await api.reportAsset(nId, candidate, `image-${candidate.fileNId}`)
    edit((d) => {
      const image = d.bands
        .find((b: Json) => b.id === bandId)
        ?.controls.find((c: Json) => c.id === imageId)
      if (!['Image', 'PdfContent'].includes(image?.type)) return
      d.assets ??= []
      if (!d.assets.some((a: Json) => a.id === asset.id)) d.assets.push(asset)
      image.value = { from: 'Constant', value: asset.id, nullPolicy: 'Error' }
    })
    if (assetCandidate.value === candidate) assetCandidate.value = null
  })
}
async function selectChildDraft(enabled: boolean) {
  if (!item.value?.reportNId) return
  const childId = String(item.value.reportNId)
  if (!enabled) {
    delete draftChildren.value[childId]
    delete draftDocuments.value[childId]
    return
  }
  await guarded(async () => {
    const draft = await api.draft(childId)
    draftDocuments.value[childId] = draft.draftDocument
    draftChildren.value[childId] = draft.resource
  })
}
async function editChild() {
  if (!item.value?.reportNId) return
  if (dirty.value) await save()
  if (dirty.value) return
  await router.push({
    name: 'studio-reports-design',
    params: { nId: String(item.value.reportNId) },
    query: { returnTo: nId },
  })
}
function publish() {
  void guarded(async () => {
    if (dirty.value) throw new Error(t.value('save'))
    const note = await ElMessageBox.prompt(t.value('note'), t.value('publish'))
    const validation = await api.validate(nId, doc.value)
    result.value = validation
    if (validation.valid) {
      await api.publish(resource.value!, note.value, validation.dependencyPlanHash)
      const value = await api.draft(nId)
      resource.value = value.resource
    } else {
      drawer.value = 'plan'
    }
  })
}
function addInstance() {
  if (!applyData()) return
  dataInstance.value = String(instances.value.length)
  edit((d) =>
    d.instances.push({
      instanceId: crypto.randomUUID(),
      datasetNId: '',
      revisionNId: '',
      parameters: [],
      dependsOn: [],
      refreshSeconds: 0,
    }),
  )
}
function setInstanceSource(instance: Json, sourceType: string) {
  if (!applyData()) return
  edit((d) => {
    const current = d.instances.find((i: Json) => i.instanceId === instance.instanceId)
    current.sourceType = sourceType
    current.parameters = []
    current.dependsOn = []
    current.refreshSeconds = 0
    if (sourceType === 'Constant') {
      delete current.datasetNId
      delete current.revisionNId
      current.value = []
    } else {
      delete current.value
      current.datasetNId = ''
      current.revisionNId = ''
    }
  })
}
function setDataset(i: Json, nId: string) {
  const ds = datasets.value.find((d) => d.nId === nId)
  i.datasetNId = nId
  i.revisionNId = ds?.publishedRevisionNId ?? ''
}
const reportOptions = computed(() =>
  reports.value.map((report) => ({ value: report.nId, label: report.name })),
)
const reportIndex = computed(() => new Map(reports.value.map((report) => [report.nId, report])))
function childTitle(child: Json) {
  return (
    child.name?.trim() ||
    reportIndex.value.get(child.reportNId)?.name ||
    t.value(
      !child.reportNId
        ? 'chooseReport'
        : optionsLoading.value
          ? 'loadingReport'
          : 'unavailableReport',
    )
  )
}
function revisionTitle(child: Json) {
  if (draftChildren.value[child.reportNId]) return t.value('useChildDraft')
  if (!child.revisionNId) return t.value('noRevision')
  const report = reportIndex.value.get(child.reportNId)
  return report?.publishedRevisionNo && report.publishedRevisionNId === child.revisionNId
    ? t.value('revision') + ' V' + report.publishedRevisionNo
    : t.value('revision')
}
function switchStitchMode(mode: string) {
  edit((d) => {
    d.assembly.stitchMode = mode
    if (mode === 'DataDriven') d.assembly.templates ??= []
    else d.assembly.rules ??= []
  })
  select('')
}
function addItem() {
  const id = crypto.randomUUID()
  edit((d) => {
    const next: Json = {
      id,
      reportNId: '',
      revisionNId: '',
      parameterMap: [],
      ...(driven.value ? {} : { required: !electronic.value }),
    }
    if (driven.value)
      next.priority =
        Math.max(-1, ...children.value.map((child) => Number(child.priority ?? 0))) + 1
    else if (electronic.value)
      Object.assign(next, {
        name: 'Rule',
        priority: Math.max(-1, ...children.value.map((child) => Number(child.priority ?? 0))) + 1,
        content: { mode: 'WholeReport' },
        presence: { mode: 'ParentPath', path: [] },
        emptyBehavior: 'Skip',
        headerMode: 'Inherit',
        breakBefore: 'Auto',
      })
    else Object.assign(next, { enabled: true, startOnNewPage: true })
    ;(electronic.value ? d.assembly[entriesKey.value] : d.items).push(next)
  })
  select(id)
}
function removeItem() {
  if (!item.value) return
  const id = item.value.id
  edit((d) => {
    if (electronic.value)
      d.assembly[entriesKey.value] = d.assembly[entriesKey.value].filter((i: Json) => i.id !== id)
    else d.items = d.items.filter((i: Json) => i.id !== id)
  })
  select('')
}
function setTemplate(nId: string) {
  if (!item.value) return
  const chosen = reports.value.find((r) => r.nId === nId)
  item.value.reportNId = nId
  item.value.revisionNId = chosen?.publishedRevisionNId ?? ''
}
watch(
  [
    () => item.value?.id,
    () => item.value?.reportNId,
    () => item.value?.revisionNId,
    () => inspectorTab.value,
    () => item.value?.content?.mode,
    () => draftChildren.value[item.value?.reportNId],
  ],
  async (_, __, onCleanup) => {
    let stale = false
    onCleanup(() => {
      stale = true
    })
    contract.value = undefined
    contractError.value = ''
    contractLoading.value = false
    const selected = item.value
    if (
      !bundle.value ||
      driven.value ||
      inspectorTab.value !== 'data' ||
      selected?.content?.mode !== 'BandSubtree' ||
      !selected?.reportNId
    )
      return
    const childId = String(selected.reportNId)
    const draft = draftDocuments.value[childId]
    if (!draft && !selected.revisionNId) return
    contractLoading.value = true
    try {
      const value = draft
        ? await api.reportDraftContract({ kind: 'Report', resourceNId: childId, document: draft })
        : await api.reportContract(childId, String(selected.revisionNId))
      if (!stale) contract.value = value
    } catch (e) {
      if (!stale) contractError.value = e instanceof Error ? e.message : t.value('error')
    } finally {
      if (!stale) contractLoading.value = false
    }
  },
)
async function loadReports(draftReady?: Promise<void>) {
  const first = await api.list('Report', { page: 1, pageSize: 200 })
  const items = [...first.items]
  reports.value = first.items
  // Resolve references after the draft arrives, while starting the first page in parallel.
  await draftReady
  const needed = new Set<string>(
    [
      ...(doc.value.bands ?? []).flatMap((b: Json) =>
        (b.controls ?? [])
          .filter((c: Json) => c.type === 'SubReport')
          .map((c: Json) => c.subReport),
      ),
      ...(doc.value.items ?? []),
      ...(doc.value.assembly?.rules ?? []),
      ...(doc.value.assembly?.templates ?? []),
    ]
      .map((i) => i.reportNId)
      .filter(Boolean),
  )
  for (const item of items) needed.delete(item.nId)
  for (let page = 2; needed.size && items.length < Number(first.totalCount); page++) {
    const more = await api.list('Report', { page, pageSize: 200 })
    if (!more.items.length) break
    items.push(...more.items)
    for (const item of more.items) needed.delete(item.nId)
  }
  return { ...first, items }
}
async function loadReportOptions(draftReady?: Promise<void>) {
  if (optionsLoading.value) return
  optionsLoading.value = true
  optionsError.value = ''
  try {
    reports.value = (await loadReports(draftReady)).items
  } catch {
    optionsError.value = t.value('optionsFailed')
  } finally {
    optionsLoading.value = false
  }
}
let datasetsLoaded = false
async function loadDatasetOptions() {
  if (datasetsLoaded || datasetOptionsLoading.value) return
  datasetOptionsLoading.value = true
  datasetOptionsError.value = ''
  try {
    datasets.value = (await api.list('Dataset', { pageSize: 200 })).items
    datasetsLoaded = true
  } catch {
    datasetOptionsError.value = t.value('optionsFailed')
  } finally {
    datasetOptionsLoading.value = false
  }
}
function loadOptions() {
  if (optionsError.value) void loadReportOptions()
  if (datasetOptionsError.value) void loadDatasetOptions()
}
watch(
  () => drawer.value === 'data' || (bundle.value && !driven.value && inspectorTab.value === 'data'),
  (needed) => {
    if (needed) void loadDatasetOptions()
  },
)
onMounted(async () => {
  const draftReady = guarded(async () => {
    const value = await api.draft(nId)
    resource.value = value.resource
    doc.value = value.draftDocument
    saved.value = JSON.stringify(doc.value)
  })
  void loadReportOptions(draftReady)
  await draftReady
  loading.value = false
})
onBeforeRouteLeave(async () => {
  if (!applyData()) return false
  if (!dirty.value) return true
  try {
    await ElMessageBox.confirm(t.value('unsaved'))
    return true
  } catch {
    return false
  }
})
</script>
<template>
  <AppPage
    :title="resource?.name ?? t('reports')"
    :description="
      bundle ? t(driven ? 'candidateTemplates' : electronic ? 'rules' : 'items') : t('bands')
    "
    class="studio-page report-designer"
    v-loading="loading"
  >
    <el-alert v-if="error" :title="error" type="error" :closable="false" />
    <template #actions>
      <el-button
        @click="
          returnFromWorkspace(
            router,
            route.query.returnTo
              ? {
                  name: 'studio-report-bundles-design',
                  params: { nId: String(route.query.returnTo) },
                }
              : bundle
                ? '/pc/studio/report-bundles'
                : '/pc/studio/reports',
          )
        "
        >{{ t('back') }}</el-button
      ><el-button :disabled="!shell?.canUndo" @click="shell?.undo()">{{
        studioCopy('undo')
      }}</el-button
      ><el-button :disabled="!shell?.canRedo" @click="shell?.redo()">{{
        studioCopy('redo')
      }}</el-button
      ><PermissionGate permission-n-id="studio.report.design"
        ><el-button v-if="bundle" :disabled="busy" @click="execute('plan')">{{
          t('plan')
        }}</el-button
        ><el-button :disabled="busy" @click="execute('preview')">{{ t('preview') }}</el-button
        ><el-button :loading="busy" @click="save">{{ t('save') }}</el-button></PermissionGate
      ><PermissionGate permission-n-id="studio.report.publish"
        ><el-button type="primary" :disabled="busy || dirty" @click="publish">{{
          t('publish')
        }}</el-button></PermissionGate
      >
    </template>
    <StudioEditorShell
      v-if="doc.schemaVersion"
      ref="shell"
      class="report-design-shell"
      :properties-open="true"
      center-canvas
      :free-canvas="bundle"
      :interaction-active="interactionActive"
      :sidebar-widths="[280, 360]"
      :left-title="
        t(bundle ? (driven ? 'candidateTemplates' : electronic ? 'rules' : 'items') : 'library')
      "
      v-model="doc"
      :canvas-width="bundle ? 700 : paperSize.width * 3 + 264"
      :canvas-height="bundle ? 700 : designHeight + 56"
      @select="selection = $event"
    >
      <template #toolbar="{ zoom, setZoom, fit, leftOpen, rightOpen, toggleLeft, toggleRight }">
        <div class="studio-toolbar report-canvas-toolbar">
          <template v-if="electronic">
            <span>{{ t('stitchMode') }}</span>
            <el-radio-group
              :model-value="doc.assembly.stitchMode ?? 'Normal'"
              :aria-label="t('stitchMode')"
              @update:model-value="switchStitchMode(String($event))"
            >
              <el-radio-button value="Normal">{{ t('normalStitch') }}</el-radio-button>
              <el-radio-button value="DataDriven">{{ t('dataStitch') }}</el-radio-button>
            </el-radio-group>
          </template>
          <span v-if="!bundle" class="report-paper-size" :title="t('compactCanvasHint')"
            >{{ doc.paper?.size }} · {{ paperSize.width }} × {{ paperSize.height }} mm ·
            {{ t('compactCanvas') }}</span
          >
          <el-select
            v-if="!bundle"
            v-model="rulerUnit"
            class="report-ruler-unit-select"
            :aria-label="t('measurementUnit')"
          >
            <el-option value="mm" :label="t('unitMm')" />
            <el-option value="cm" :label="t('unitCm')" />
            <el-option value="in" :label="t('unitIn')" />
            <el-option value="px" :label="t('unitPx')" />
          </el-select>
          <el-slider
            v-if="!bundle"
            :model-value="Math.round(zoom * 100)"
            :min="10"
            :max="200"
            :aria-label="t('canvasZoom')"
            @update:model-value="setZoom(Number($event) / 100)"
          />
          <el-button v-if="!bundle" @click="fit">{{ t('fitCanvas') }}</el-button>
          <el-button v-if="!bundle" @click="setZoom(1)">100%</el-button>
          <span v-if="!bundle" class="report-zoom-value">{{ Math.round(zoom * 100) }}%</span>
          <el-button @click="drawer = 'data'">{{ t('data') }}</el-button>
          <el-button @click="select('')">{{ t('pageSettings') }}</el-button>
          <el-button @click="toggleLeft">{{
            t(
              bundle
                ? leftOpen
                  ? 'collapseStructure'
                  : 'expandStructure'
                : leftOpen
                  ? 'collapseLibrary'
                  : 'expandLibrary',
            )
          }}</el-button>
          <el-button @click="toggleRight">{{
            t(rightOpen ? 'collapseProperties' : 'expandProperties')
          }}</el-button>
          <small v-if="optionsLoading || datasetOptionsLoading" class="report-help">{{
            t('loadingOptions')
          }}</small>
          <template v-if="optionsError || datasetOptionsError"
            ><span role="status">{{ optionsError }}</span
            ><el-button link @click="loadOptions">{{ t('retryOptions') }}</el-button></template
          >
        </div>
      </template>
      <template #structure>
        <div class="report-panel-heading">
          <h3>
            {{
              t(
                bundle
                  ? driven
                    ? 'candidateTemplates'
                    : electronic
                      ? 'rules'
                      : 'items'
                  : 'library',
              )
            }}
          </h3>
          <el-tag size="small">{{ bundle ? children.length : reportControls.length }}</el-tag>
        </div>
        <el-tabs v-if="!bundle" v-model="tab" stretch>
          <el-tab-pane v-if="!bundle" :label="t('controls')" name="controls" />
          <el-tab-pane :label="t('structure')" name="structure" />
          <el-tab-pane v-if="!bundle" :label="t('fieldsTab')" name="fields" />
        </el-tabs>
        <template v-if="tab === 'controls' && !bundle">
          <el-input
            v-model="controlSearch"
            :placeholder="t('searchControls')"
            :aria-label="t('searchControls')"
            clearable
          />
          <div class="report-control-palette">
            <button
              v-for="entry in palette"
              :key="entry.type"
              type="button"
              draggable="true"
              @dragstart="startPaletteDrag($event, 'report', entry.type)"
              @keydown.enter.prevent="addControl(entry.type)"
              @keydown.space.prevent="addControl(entry.type)"
            >
              <el-icon><component :is="entry.icon" /></el-icon><span>{{ t(entry.label) }}</span>
            </button>
          </div>
          <el-form label-position="top"
            ><el-form-item :label="t('targetBand')"
              ><el-select :model-value="selection" @update:model-value="select"
                ><el-option
                  v-for="b in bands"
                  :key="b.id"
                  :value="b.id"
                  :label="b.name" /></el-select></el-form-item
          ></el-form>
          <p class="report-help">{{ t('controlHint') }}</p>
        </template>
        <template v-if="tab === 'structure' || bundle">
          <div class="studio-toolbar">
            <el-button @click="bundle ? addItem() : addBand()">{{ t('add') }}</el-button
            ><el-button :disabled="!selection" :title="t('moveUp')" @click="move(-1)">↑</el-button
            ><el-button :disabled="!selection" :title="t('moveDown')" @click="move(1)">↓</el-button>
            <el-button v-if="bundle" :disabled="!item" type="danger" link @click="removeItem">{{
              t('removeItem')
            }}</el-button>
          </div>
          <template v-if="!bundle">
            <div class="studio-toolbar">
              <el-button :disabled="!band" @click="addBand(true)">{{ t('child') }}</el-button
              ><el-button :disabled="!band" @click="removeBand">{{ t('deleteBand') }}</el-button>
            </div>
            <div v-for="b in bands" :key="b.id" class="report-tree-band">
              <button
                class="report-tree-row"
                :class="{ active: selection === b.id && !controlId }"
                @click="select(b.id)"
              >
                {{ b.parentBandId ? '↳ ' : '' }}{{ b.name }}<small>{{ bandLabel(b.kind) }}</small>
              </button>
              <button
                v-for="c in b.controls"
                :key="c.id"
                class="report-tree-row report-tree-control"
                :class="{ active: controlId === c.id }"
                @click="selectControl(b.id, c.id)"
              >
                {{ controlLabel(c.type) }} · {{ c.id.slice(0, 8) }}
              </button>
            </div>
          </template>
          <button
            v-for="i in children"
            v-else
            :key="i.id"
            class="report-tree-row"
            :class="{ active: selection === i.id }"
            @click="select(i.id)"
          >
            {{ childTitle(i) }}
          </button>
        </template>
        <template v-if="tab === 'fields' && !bundle"
          ><p class="report-help">{{ t('dragField') }}</p>
          <StudioFieldTree :sources="fieldSources" @select="chooseField"
        /></template>
      </template>
      <template #canvas="{ zoom }"
        ><ReportMeasuredCanvas
          v-if="!bundle"
          :width-mm="paperSize.width"
          :height-mm="designHeight / 3"
          :sections="rulerSections"
          :selected-section="controlId ? '' : selection"
          @select-section="select"
          @section-pointer="
            (id, event) => {
              const b = bands.find((b) => b.id === id)
              if (b) dragBand(event, b, zoom)
            }
          "
          :margin="doc.paper?.margin ?? {}"
          :unit="rulerUnit"
          :zoom="zoom"
          @margin-pointer="(side, event) => dragMargin(side, event, zoom)"
        >
          <div
            class="report-paper"
            @dragover="allowPaletteDrop($event, 'application/x-platform-studio-binding')"
            @drop.prevent="dropOnPaper($event, zoom)"
            :style="{
              width: paperSize.width * 3 + 'px',
              minHeight: designHeight + 'px',
              padding: ['top', 'right', 'bottom', 'left']
                .map((side) => Number(doc.paper?.margin?.[side] ?? 15) * 3 + 'px')
                .join(' '),
            }"
          >
            <section
              v-for="b in bands"
              :key="b.id"
              class="report-band"
              :data-band-id="b.id"
              :class="{ active: selection === b.id && !controlId }"
              :style="{
                minHeight: designBandHeight(b) * 3 + 'px',
              }"
              @click.self="select(b.id)"
            >
              <div
                v-for="c in b.controls"
                :key="c.id"
                class="report-control"
                role="button"
                tabindex="0"
                :aria-label="controlCaption(c)"
                @keydown.enter.self="selectControl(b.id, c.id)"
                @keydown.space.self.prevent="selectControl(b.id, c.id)"
                :class="{
                  active: controlId === c.id && !cellSelected,
                  'report-control-table': c.type === 'Table',
                  'report-control-line': c.type === 'Line' && !c.advanced,
                  'report-control-box': c.type === 'Box',
                }"
                :style="{
                  ...styleFor(c),
                  left: Number(c.xMm) * 3 + 'px',
                  top: Number(c.yMm) * 3 + 'px',
                  width: Number(c.widthMm) * 3 + 'px',
                  height: designControlHeight(c) * 3 + 'px',
                }"
                @pointerdown.stop="drag($event, b, c, zoom)"
                @click="selectControl(b.id, c.id)"
              >
                <ReportDesignTable
                  v-if="c.type === 'Table'"
                  :table="c.table"
                  :active="controlId === c.id && cellSelected"
                  :row="selectedRow"
                  :column-id="currentColumn?.id ?? ''"
                  :caption="controlCaption"
                  :column-style="styleFor"
                  :cell-style="
                    (row, column) => reportDesignStyle(resolvedCellStyle(c, row, column))
                  "
                  :range="controlId === c.id ? cellRange : undefined"
                  :row-heights="
                    tableRows(c.table).map((_: Json, r: number) => tableRowHeight(c, r))
                  "
                  :header-height="tableRowHeight(c, -1)"
                  :measure="measure"
                  @measured-height="(height) => (measuredTableHeights[c.id] = height)"
                  @select="(row, columnId, extend) => selectCell(b.id, c.id, row, columnId, extend)"
                  @edit-content="
                    (row, columnId, text) => editCellContent(b.id, c.id, row, columnId, text)
                  "
                  @action="tableAction"
                  @merge="mergeCells()"
                  @split="mergeCells(true)"
                  @resize-column="
                    (event, index) => resizeTablePart(event, b, c, index, 'column', zoom)
                  "
                  @resize-row="
                    (event, index, height) =>
                      resizeTablePart(event, b, c, index, 'row', zoom, height)
                  "
                  @select-table="selectControl(b.id, c.id)"
                  @move-table="drag($event, b, c, zoom)"
                />
                <ReportAdvancedDesign
                  v-else-if="isAdvancedControl(c.type) && (c.type !== 'Line' || c.advanced)"
                  :control="c"
                  :caption="controlCaption(c)"
                  :label="controlLabel(c.type)"
                />
                <span v-else>{{ controlCaption(c) }}</span>
              </div>
              <template
                v-if="
                  selection === b.id && control && !cellSelected && !isDocumentControl(control.type)
                "
              >
                <button
                  v-for="edge in resizeEdges"
                  :key="edge"
                  type="button"
                  class="report-control-resize"
                  :data-edge="edge"
                  :aria-label="`${t('resizeControl')} ${edge}`"
                  :title="t('dragHint')"
                  :style="resizePosition(control, edge)"
                  @pointerdown.stop.prevent="drag($event, b, control, zoom, edge)"
                  @click.stop
                />
                <output
                  class="report-geometry-readout"
                  :style="{
                    left: Number(control.xMm) * 3 + 'px',
                    top: (Number(control.yMm) + designControlHeight(control)) * 3 + 10 + 'px',
                  }"
                  >{{ rectReadout(control) }}</output
                >
              </template>
              <svg
                v-if="dragFeedback && dragFeedback.bandId === b.id"
                class="report-alignment-guides"
                :width="bodyWidth * 3"
                :height="designBandHeight(b) * 3"
              >
                <line
                  v-if="dragFeedback.guideX !== undefined"
                  :x1="dragFeedback.guideX * 3"
                  :x2="dragFeedback.guideX * 3"
                  y1="0"
                  y2="100%"
                />
                <line
                  v-if="dragFeedback.guideY !== undefined"
                  x1="0"
                  x2="100%"
                  :y1="dragFeedback.guideY * 3"
                  :y2="dragFeedback.guideY * 3"
                />
              </svg>
              <button
                class="report-band-resize"
                type="button"
                :aria-label="`${t('resizeBand')} · ${bandLabel(b.kind)} · ${b.name}`"
                :title="t('dragHint')"
                @pointerdown.stop.prevent="dragBand($event, b, zoom)"
                @click.stop
              />
            </section>
          </div>
        </ReportMeasuredCanvas>
      </template>
      <template #workspace>
        <div class="report-bundle-flow">
          <button
            v-for="i in children"
            :key="i.id"
            class="report-tree-row"
            :class="{ active: selection === i.id }"
            @click="select(i.id)"
          >
            {{ childTitle(i) }} · {{ revisionTitle(i) }}
            <small v-if="driven"
              >{{ i.scope === 'WorkOrder' ? t('workOrderScope') : t('operationScope') }} ·
              {{ i.required ? t('fixedTemplate') : t('optionalItem') }}</small
            >
            <small v-if="!driven"
              >{{ i.required ? t('required') : t('optionalItem') }} ·
              {{
                electronic
                  ? t(i.content?.mode === 'BandSubtree' ? 'bandSubtree' : 'wholeReport')
                  : t('independentPaper')
              }}
              ·
              {{
                t(
                  (i.breakBefore ?? (electronic ? 'Auto' : 'Page')) === 'Page'
                    ? 'breakPage'
                    : 'breakAuto',
                )
              }}
            </small>
          </button>
        </div></template
      >
      <template #properties>
        <div class="report-panel-heading">
          <h3>{{ t('properties') }}</h3>
          <el-button v-if="control && !cellSelected" link type="danger" @click="removeControl">{{
            t('deleteControl')
          }}</el-button>
        </div>
        <output v-if="tableSizeHint" class="report-table-size-hint">{{ tableSizeHint }}</output>
        <div class="report-selection-title">
          {{
            control
              ? cellSelected
                ? `${t('cellObject')} · ${selectedRow < 0 ? t('header') : t('templateRow') + ' ' + (selectedRow + 1)} · ${currentColumn?.label ?? ''}`
                : controlLabel(control.type)
              : band
                ? band.name
                : item
                  ? childTitle(item)
                  : t('pageObject')
          }}
        </div>
        <el-button v-if="cellSelected" link @click="selectControl(selection, controlId)">{{
          t('selectTable')
        }}</el-button>
        <p v-if="driven" class="report-help">{{ t('dataStitchHint') }}</p>
        <p v-if="control?.type === 'Table'" class="report-help">{{ t('tableSelectionHint') }}</p>
        <template v-if="!band && !item">
          <label v-if="bundle && !electronic"
            >{{ t('pageNumberMode')
            }}<el-select v-model="doc.pageNumberMode" :aria-label="t('pageNumberMode')">
              <el-option value="Preserve" :label="t('preservePages')" />
              <el-option value="Global" :label="t('globalPages')" /> </el-select
          ></label>
          <template v-if="doc.paper"
            ><el-select v-model="doc.paper.size" :aria-label="t('paper')"
              ><el-option
                v-for="v in ['A4', 'A3', 'Letter', 'Custom']"
                :key="v"
                :value="v"
                :label="v" /></el-select
            ><el-select v-model="doc.paper.orientation"
              ><el-option value="Portrait" :label="t('paperPortrait')" /><el-option
                value="Landscape"
                :label="t('paperLandscape')" /></el-select
            ><el-input-number
              v-if="doc.paper.size === 'Custom'"
              v-model="doc.paper.widthMm"
              :min="20"
              :max="2000" /><el-input-number
              v-if="doc.paper.size === 'Custom'"
              v-model="doc.paper.heightMm"
              :min="20"
              :max="2000" /><label v-for="v in ['top', 'right', 'bottom', 'left']" :key="v"
              >{{
                t(
                  (
                    {
                      top: 'topMargin',
                      right: 'rightMargin',
                      bottom: 'bottomMargin',
                      left: 'leftMargin',
                    } as const
                  )[v as 'top'],
                ).replace(' mm', '')
              }}
              {{ rulerUnit
              }}<el-input-number
                :model-value="reportUnitValue(Number(doc.paper.margin[v]), rulerUnit)"
                :step="rulerUnit === 'in' ? 0.05 : rulerUnit === 'cm' ? 0.1 : 1"
                :precision="4"
                :min="0"
                @update:model-value="
                  (value: number | undefined) => {
                    if (value != null) doc.paper.margin[v] = reportMillimetres(value, rulerUnit)
                  }
                " /></label
            ><label
              >{{ t('headerHeight')
              }}<el-input-number v-model="doc.paper.headerHeightMm" :min="0" /></label
            ><label
              >{{ t('footerHeight')
              }}<el-input-number v-model="doc.paper.footerHeightMm" :min="0" /></label
          ></template>

          <template v-if="!bundle"
            ><h3>{{ t('insertBand') }}</h3>
            <div class="report-band-palette">
              <el-button
                v-for="entry in reportBandKinds"
                :key="entry.kind"
                @click="addBand(false, entry.kind)"
                >{{ t(entry.label) }}</el-button
              >
            </div>
            <p class="report-help">{{ t('groupNote') }}</p></template
          >
          <p class="report-help">{{ t('selectHint') }}</p>
        </template>
        <template v-if="band && !bundle"
          ><el-form v-if="!control" label-position="top"
            ><el-form-item :label="t('name')"
              ><el-input
                :model-value="band.name"
                @update:model-value="updateBand('name', $event)" /></el-form-item
            ><el-form-item :label="t('kind')"
              ><el-select :model-value="band.kind" @update:model-value="setBandKind"
                ><el-option
                  v-for="entry in reportBandKinds"
                  :key="entry.kind"
                  :value="entry.kind"
                  :label="t(entry.label)" /></el-select></el-form-item
            ><el-form-item :label="t('parent')"
              ><el-select v-model="band.parentBandId" clearable
                ><el-option
                  v-for="b in bands.filter((b) => b.id !== band?.id)"
                  :key="b.id"
                  :value="b.id"
                  :label="b.name" /></el-select></el-form-item
            ><el-form-item :label="t('height')"
              ><el-input-number
                :model-value="band.heightMm"
                :min="1"
                @update:model-value="updateBand('heightMm', $event)" /></el-form-item
            ><ReportCollectionEditor
              v-if="band.kind === 'Detail'"
              v-model="band.data"
              :instances="ids"
              :datasets="datasets"
            /><el-select v-model="band.emptyBehavior" :aria-label="t('empty')"
              ><el-option value="Skip" label="Skip" /><el-option
                value="Label"
                label="Label" /></el-select
            ><el-input v-if="band.emptyBehavior === 'Label'" v-model="band.emptyText" />
            <template v-if="band.kind.startsWith('Group') && band.group"
              ><h4>{{ t('groupKeys') }}</h4>
              <div v-for="(key, index) in band.group.keys" :key="index">
                <BindingEditor
                  :model-value="key"
                  :instances="ids"
                  :contexts="reportContexts"
                  @update:model-value="
                    (value) => {
                      const keys = clone(band!.group.keys)
                      keys[index] = value
                      groupKeys(keys)
                    }
                  "
                /><el-button
                  link
                  @click="groupKeys(band!.group.keys.filter((_: Json, i: number) => i !== index))"
                  >{{ t('remove') }}</el-button
                >
              </div>
              <el-button @click="groupKeys([...band.group.keys, binding()])">{{
                t('add')
              }}</el-button
              ><el-select
                :model-value="band.group.pairedBandId"
                :aria-label="t('pairedBand')"
                @update:model-value="groupPair"
                ><el-option
                  v-for="other in bands.filter(
                    (b) =>
                      b.id !== band?.id &&
                      b.parentBandId === band?.parentBandId &&
                      b.kind === (band?.kind === 'GroupHeader' ? 'GroupFooter' : 'GroupHeader'),
                  )"
                  :key="other.id"
                  :value="other.id"
                  :label="other.name" /></el-select
            ></template>
            <el-checkbox v-model="band.grow">{{ t('grow') }}</el-checkbox>
            <el-checkbox v-model="band.keepWithNext">{{ t('keepNext') }}</el-checkbox>
            <el-checkbox v-if="band.kind === 'GroupHeader'" v-model="band.repeatOnPage">{{
              t('repeatBand')
            }}</el-checkbox>
          </el-form>
          <template v-if="control">
            <el-tabs v-if="!cellSelected" v-model="inspectorTab" stretch
              ><el-tab-pane :label="t('basic')" name="basic" /><el-tab-pane
                :label="t('content')"
                name="content" /><el-tab-pane :label="t('appearance')" name="appearance"
            /></el-tabs>
            <p
              v-if="!cellSelected && inspectorTab === 'basic' && isDocumentControl(control.type)"
              class="studio-muted"
            >
              {{ t('documentHint') }}
            </p>
            <div
              v-if="!cellSelected && inspectorTab === 'basic' && !isDocumentControl(control.type)"
              class="report-geometry"
            >
              <label v-for="key in ['xMm', 'yMm', 'widthMm', 'heightMm']" :key="key"
                >{{
                  t(
                    ({ xMm: 'x', yMm: 'y', widthMm: 'width', heightMm: 'controlHeight' } as const)[
                      key as 'xMm'
                    ],
                  )
                }}<el-input-number
                  :model-value="control[key]"
                  :min="key === 'widthMm' || key === 'heightMm' ? 0.1 : 0"
                  @update:model-value="updateControl(key, $event)"
              /></label>
              <el-checkbox v-model="control.canGrow">{{ t('grow') }}</el-checkbox
              ><el-checkbox v-model="control.keepTogether">{{ t('keepTogether') }}</el-checkbox>
              <el-form label-position="top"
                ><el-form-item :label="t('follow')"
                  ><el-select
                    :model-value="control.flow?.mode ?? 'Follow'"
                    @update:model-value="
                      updateControl('flow', { afterControlIds: [], ...control!.flow, mode: $event })
                    "
                    ><el-option value="Follow" :label="t('followFlow')" /><el-option
                      value="Overlay"
                      :label="t('overlayFlow')" /></el-select></el-form-item
                ><el-form-item :label="t('dependencies')"
                  ><el-select
                    :model-value="control.flow?.afterControlIds ?? []"
                    multiple
                    @update:model-value="
                      updateControl('flow', {
                        mode: 'Follow',
                        ...control!.flow,
                        afterControlIds: $event,
                      })
                    "
                    ><el-option
                      v-for="c in band.controls.filter((c: Json) => c.id !== controlId)"
                      :key="c.id"
                      :value="c.id"
                      :label="
                        controlLabel(c.type) + ' · ' + c.id.slice(0, 8)
                      " /></el-select></el-form-item
              ></el-form>
            </div>
            <template v-if="cellSelected || inspectorTab === 'content'"
              ><el-checkbox
                v-if="['Text', 'Field'].includes(String(control.type))"
                :model-value="!!control.aggregate"
                @update:model-value="aggregate"
                >{{ t('aggregate') }}</el-checkbox
              ><template v-if="control.aggregate"
                ><el-select v-model="control.aggregate.function" :aria-label="t('aggregate')"
                  ><el-option
                    v-for="value in ['Count', 'Sum', 'Avg', 'Min', 'Max']"
                    :key="value"
                    :value="value"
                    :label="value" /></el-select
                ><el-select v-model="control.aggregate.scopeBandId" :aria-label="t('scope')"
                  ><el-option
                    v-for="b in bands.filter((b) => b.kind === 'Detail')"
                    :key="b.id"
                    :value="b.id"
                    :label="b.name" /></el-select
                ><BindingEditor
                  v-if="control.aggregate.function !== 'Count'"
                  v-model="control.aggregate.field"
                  :instances="ids"
                  :contexts="reportContexts"
                  :paths="
                    schemaPaths(schemaContract.bandSchemas?.[control.aggregate.scopeBandId] ?? {})
                  " /></template
              ><template v-if="['Image', 'PdfContent'].includes(control.type)"
                ><el-select
                  :model-value="control.value?.value"
                  :aria-label="t(control.type === 'PdfContent' ? 'pdfAsset' : 'imageAsset')"
                  @update:model-value="
                    (id: string) =>
                      (control!.value = { from: 'Constant', value: id, nullPolicy: 'Error' })
                  "
                  ><el-option
                    v-for="asset in (doc.assets ?? []).filter(
                      (a: Json) =>
                        (a.mediaType === 'application/pdf') === (control!.type === 'PdfContent'),
                    )"
                    :key="asset.id"
                    :value="asset.id"
                    :label="asset.id" /></el-select
                ><StudioFileField
                  v-model="assetCandidate"
                  :model-n-id="nId"
                  module-key="Report"
                  purpose="TemplateAsset"
                  :accept="
                    control.type === 'PdfContent'
                      ? 'application/pdf'
                      : 'image/png,image/jpeg,image/gif,image/webp'
                  "
                  :disabled="busy"
                /><el-button v-if="assetCandidate" :loading="busy" @click="attachImage">{{
                  t(control.type === 'PdfContent' ? 'attachAsset' : 'attachImage')
                }}</el-button></template
              ><BindingEditor
                v-else-if="control.value && !cellSelected"
                v-model="control.value"
                :instances="ids"
                :contexts="reportContexts"
                :bands="ancestorBands.map((b) => ({ id: String(b.id), name: String(b.name) }))"
                :paths="bindingPaths(control.value)"
              />
              <ReportDocumentEditor
                :model-value="control"
                :reports="reports"
                :instances="ids"
                :resource-n-id="nId"
                :allow-outline="!['PageHeader', 'PageFooter'].includes(band.kind)"
                @update:model-value="updateAdvancedControl"
              />
              <ReportAdvancedEditor
                v-if="isAdvancedControl(control.type) && !cellSelected"
                :model-value="control"
                @update:model-value="updateAdvancedControl"
              />
              <template v-if="control.type === 'Table'">
                <template v-if="!cellSelected"
                  ><el-select
                    :model-value="control.table.rowMode"
                    :aria-label="t('rowMode')"
                    @update:model-value="
                      (v: string) => {
                        control!.table.rowMode = v
                        if (v === 'Collection') control!.value = binding()
                        else delete control!.value
                      }
                    "
                    ><el-option value="BandRow" :label="t('bandRowMode')" /><el-option
                      value="Collection"
                      :label="t('collectionRowMode')" /></el-select
                  ><el-form label-position="top"
                    ><el-form-item :label="t('header')"
                      ><el-select v-model="control.table.headerMode" :aria-label="t('header')"
                        ><el-option
                          v-for="v in ['OncePerParent', 'EveryInstance', 'Hidden'] as const"
                          :key="v"
                          :value="v"
                          :label="t(v)" /></el-select></el-form-item
                  ></el-form>
                  <p class="studio-muted">{{ t('tableHeaderHint') }}</p>
                  <el-checkbox v-model="control.table.repeatHeader">{{ t('repeat') }}</el-checkbox>
                </template>
                <p class="studio-muted">{{ t('tableCellHint') }}</p>
                <template v-if="cellSelected">
                  <el-form label-position="top"
                    ><el-form-item :label="t('templateRow')">
                      <el-select v-model="selectedRow" :aria-label="t('templateRow')">
                        <el-option
                          v-if="control.table.headerMode !== 'Hidden'"
                          :value="-1"
                          :label="t('header')"
                        />
                        <el-option
                          v-for="(r, index) in tableRows(control.table)"
                          :key="r.id"
                          :value="index"
                          :label="`${t('templateRow')} ${Number(index) + 1}`"
                        />
                      </el-select> </el-form-item
                    ><el-form-item :label="t('selectedColumn')"
                      ><el-select
                        :model-value="currentColumn?.id"
                        @update:model-value="selectedColumnId = $event"
                        ><el-option
                          v-for="col in control.table.columns"
                          :key="col.id"
                          :value="col.id"
                          :label="col.label" /></el-select></el-form-item
                  ></el-form>
                  <div
                    v-for="col in currentColumn ? [currentColumn] : []"
                    :key="col.id"
                    class="report-column-editor"
                  >
                    <el-form label-position="top"
                      ><el-form-item :label="t('columnTitle')"
                        ><el-input v-model="col.label" :aria-label="t('columnTitle')"
                      /></el-form-item>
                      <el-form-item :label="t('columnWidth')"
                        ><el-input-number
                          v-model="col.widthMm"
                          :min="1"
                          :aria-label="t('columnWidth')" /></el-form-item
                    ></el-form>
                    <BindingEditor
                      v-if="selectedRow >= 0 && currentCell"
                      v-model="currentCell.value"
                      :instances="ids"
                      :contexts="reportContexts"
                      :bands="
                        ancestorBands.map((b) => ({ id: String(b.id), name: String(b.name) }))
                      "
                      :paths="bindingPaths(currentCell?.value)"
                    />
                    <el-form label-position="top"
                      ><el-form-item :label="t('columnStyle')"
                        ><el-select v-model="col.styleId" clearable :aria-label="t('columnStyle')"
                          ><el-option
                            v-for="style in doc.styles"
                            :key="style.id"
                            :value="style.id"
                            :label="style.id" /></el-select></el-form-item
                    ></el-form>
                    <el-button
                      link
                      :disabled="tableHasMerges || control.table.columns.indexOf(col) === 0"
                      @click="columnAction(control.table.columns.indexOf(col), -1)"
                      >↑</el-button
                    ><el-button
                      link
                      :disabled="
                        tableHasMerges ||
                        control.table.columns.indexOf(col) === control.table.columns.length - 1
                      "
                      @click="columnAction(control.table.columns.indexOf(col), 1)"
                      >↓</el-button
                    ><el-button link @click="columnAction(control.table.columns.indexOf(col), 0)">{{
                      t('deleteColumn')
                    }}</el-button>
                  </div>
                  <el-button
                    :disabled="Number(currentColumn?.widthMm) < 2"
                    @click="tableAction('columnAfter')"
                    >{{ t('columnAfter') }}</el-button
                  >
                  <el-divider>{{ t('cellAppearance') }}</el-divider>
                  <p class="report-help">{{ t('mergeHint') }}</p>
                  <p
                    v-if="
                      cellRange &&
                      (cellRange.bottom > cellRange.top || cellRange.right > cellRange.left)
                    "
                    class="report-help"
                  >
                    {{ t('batchCellStyle') }}
                  </p>
                  <ReportStyleEditor
                    :model-value="currentCellStyle"
                    @patch="updateCellStyle"
                  /> </template
              ></template> </template
          ></template>
          <template v-if="control && !cellSelected && inspectorTab === 'appearance'">
            <el-form label-position="top"
              ><el-form-item :label="t('styles')"
                ><el-select v-model="control.styleId"
                  ><el-option
                    v-for="style in doc.styles"
                    :key="style.id"
                    :value="style.id"
                    :label="style.id" /></el-select></el-form-item
            ></el-form>
            <p class="report-help">{{ t('styleNote') }}</p>
            <ReportStyleEditor
              v-if="currentStyle"
              :model-value="currentStyle"
              @update:model-value="
                (value: Json) =>
                  edit(
                    (d) =>
                      (d.styles[d.styles.findIndex((s: Json) => s.id === currentStyle!.id)] =
                        value),
                  )
              "
            />
            <el-button
              @click="
                edit((d) => {
                  const id = `style-${newId().slice(0, 8)}`
                  d.styles.push({
                    ...(currentStyle ?? {
                      fontFamily: 'sans-serif',
                      fontSizePt: 10,
                      fontWeight: 400,
                      color: '#000000',
                      textAlign: 'Left',
                      lineHeight: 1.3,
                    }),
                    id,
                  })
                  d.bands
                    .find((b: Json) => b.id === selection)
                    .controls.find((c: Json) => c.id === controlId).styleId = id
                })
              "
              >{{ t('add') }}</el-button
            >
          </template>
        </template>
        <template v-if="item && bundle">
          <el-tabs v-model="inspectorTab" stretch>
            <el-tab-pane :label="t('basic')" name="basic" />
            <el-tab-pane :label="t('bundleData')" name="data" />
            <el-tab-pane :label="t('bundleLayout')" name="layout" />
          </el-tabs>
          <el-form label-position="top" class="report-bundle-properties">
            <template v-if="inspectorTab === 'basic'">
              <el-form-item v-if="electronic && !driven" :label="t('name')"
                ><el-input v-model="item.name" :aria-label="t('name')"
              /></el-form-item>
              <el-form-item :label="t('template')"
                ><el-select-v2
                  :model-value="item.reportNId"
                  :options="reportOptions"
                  :placeholder="t('chooseReport')"
                  filterable
                  :aria-label="t('template')"
                  @update:model-value="setTemplate"
              /></el-form-item>
              <el-form-item :label="t('revision')"
                ><el-input
                  :model-value="item.revisionNId"
                  :aria-label="t('revision')"
                  @change="(value: string) => (item!.revisionNId = value)"
              /></el-form-item>
              <el-form-item
                ><el-checkbox
                  :model-value="!!draftChildren[item.reportNId]"
                  @update:model-value="(value: boolean) => selectChildDraft(value)"
                  >{{ t('useChildDraft') }}</el-checkbox
                ></el-form-item
              >
              <el-button link @click="editChild">{{ t('editChild') }}</el-button>
              <el-form-item v-if="!electronic"
                ><el-checkbox v-model="item.enabled">{{ t('enabled') }}</el-checkbox></el-form-item
              >
              <el-form-item v-if="driven" :label="t('templateScope')"
                ><el-select
                  :model-value="item.scope ?? 'Operation'"
                  :aria-label="t('templateScope')"
                  @update:model-value="(value: string) => (item!.scope = value)"
                  ><el-option value="WorkOrder" :label="t('workOrderScope')" /><el-option
                    value="Operation"
                    :label="t('operationScope')" /></el-select
              ></el-form-item>
              <el-form-item
                ><el-checkbox v-model="item.required">{{
                  driven ? t('fixedTemplate') : t('required')
                }}</el-checkbox></el-form-item
              >
              <p v-if="driven" class="report-help">{{ t('fixedTemplateHint') }}</p>
            </template>
            <template v-if="inspectorTab === 'data'">
              <ReportMappings
                v-if="driven"
                :model-value="item.instanceMap ?? []"
                @update:model-value="(value: Json[]) => (item!.instanceMap = value)"
                :instances="ids"
                :label="t('snapshotMapping')"
                :target-label="t('snapshotTarget')"
              />
              <p v-if="driven" class="report-help">{{ t('snapshotMappingHint') }}</p>
              <ReportMappings v-model="item.parameterMap" :instances="ids" :label="t('params')" />
              <template v-if="electronic && !driven">
                <el-select
                  :model-value="item.repeat ? 'repeat' : 'presence'"
                  :aria-label="t('output')"
                  @update:model-value="
                    (v: string) => {
                      const old = item!.repeat ?? item!.presence
                      delete item!.repeat
                      delete item!.presence
                      item![v] = old
                    }
                  "
                  ><el-option value="repeat" label="0..N" /><el-option
                    value="presence"
                    label="1" /></el-select
                ><ReportCollectionEditor
                  v-if="item.repeat"
                  v-model="item.repeat"
                  :instances="ids"
                  :datasets="datasets"
                /><ReportCollectionEditor
                  v-else
                  v-model="item.presence"
                  :instances="ids"
                  :datasets="datasets"
                /><el-select
                  :model-value="item.content.mode"
                  :aria-label="t('entry')"
                  @update:model-value="
                    (v: string) =>
                      (item!.content =
                        v === 'WholeReport'
                          ? { mode: v }
                          : { mode: v, entryBandId: '', rowMap: [], ancestorMap: [] })
                  "
                  ><el-option value="WholeReport" :label="t('wholeReport')" /><el-option
                    value="BandSubtree"
                    :label="t('bandSubtree')" /></el-select
                ><template v-if="item.content.mode === 'BandSubtree'"
                  ><el-select
                    v-model="item.content.entryBandId"
                    :aria-label="t('entryBand')"
                    :loading="contractLoading"
                    ><el-option
                      v-for="b in contract?.entryBands ?? []"
                      :key="b.id"
                      :value="b.id"
                      :label="b.name"
                  /></el-select>
                  <el-alert
                    v-if="contractError"
                    :title="contractError"
                    type="error"
                    :closable="false"
                  />
                  <div v-for="(map, index) in item.content.rowMap" :key="index">
                    <el-input
                      :aria-label="`${t('rowMap')} ${Number(index) + 1}`"
                      :model-value="map.targetPath.join('.')"
                      @update:model-value="
                        (v: string) => (map.targetPath = v.split('.').filter(Boolean))
                      "
                    /><BindingEditor
                      v-model="map.source"
                      :instances="ids"
                      :contexts="reportContexts"
                    />
                    <el-button
                      link
                      type="danger"
                      :aria-label="`${t('removeMapping')} · ${t('rowMap')} ${Number(index) + 1}`"
                      @click="item.content.rowMap.splice(index, 1)"
                      >{{ t('removeMapping') }}</el-button
                    >
                  </div>
                  <el-button
                    @click="item.content.rowMap.push({ targetPath: [], source: binding() })"
                    >{{ t('rowMap') }} +</el-button
                  >
                  <div v-for="(map, index) in item.content.ancestorMap" :key="index">
                    <el-input
                      v-model="map.bandId"
                      :aria-label="`${t('ancestorMap')} ${Number(index) + 1}`"
                    /><BindingEditor
                      v-model="map.source"
                      :instances="ids"
                      :contexts="reportContexts"
                    />
                    <el-button
                      link
                      type="danger"
                      :aria-label="`${t('removeMapping')} · ${t('ancestorMap')} ${Number(index) + 1}`"
                      @click="item.content.ancestorMap.splice(index, 1)"
                      >{{ t('removeMapping') }}</el-button
                    >
                  </div>
                  <el-button
                    @click="item.content.ancestorMap.push({ bandId: '', source: binding('Root') })"
                    >{{ t('ancestorMap') }} +</el-button
                  ></template
                >
              </template>
            </template>
            <template v-if="inspectorTab === 'layout'">
              <el-form-item v-if="electronic" :label="t('breakBefore')"
                ><el-select
                  :model-value="item.breakBefore ?? 'Auto'"
                  :aria-label="t('breakBefore')"
                  @update:model-value="(value: string) => (item!.breakBefore = value)"
                  ><el-option value="Auto" :label="t('breakAuto')" /><el-option
                    value="Page"
                    :label="t('breakPage')" /></el-select
              ></el-form-item>
              <p v-else class="report-help">{{ t('independentPaper') }} · {{ t('breakPage') }}</p>
            </template>
          </el-form>
        </template>
      </template>
    </StudioEditorShell>
    <AppFormDrawer
      :model-value="drawer === 'data'"
      :title="t('data')"
      size="extra-wide"
      :hide-footer="true"
      @update:model-value="closeData"
    >
      <el-tabs v-if="drawer === 'data'" v-model="dataTab" class="report-data-tabs">
        <el-tab-pane :label="t('dataInstances')" name="instances">
          <p class="report-data-hint">{{ t('instanceDataHint') }}</p>
          <el-collapse v-model="dataInstance" accordion>
            <el-collapse-item v-for="(i, index) in instances" :key="index" :name="String(index)">
              <template #title
                ><span class="report-instance-title">{{ i.instanceId || t('instance') }}</span
                ><el-tag size="small" type="info">{{
                  t(i.sourceType === 'Constant' ? 'constantSource' : 'datasetSource')
                }}</el-tag></template
              >
              <el-form label-position="top">
                <div class="report-data-fields">
                  <el-form-item :label="t('instance')"
                    ><el-input v-model="i.instanceId" :aria-label="t('instance')"
                  /></el-form-item>
                  <el-form-item :label="t('source')">
                    <el-select
                      :model-value="i.sourceType ?? 'Dataset'"
                      :aria-label="t('source')"
                      @update:model-value="setInstanceSource(i, $event)"
                    >
                      <el-option value="Dataset" :label="t('datasetSource')" />
                      <el-option value="Constant" :label="t('constantSource')" />
                    </el-select>
                  </el-form-item>
                </div>
                <DashboardConstantEditor
                  v-if="i.sourceType === 'Constant'"
                  :ref="(el) => setConstantEditor(index, el)"
                  v-model="i.value"
                  json-only
                />
                <template v-else>
                  <el-form-item :label="t('datasetSource')"
                    ><el-select
                      :model-value="i.datasetNId"
                      :aria-label="t('datasetSource')"
                      @update:model-value="setDataset(i, $event)"
                      ><el-option
                        v-for="d in datasets"
                        :key="d.nId"
                        :value="d.nId"
                        :label="d.name" /></el-select
                  ></el-form-item>
                  <small>{{ i.revisionNId }}</small>
                  <ReportMappings v-model="i.parameters" :instances="ids" :label="t('params')" />
                </template>
              </el-form>
            </el-collapse-item>
          </el-collapse>
          <el-button class="report-data-add" @click="addInstance">{{ t('addInstance') }}</el-button>
        </el-tab-pane>
        <el-tab-pane :label="t('params')" name="parameters">
          <el-form label-position="top">
            <div v-for="(p, i) in doc.parameters" :key="i" class="report-data-fields">
              <el-form-item :label="t('name')"
                ><el-input v-model="p.name" :aria-label="t('name')"
              /></el-form-item>
              <el-form-item :label="t('parameterType')"
                ><el-select v-model="p.type" :aria-label="t('parameterType')"
                  ><el-option
                    v-for="v in ['String', 'Int32', 'Decimal', 'Date', 'Boolean', 'Array']"
                    :key="v"
                    :value="v"
                    :label="parameterTypeLabels[v]" /></el-select
              ></el-form-item>
              <el-checkbox v-model="p.required">{{ t('required') }}</el-checkbox>
            </div>
          </el-form>
          <el-button
            @click="
              edit((d) =>
                d.parameters.push({
                  name: '',
                  label: '',
                  type: 'String',
                  required: false,
                  nullable: true,
                }),
              )
            "
            >{{ t('addParameter') }}</el-button
          >
        </el-tab-pane>
        <el-tab-pane v-if="electronic" :label="t('collectionBindings')" name="collections">
          <h3>{{ t('orderCollection') }}</h3>
          <ReportCollectionEditor
            v-model="doc.assembly.root"
            :instances="ids"
            :datasets="datasets"
          />
          <h3>{{ t('operationCollection') }}</h3>
          <ReportCollectionEditor
            v-model="doc.assembly.operation.collection"
            :instances="ids"
            :datasets="datasets"
          />
        </el-tab-pane>
        <el-tab-pane v-if="!bundle" :label="t('querySettings')" name="query">
          <ReportQueryEditor
            v-model="doc"
            :datasets="datasets"
            :dataset-schemas="schemaContract.datasetSchemas"
          />
        </el-tab-pane>
      </el-tabs>
    </AppFormDrawer>
    <el-dialog
      v-if="drawer === 'preview'"
      :model-value="true"
      fullscreen
      append-to-body
      class="report-preview-dialog"
      :title="t('preview')"
      :show-close="false"
      @close="closePreview"
    >
      <ReportPreviewPanel
        :preview="result?.pages ? result : undefined"
        :preview-n-id="result?.previewNId ? String(result.previewNId) : undefined"
      >
        <template #actions>
          <ReportPreviewExport v-if="previewExportInput" :input="previewExportInput" />
          <el-popover
            v-if="doc.parameters?.length"
            trigger="click"
            :width="300"
            placement="bottom-end"
          >
            <template #reference
              ><el-button>{{ t('previewParameters') }}</el-button></template
            >
            <label v-for="p in doc.parameters" :key="p.name"
              >{{ p.label || p.name }}<el-input v-model="parameters[p.name]"
            /></label>
            <el-button :loading="busy" @click="execute('preview')">{{ t('query') }}</el-button>
          </el-popover>
          <el-button
            class="exit-preview"
            :title="t('exitPreview')"
            :aria-label="t('exitPreview')"
            @click="closePreview"
            >×</el-button
          >
        </template>
        <el-alert v-if="error" :title="error" type="error" :closable="false" />
        <el-button v-if="error && !busy" @click="execute('preview')">{{ t('query') }}</el-button>
        <div v-if="busy" class="preview-loading" role="status">{{ t('running') }}</div>
      </ReportPreviewPanel>
    </el-dialog>
    <AppFormDrawer
      :model-value="drawer === 'plan'"
      :title="t('plan')"
      size="extra-wide"
      :hide-footer="true"
      @update:model-value="(v: boolean) => !v && (drawer = '')"
    >
      <label v-for="p in doc.parameters" :key="p.name"
        >{{ p.label || p.name }}<el-input v-model="parameters[p.name]"
      /></label>
      <el-button :loading="busy" @click="execute('plan')">{{ t('query') }}</el-button>
      <template v-if="result"
        ><AppDataTable
          table-key="studio.report.plan"
          row-key="instanceKey"
          :rows="result.instances ?? result.assemblyPlan?.instances ?? []"
          :columns="[
            { field: 'ruleId', title: t('rules') },
            { field: 'workOrderKey', title: t('workOrderKey') },
            { field: 'operationKey', title: t('operationKey') },
            { field: 'decision', title: t('status') },
            { field: 'rowCount', title: t('rowCount') },
            { field: 'reason', title: t('reason') },
          ]" /><el-alert
          v-for="(diagnostic, i) in result.diagnostics ?? []"
          :key="i"
          :title="diagnostic.message || diagnostic.code"
          :type="diagnostic.severity === 'Error' ? 'error' : 'warning'"
          :closable="false"
      /></template>
      <el-alert v-if="error" :title="error" type="error" :closable="false"
    /></AppFormDrawer>
  </AppPage>
</template>
<style scoped>
.report-data-hint {
  color: var(--el-text-color-secondary);
  margin: 0 0 16px;
}
.report-instance-title {
  overflow-wrap: anywhere;
  margin-right: 12px;
}
.report-data-fields {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 12px;
  align-items: center;
}
.report-data-add {
  margin-top: 16px;
}
.report-data-tabs :deep(.el-collapse-item__header) {
  line-height: 1.5;
}
@media (max-width: 640px) {
  .report-data-fields {
    grid-template-columns: 1fr;
  }
}

.report-designer {
  height: 100%;
  min-height: 0;
}
.report-designer :deep(.app-page__body),
.report-design-shell {
  display: flex;
  flex-direction: column;
  flex: 1 1 0;
  min-height: 0;
}
.report-design-shell :deep(.studio-editor) {
  flex: 1 1 0;
  height: auto;
  min-height: 0;
}
.report-canvas-toolbar {
  flex-shrink: 0;
}
@media (min-width: 1025px) and (max-width: 1280px) {
  .report-design-shell :deep(.studio-editor:not(.left-hidden):not(.right-hidden)) {
    grid-template-columns: var(--studio-left) minmax(0, 1fr) var(--studio-right);
  }
  .report-design-shell :deep(.studio-editor.left-hidden:not(.right-hidden)) {
    grid-template-columns: 0 minmax(0, 1fr) var(--studio-right);
  }
  .report-design-shell :deep(.studio-sidebar:last-child) {
    position: relative;
    width: auto;
    box-shadow: none;
    z-index: auto;
  }
}

:global(.el-dialog.report-preview-dialog) {
  padding: 0;
  overflow: hidden;
  height: 100dvh;
  margin: 0;
  width: 100vw;
  max-width: none;
}
:global(.report-preview-dialog > .el-dialog__header) {
  display: none;
}
:global(.report-preview-dialog > .el-dialog__body) {
  height: 100%;
}
.exit-preview {
  margin-left: auto !important;
}
.preview-loading {
  padding: 12px;
  text-align: center;
}
:deep(.studio-sidebar .el-tabs__item) {
  padding-inline: 12px;
  font-size: 13px;
}
.report-panel-heading {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 8px;
  margin-bottom: 12px;
}
.report-panel-heading h3 {
  margin: 0;
  font-size: 16px;
}
.report-selection-title {
  padding: 10px 12px;
  margin-bottom: 12px;
  background: var(--el-fill-color-light);
  border: 1px solid var(--el-border-color-lighter);
  border-radius: var(--el-border-radius-base);
  overflow-wrap: anywhere;
  font-weight: 600;
}
.report-control-palette,
.report-band-palette,
.report-geometry {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 8px;
  margin-block: 12px;
}
.report-control-palette button {
  min-height: 76px;
  display: flex;
  flex-direction: column;
  align-items: flex-start;
  justify-content: center;
  gap: 8px;
  padding: 12px;
  border: 1px solid var(--el-border-color-lighter);
  border-radius: var(--el-border-radius-base);
  background: var(--el-fill-color-lighter);
  color: var(--el-text-color-primary);
  cursor: pointer;
  text-align: left;
}
.report-control-palette button:hover,
.report-control-palette button:focus-visible {
  border-color: var(--el-color-primary);
  color: var(--el-color-primary);
}
.report-control-palette .el-icon {
  font-size: 22px;
}
.report-band-palette .el-button {
  margin: 0;
  padding-inline: 6px;
}
.report-help {
  font-size: 12px;
  color: var(--el-text-color-secondary);
  line-height: 1.7;
}
.report-geometry .el-form {
  grid-column: 1 / -1;
}
.report-geometry label {
  margin: 0;
  font-size: 12px;
}
.report-geometry :deep(.el-input-number) {
  margin-top: 8px;
}
.report-column-editor {
  padding: 12px;
  background: var(--el-fill-color-lighter);
  border-radius: var(--el-border-radius-base);
}
.report-column-editor > :deep(*) {
  margin-bottom: 8px;
}
.report-tree-band {
  margin-top: 10px;
}
.report-tree-control {
  padding-left: 20px;
  font-size: 12px;
}
.report-canvas-toolbar {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  padding: 12px;
  background: var(--el-bg-color);
  border: 1px solid var(--el-border-color-light);
  margin-bottom: 8px;
  gap: 12px;
}
.report-canvas-toolbar :deep(.el-slider) {
  width: 140px;
  margin-inline: 8px;
}
.report-ruler-unit-select {
  width: 145px;
}
.report-canvas-toolbar :deep(.el-button + .el-button) {
  margin-left: 0;
}
.report-paper-size,
.report-zoom-value {
  white-space: nowrap;
  font-size: 13px;
}
.report-tree-row {
  display: block;
  width: 100%;
  padding: 8px;
  text-align: left;
  background: var(--el-bg-color);
  color: var(--el-text-color-primary);
  border: 1px solid var(--el-border-color);
  cursor: pointer;
}
.report-tree-row small {
  display: block;
  overflow-wrap: anywhere;
}
.active {
  outline: 2px solid var(--el-color-primary);
}
.report-paper {
  position: relative;
  box-sizing: border-box;
  background: white;
  color: #171717;
}
.report-band {
  position: relative;
  outline: 1px dashed #d5d9df;
}
.report-band.active {
  outline: 2px solid var(--el-color-primary);
  outline-offset: -1px;
  background-color: color-mix(in srgb, var(--el-color-primary) 4%, transparent);
}
.report-band.active > .report-band-resize {
  border-top: 2px solid var(--el-color-primary);
}
.report-band-resize {
  position: absolute;
  left: 0;
  right: 0;
  bottom: -3px;
  height: 6px;
  padding: 0;
  border: 0;
  background: transparent;
  border-top: 1px dashed #869bad;
  cursor: ns-resize;
  z-index: 3;
  touch-action: none;
}
.report-band-resize:hover {
  background: #79a7c366;
}
.report-control-resize {
  position: absolute;
  z-index: 6;
  width: 8px;
  height: 8px;
  padding: 0;
  border: 1px solid #156fa0;
  background: white;
  transform: translate(-50%, -50%);
  touch-action: none;
}
.report-geometry-readout {
  position: absolute;
  z-index: 7;
  pointer-events: none;
  white-space: nowrap;
  color: #fff;
  background: #285670ed;
  border-radius: 3px;
  font: 11px sans-serif;
  padding: 4px 6px;
  box-shadow: 0 1px 3px #0002;
}
.report-alignment-guides {
  position: absolute;
  inset: 0;
  pointer-events: none;
  z-index: 5;
  overflow: visible;
}
.report-alignment-guides line {
  stroke: #dc4792;
  stroke-dasharray: 4 2;
  stroke-width: 1;
}
.report-control {
  position: absolute;
  box-sizing: border-box;
  background: transparent;
  border: 0;
  outline: 1px dotted #c5cbd2;
  padding: 0;
  color: #171717;
  overflow: hidden;
  cursor: move;
  touch-action: none;
  white-space: pre-wrap;
  overflow-wrap: anywhere;
}
.report-control.active {
  outline: 2px solid var(--el-color-primary);
}
.report-control-table {
  overflow: visible;
}
.report-bundle-flow {
  box-sizing: border-box;
  width: 100%;
  max-width: 900px;
  padding: 20px;
  margin-inline: auto;
}
.report-bundle-flow .report-tree-row {
  overflow-wrap: anywhere;
}
.report-control-line {
  background: transparent;
  border-color: transparent;
}
.report-control-line::before {
  content: '';
  position: absolute;
  inset: 50% 0 auto;
  border-top: 1px solid currentColor;
}
.report-control-box {
  background: transparent;
  border: 1px solid currentColor;
}
label {
  display: block;
  margin-block: 8px;
}
</style>
