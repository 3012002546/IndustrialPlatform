<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import type { CSSProperties } from 'vue'
import type { Json } from '@/api/studio'
import { tableRows, tableCells, type CellRange, type TableAction } from './tableEditing'
import { useReportCopy } from './copy'
const props = defineProps<{
  table: Json
  active: boolean
  row: number
  columnId: string
  caption: (cell: Json) => string
  columnStyle: (column: Json) => CSSProperties
  cellStyle?: (row: number, column: Json) => CSSProperties
  range?: CellRange | undefined
  rowHeights?: number[]
  headerHeight?: number
  measure?: (mm: number) => string
}>()
const emit = defineEmits<{
  select: [row: number, columnId: string, extend?: boolean]
  action: [action: TableAction]
  'select-table': []
  'move-table': [event: PointerEvent]
  merge: []
  split: []
  'resize-column': [event: PointerEvent, index: number]
  'resize-row': [event: PointerEvent, row: number, heightMm: number]
  'measured-height': [heightMm: number]
  'edit-content': [row: number, columnId: string, text: string]
}>()
const t = useReportCopy()
const editing = ref<{ row: number; column: number; original: string; text: string }>()
async function beginEdit(row: number, column: number, cell?: Json) {
  emit('select', row, props.table.columns[column].id, false)
  const text = row < 0 ? String(props.table.columns[column].label ?? '') : props.caption(cell ?? {})
  editing.value = { row, column, original: text, text }
  await nextTick()
  const input = tableElement.value?.querySelector<HTMLInputElement>('.report-cell-editor')
  input?.focus()
  input?.select()
}
function finishEdit(cancel = false) {
  const current = editing.value
  editing.value = undefined
  if (!cancel && current && current.text !== current.original)
    emit('edit-content', current.row, props.table.columns[current.column].id, current.text)
}
const tableElement = ref<HTMLTableElement>()
const measuredEdges = ref<{ row: number; y: number }[]>([])
let observer: ResizeObserver | undefined
onMounted(() => {
  if (!tableElement.value || typeof ResizeObserver === 'undefined') return
  observer = new ResizeObserver(() => {
    const table = tableElement.value!
    // offset coordinates remain in design pixels, independent of canvas zoom.
    const edges = Array.from(table.rows).map((row, i) => ({
      row: i - (table.tHead ? 1 : 0),
      y: row.offsetTop + row.offsetHeight,
    }))
    if (JSON.stringify(edges) !== JSON.stringify(measuredEdges.value)) measuredEdges.value = edges
    emit('measured-height', table.offsetHeight / 3)
  })
  observer.observe(tableElement.value)
})
const rows = computed(() => tableRows(props.table))
const bodyCells = computed(() => tableCells(props.table))
const headerCells = computed(() => tableCells(props.table, true))
const columnEdges = computed<number[]>(() => {
  let x = 0
  return props.table.columns.map((c: Json) => (x += Number(c.widthMm) * 3))
})
const rowEdges = computed(() => {
  if (
    measuredEdges.value.length ===
    rows.value.length + (props.table.headerMode === 'Hidden' ? 0 : 1)
  )
    return measuredEdges.value
  let y = props.table.headerMode === 'Hidden' ? 0 : (props.headerHeight ?? 7) * 3
  return [
    { row: -1, y },
    ...rows.value.map((r, index) => ({
      row: index,
      y: (y += (props.rowHeights?.[index] ?? Number(r.heightMm ?? 7)) * 3),
    })),
  ].filter((r) => r.row !== -1 || props.table.headerMode !== 'Hidden')
})
const canMerge = computed(
  () =>
    props.range && (props.range.bottom > props.range.top || props.range.right > props.range.left),
)
function resizeRow(event: PointerEvent, row: number) {
  const index = rowEdges.value.findIndex((edge) => edge.row === row)
  const height = (rowEdges.value[index]!.y - (rowEdges.value[index - 1]?.y ?? 0)) / 3
  emit('resize-row', event, row, height)
}
const canSplit = computed(() =>
  [...bodyCells.value, ...headerCells.value].some(
    (c) =>
      c.row === props.row &&
      props.table.columns[c.column].id === props.columnId &&
      (c.rowSpan > 1 || c.colSpan > 1),
  ),
)
function selected(row: number, column: number) {
  const r = props.range
  return (
    props.active &&
    (r
      ? row >= r.top && row <= r.bottom && column >= r.left && column <= r.right
      : props.row === row && props.table.columns[column].id === props.columnId)
  )
}
function choose(event: MouseEvent | KeyboardEvent, row: number, column: number) {
  emit('select', row, props.table.columns[column].id, event.shiftKey)
}
function runMerge(split = false) {
  if (split) emit('split')
  else emit('merge')
  close()
}
const menu = ref<HTMLElement>()
const position = ref<{ left: number; top: number }>()
const actions: TableAction[] = [
  'rowBefore',
  'rowAfter',
  'deleteRow',
  'columnBefore',
  'columnAfter',
  'deleteColumn',
]
function disabled(action: TableAction) {
  if (action.startsWith('row') || action === 'deleteRow')
    return props.row < 0 || (action === 'deleteRow' && rows.value.length <= 1)
  if (action === 'deleteColumn') return props.table.columns.length <= 1
  return Number(props.table.columns.find((c: Json) => c.id === props.columnId)?.widthMm) < 2
}
function close() {
  position.value = undefined
}
function execute(action: TableAction) {
  emit('action', action)
  close()
}
function outside(event: PointerEvent) {
  if (!menu.value?.contains(event.target as Node)) close()
}
function escape(event: KeyboardEvent) {
  if (event.key === 'Escape') close()
}
function cleanup() {
  document.removeEventListener('pointerdown', outside, true)
  document.removeEventListener('keydown', escape, true)
  window.removeEventListener('wheel', close, true)
  window.removeEventListener('resize', close)
}
async function open(event: MouseEvent, row: number, columnId: string) {
  if (
    !selected(
      row,
      props.table.columns.findIndex((c: Json) => c.id === columnId),
    )
  )
    emit('select', row, columnId)
  position.value = { left: event.clientX, top: event.clientY }
  await nextTick()
  const rect = menu.value?.getBoundingClientRect()
  if (rect)
    position.value = {
      left: Math.max(0, Math.min(event.clientX, window.innerWidth - rect.width)),
      top: Math.max(0, Math.min(event.clientY, window.innerHeight - rect.height)),
    }
  menu.value
    ?.querySelector<HTMLButtonElement>('button:not(:disabled)')
    ?.focus({ preventScroll: true })
  document.addEventListener('pointerdown', outside, true)
  document.addEventListener('keydown', escape, true)
  window.addEventListener('wheel', close, true)
  window.addEventListener('resize', close)
}
watch(position, (p) => {
  if (!p) cleanup()
})
watch(
  () => props.active,
  (active) => {
    if (!active) close()
  },
)
onBeforeUnmount(() => {
  cleanup()
  observer?.disconnect()
})
</script>
<template>
  <button
    type="button"
    class="report-table-handle"
    :aria-label="t('selectTable')"
    :title="t('tableSelectionHint')"
    @pointerdown.stop="emit('move-table', $event)"
    @click.stop="emit('select-table')"
  >
    ✥
  </button>
  <table ref="tableElement" class="report-design-table">
    <colgroup>
      <col
        v-for="col in table.columns"
        :key="col.id"
        :style="{ width: Number(col.widthMm) * 3 + 'px' }"
      />
    </colgroup>
    <thead v-if="table.headerMode !== 'Hidden'">
      <tr :style="{ height: (headerHeight ?? 7) * 3 + 'px' }">
        <th
          v-for="c in headerCells"
          :key="table.columns[c.column].id"
          tabindex="0"
          :colspan="c.colSpan"
          :style="cellStyle?.(-1, table.columns[c.column])"
          :class="{ 'cell-selected': selected(-1, c.column) }"
          @pointerdown.stop
          @click.stop="choose($event, -1, c.column)"
          @dblclick.stop="beginEdit(-1, c.column)"
          @keydown.enter.stop="choose($event, -1, c.column)"
          @keydown.space.stop.prevent="choose($event, -1, c.column)"
          @keydown.esc.stop="emit('select-table')"
          @contextmenu.stop.prevent="open($event, -1, table.columns[c.column].id)"
        >
          {{ table.columns[c.column].label || t('newColumn') }}
          <input
            v-if="editing?.row === -1 && editing.column === c.column"
            v-model="editing.text"
            class="report-cell-editor"
            :aria-label="t('editCellText')"
            @pointerdown.stop
            @click.stop
            @dblclick.stop
            @keydown.enter.stop.prevent="finishEdit()"
            @keydown.esc.stop.prevent="finishEdit(true)"
            @keydown.stop
            @blur="finishEdit()"
          />
        </th>
      </tr>
    </thead>
    <tbody>
      <tr
        v-for="(r, index) in rows"
        :key="r.id"
        :style="{ height: (rowHeights?.[index] ?? Number(r.heightMm ?? 7)) * 3 + 'px' }"
      >
        <td
          v-for="c in bodyCells.filter((c) => c.row === index)"
          :key="table.columns[c.column].id"
          tabindex="0"
          :colspan="c.colSpan"
          :rowspan="c.rowSpan"
          :style="
            cellStyle
              ? cellStyle(index, table.columns[c.column])
              : columnStyle(table.columns[c.column])
          "
          :class="{ 'cell-selected': selected(index, c.column) }"
          @pointerdown.stop
          @click.stop="choose($event, index, c.column)"
          @dblclick.stop="beginEdit(index, c.column, c.cell)"
          @keydown.enter.stop="choose($event, index, c.column)"
          @keydown.space.stop.prevent="choose($event, index, c.column)"
          @keydown.esc.stop="emit('select-table')"
          @contextmenu.stop.prevent="open($event, index, table.columns[c.column].id)"
        >
          <span :class="{ 'binding-caption': c.cell.value?.from !== 'Constant' }">{{
            caption(c.cell)
          }}</span>
          <input
            v-if="editing?.row === index && editing.column === c.column"
            v-model="editing.text"
            class="report-cell-editor"
            :aria-label="t('editCellText')"
            :title="t('editCellHint')"
            @pointerdown.stop
            @click.stop
            @dblclick.stop
            @keydown.enter.stop.prevent="finishEdit()"
            @keydown.esc.stop.prevent="finishEdit(true)"
            @keydown.stop
            @blur="finishEdit()"
          />
        </td>
      </tr>
    </tbody>
  </table>
  <button
    v-for="(x, index) in columnEdges"
    :key="'column-' + index"
    type="button"
    class="report-column-resize"
    :data-column-index="index"
    :aria-label="`${t('dragColumnWidth')} ${index + 1}`"
    :title="`${t('dragColumnWidth')} ${measure ? measure(Number(table.columns[index].widthMm)) : Number(table.columns[index].widthMm).toFixed(2) + ' mm'}`"
    :style="{ left: x - 3 + 'px' }"
    @pointerdown.stop.prevent="emit('resize-column', $event, index)"
    @click.stop
  />
  <button
    v-for="r in rowEdges"
    :key="'row-' + r.row"
    type="button"
    class="report-row-resize"
    :data-row-index="r.row"
    :aria-label="`${t('dragRowHeight')} ${r.row < 0 ? t('header') : r.row + 1}`"
    :title="t('dragRowHeight')"
    :style="{ top: r.y - 3 + 'px' }"
    @pointerdown.stop.prevent="resizeRow($event, r.row)"
    @click.stop
  />
  <Teleport to="body"
    ><div
      v-if="position"
      ref="menu"
      role="menu"
      class="report-cell-menu"
      :style="{ left: position.left + 'px', top: position.top + 'px' }"
      @pointerdown.stop
      @click.stop
    >
      <button
        v-for="action in actions"
        :key="action"
        role="menuitem"
        type="button"
        :disabled="disabled(action)"
        @click="execute(action)"
      >
        {{ t(action) }}
      </button>
      <button role="menuitem" type="button" :disabled="!canMerge" @click="runMerge()">
        {{ t('mergeCells') }}
      </button>
      <button role="menuitem" type="button" :disabled="!canSplit" @click="runMerge(true)">
        {{ t('splitCell') }}
      </button>
    </div></Teleport
  >
