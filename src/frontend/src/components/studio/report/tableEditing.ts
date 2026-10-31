import type { Json } from '@/api/studio'
import { clone } from '../shared/model'

export type TableAction =
  'rowBefore' | 'rowAfter' | 'deleteRow' | 'columnBefore' | 'columnAfter' | 'deleteColumn'
export const tableRows = (table: Json): Json[] =>
  table.rows ?? [
    { id: 'default', cells: table.columns.map((c: Json) => ({ columnId: c.id, value: c.value })) },
  ]
export function ensureTableRows(table: Json): Json[] {
  return (table.rows ??= clone(tableRows(table)).map((r) => ({ ...r, id: crypto.randomUUID() })))
}
export interface CellRange {
  top: number
  bottom: number
  left: number
  right: number
}
export interface TableCellPosition {
  row: number
  column: number
  rowSpan: number
  colSpan: number
  cell: Json
}
export function tableCells(table: Json, header = false): TableCellPosition[] {
  const rows: Json[] = header
    ? [{ cells: table.columns.map((c: Json) => ({ columnId: c.id, ...c.header })) }]
    : tableRows(table)
  const covered = new Set<string>(),
    result: TableCellPosition[] = []
  rows.forEach((r, row) =>
    table.columns.forEach((col: Json, column: number) => {
      if (covered.has(`${row}/${column}`)) return
      const cell = r.cells.find((c: Json) => c.columnId === col.id) ?? {}
      const rowSpan = Math.max(1, Number(cell.rowSpan ?? 1)),
        colSpan = Math.max(1, Number(cell.colSpan ?? 1))
      result.push({ row: header ? -1 : row, column, rowSpan, colSpan, cell })
      for (let y = row; y < row + rowSpan; y++)
        for (let x = column; x < column + colSpan; x++) covered.add(`${y}/${x}`)
    }),
  )
  return result
}
export function mergeRange(table: Json, range: CellRange): boolean {
  if (
    range.top < -1 ||
    range.top > range.bottom ||
    range.left < 0 ||
    range.right >= table.columns.length ||
    (range.top === -1 && range.bottom !== -1) ||
    range.bottom >= tableRows(table).length
  )
    return false
  const cells = tableCells(table, range.top === -1)
  const intersects = cells.filter(
    (c) =>
      c.row <= range.bottom &&
      c.row + c.rowSpan - 1 >= range.top &&
      c.column <= range.right &&
      c.column + c.colSpan - 1 >= range.left,
  )
  if (
    intersects.some(
      (c) =>
        c.row < range.top ||
        c.column < range.left ||
        c.row + c.rowSpan - 1 > range.bottom ||
        c.column + c.colSpan - 1 > range.right,
    )
  )
    return false
  if (range.top !== -1) ensureTableRows(table)
  for (const c of intersects) {
    const target = editableCell(table, c.row, table.columns[c.column].id)
    delete target.colSpan
    delete target.rowSpan
  }
  const target = editableCell(table, range.top, table.columns[range.left].id)
  target.colSpan = range.right - range.left + 1
  target.rowSpan = range.bottom - range.top + 1
  return true
}
export function editableCell(table: Json, row: number, columnId: string): Json {
  if (row < 0) return (table.columns.find((c: Json) => c.id === columnId).header ??= {})
  ensureTableRows(table)
  return tableCell(table, row, columnId)!
}
export function splitCell(table: Json, row: number, columnId: string) {
  const cell = editableCell(table, row, columnId)
  delete cell.rowSpan
  delete cell.colSpan
}
export const tableCell = (table: Json, row: number, columnId: string): Json | undefined =>
  table.rows
    ? table.rows[row]?.cells.find((c: Json) => c.columnId === columnId)
    : table.columns.find((c: Json) => c.id === columnId)
const blank = () => ({ from: 'Constant', value: null, nullPolicy: 'Null' })