</template>
<style scoped>
.report-column-resize,
.report-row-resize {
  position: absolute;
  z-index: 6;
  border: 0;
  padding: 0;
  background: transparent;
}
.report-column-resize {
  top: -9px;
  width: 6px;
  height: calc(100% + 9px);
  cursor: col-resize;
}
.report-row-resize {
  left: -10px;
  width: calc(100% + 10px);
  height: 6px;
  cursor: row-resize;
}
.report-column-resize:hover,
.report-row-resize:hover {
  background: var(--el-color-primary-light-7);
}
.report-table-handle {
  position: absolute;
  top: 0;
  left: -22px;
  width: 20px;
  height: 20px;
  padding: 0;
  border: 1px solid var(--el-border-color);
  border-radius: 2px;
  color: var(--el-color-primary);
  background: var(--el-bg-color);
  cursor: move;
}
.report-design-table {
  width: 100%;
  height: auto;
  font: inherit;
  border-collapse: collapse;
  table-layout: fixed;
}
td,
th {
  position: relative;
  cursor: cell;
  border: 1px solid currentColor;
  padding: 3px;
  overflow: hidden;
  overflow-wrap: anywhere;
  text-align: inherit;
}
th {
  font-weight: bold;
}
.binding-caption {
  display: block;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}
.cell-selected {
  outline: 2px solid var(--el-color-primary);
  outline-offset: -2px;
  box-shadow: inset 0 0 0 1px var(--el-color-primary);
}
.report-cell-editor {
  position: absolute;
  inset: 0;
  box-sizing: border-box;
  width: 100%;
  height: 100%;
  min-width: 0;
  padding: inherit;
  border: 2px solid var(--el-color-primary);
  border-radius: 0;
  color: inherit;
  background: var(--el-bg-color);
  font: inherit;
  text-align: inherit;
  z-index: 8;
  outline: none;
}
.report-cell-menu {
  position: fixed;
  z-index: 4000;
  min-width: 170px;
  padding: 5px;
  border: 1px solid var(--el-border-color);
  border-radius: 4px;
  background: var(--el-bg-color-overlay);
  box-shadow: var(--el-box-shadow-light);
}
.report-cell-menu button {
  display: block;
  width: 100%;
  padding: 8px 12px;
  border: 0;
  background: transparent;
  color: var(--el-text-color-primary);
  text-align: left;
  cursor: pointer;
}
.report-cell-menu button:hover,
.report-cell-menu button:focus-visible {
  background: var(--el-color-primary-light-9);
}
.report-cell-menu button:disabled {
  color: var(--el-text-color-disabled);
  cursor: not-allowed;
}
</style>