/** Mutates the editor's transaction copy. Rows are templates, never source records. */
function editTableStructure(
  table: Json,
  action: TableAction,
  row: number,
  columnId: string,
  newId: () => string,
): { row: number; columnId: string } {
  const columns: Json[] = table.columns
  const index = columns.findIndex((c) => c.id === columnId)
  if (index < 0) return { row, columnId }
  if (action === 'rowBefore' || action === 'rowAfter') {
    table.rows ??= clone(tableRows(table)).map((r) => ({ ...r, id: newId() }))
    const next = Math.max(0, row) + (action === 'rowAfter' ? 1 : 0)
    table.rows.splice(next, 0, {
      id: newId(),
      cells: columns.map((c) => ({ columnId: c.id, value: blank() })),
    })
    return { row: next, columnId }
  }
  if (action === 'deleteRow') {
    if (table.rows?.length > 1 && row >= 0) table.rows.splice(row, 1)
    return { row: Math.max(0, Math.min(row, tableRows(table).length - 1)), columnId }
  }
  if (action === 'deleteColumn') {
    if (columns.length <= 1) return { row, columnId }
    const removed = columns.splice(index, 1)[0]!
    const neighbor = columns[Math.min(index, columns.length - 1)]!
    neighbor.widthMm = Number(neighbor.widthMm) + Number(removed.widthMm)
    for (const r of table.rows ?? []) r.cells = r.cells.filter((c: Json) => c.columnId !== columnId)
    return { row, columnId: neighbor.id }
  }
  const source = columns[index]!
  const width = Number(source.widthMm) / 2
  if (width < 1) return { row, columnId }
  source.widthMm = width
  const id = newId()
  columns.splice(index + (action === 'columnAfter' ? 1 : 0), 0, {
    id,
    label: '',
    widthMm: width,
    value: blank(),
  })
  for (const r of table.rows ?? []) r.cells.push({ columnId: id, value: blank() })
  return { row, columnId: id }
}

export function editTable(
  table: Json,
  action: TableAction,
  row: number,
  columnId: string,
  newId: () => string,
) {
  const merges = [...tableCells(table), ...tableCells(table, true)]
    .filter((c) => c.rowSpan > 1 || c.colSpan > 1)
    .map((c) => ({ ...c, cell: clone(c.cell), label: table.columns[c.column].label }))
  const oldRows = tableRows(table).length,
    oldColumns = table.columns.length
  const column = table.columns.findIndex((c: Json) => c.id === columnId)
  for (const c of merges) splitCell(table, c.row, table.columns[c.column].id)
  const result = editTableStructure(table, action, row, columnId, newId)
  const rowDelta = tableRows(table).length - oldRows,
    columnDelta = table.columns.length - oldColumns
  function interval(start: number, length: number, at: number, delta: number) {
    if (delta > 0)
      return at <= start
        ? [start + 1, length]
        : at < start + length
          ? [start, length + 1]
          : [start, length]
    if (delta < 0)
      return at < start
        ? [start - 1, length]
        : at < start + length
          ? [start, length - 1]
          : [start, length]
    return [start, length]
  }
  for (const c of merges) {
    const [y, height] =
      c.row < 0
        ? [-1, 1]
        : interval(c.row, c.rowSpan, Math.max(0, row) + (action === 'rowAfter' ? 1 : 0), rowDelta)
    const [x, width] = interval(
      c.column,
      c.colSpan,
      column + (action === 'columnAfter' ? 1 : 0),
      columnDelta,
    )
    if (!height || !width) continue
    const target = editableCell(table, y!, table.columns[x!].id)
    if ((rowDelta < 0 && c.row === row) || (columnDelta < 0 && c.column === column)) {
      const saved = clone(c.cell)
      delete saved.columnId
      Object.assign(target, saved)
      if (c.row < 0) table.columns[x!].label = c.label
    }
    target.rowSpan = height
    target.colSpan = width
  }
  return result
}
