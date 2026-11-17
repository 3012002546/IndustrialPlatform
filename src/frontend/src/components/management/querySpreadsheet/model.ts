import {
  CellValueType,
  LocaleType,
  numfmt,
  type ICellData,
  type IWorkbookData,
} from '@univerjs/core'
import type { QueryColumn, QueryPage, QueryRow, SheetCapture } from './types'
import { SheetError } from './copy'

export function validatePage(
  page: QueryPage,
  columns: QueryColumn[],
  expectedPage: number,
  expectedSize: number,
) {
  if (
    !Array.isArray(page.items) ||
    !Number.isSafeInteger(page.total) ||
    page.total < 0 ||
    page.pageIndex !== expectedPage ||
    page.pageSize !== expectedSize ||
    page.items.length > expectedSize ||
    page.items.length > page.total
  )
    throw new SheetError('invalid')
  if (
    page.items.length > 10000 ||
    page.items.length * columns.length > 500000 ||
    new TextEncoder().encode(JSON.stringify({ columns, items: page.items })).length >
      20 * 1024 * 1024
  )
    throw new SheetError('limit')
  if (new Set(columns.map((c) => c.field)).size !== columns.length || columns.some((c) => !c.field))
    throw new SheetError('invalid')
  for (const row of page.items)
    for (const column of columns) {
      const value = row?.[column.field]
      if (!row || !Object.hasOwn(row, column.field)) throw new SheetError('invalid')
      if (
        (value != null && !['string', 'number', 'boolean'].includes(typeof value)) ||
        (typeof value === 'number' && !Number.isFinite(value))
      )
        throw new SheetError('invalidValue')
    }
}
export function createSheet(
  columns: QueryColumn[],
  rows: QueryRow[],
  name: string,
): Partial<IWorkbookData> {
  const cellData: Record<number, Record<number, ICellData>> = {}
  // Only unambiguous, valid ISO calendar dates are converted; identifiers stay text.
  const dateValue = (value: unknown): number | undefined => {
    if (typeof value !== 'string' || !/^\d{4}-\d{2}-\d{2}$/.test(value)) return
    const time = Date.parse(value + 'T00:00:00Z')
    if (
      !Number.isFinite(time) ||
      new Date(time).toISOString().slice(0, 10) !== value ||
      value < '1900-03-01'
    )
      return
    return (time - Date.UTC(1899, 11, 30)) / 86400000
  }
  const dates = columns.map((column) => {
    const present = rows
      .map((row) => row[column.field])
      .filter((value) => value != null && value !== '')
    return present.length > 0 && present.every((value) => dateValue(value) !== undefined)
  })
  const formats = columns.map(
    (column, c) => column.numberFormat ?? (dates[c] ? 'yyyy-mm-dd' : undefined),
  )
  const values = [Object.fromEntries(columns.map((c) => [c.field, c.title])), ...rows]
  values.forEach((row, r) => {
    const cells: Record<number, ICellData> = {}
    cellData[r] = cells
    columns.forEach((column, c) => {
      const raw = row[column.field] as string | number | boolean | null | undefined
      const value = r && dates[c] ? (dateValue(raw) ?? raw) : raw
      cells[c] = {
        v: value ?? null,
        t:
          typeof value === 'number'
            ? CellValueType.NUMBER
            : typeof value === 'boolean'
              ? CellValueType.BOOLEAN
              : CellValueType.STRING,
        s: r === 0 ? 'head' : formats[c] ? 'column-' + c : 'body',
      }
    })
  })
  return {
    id: crypto.randomUUID(),
    name,
    appVersion: '1.3.1',
    locale: LocaleType.ZH_CN,
    sheetOrder: ['result'],
    styles: {
      head: { bg: { rgb: '#E8EEF7' }, cl: { rgb: '#20304A' }, bl: 1, fs: 11 },
      body: { fs: 11 },
      ...Object.fromEntries(
        formats.flatMap((pattern, c) =>
          pattern ? [['column-' + c, { fs: 11, n: { pattern } }]] : [],
        ),
      ),
    },
    sheets: {
      result: {
        id: 'result',
        name: 'Result',
        rowCount: Math.max(31, rows.length + 31),
        columnCount: columns.length + 10,
        defaultRowHeight: 28,
        defaultColumnWidth: 140,
        cellData,
        columnData: Object.fromEntries(columns.map((c, i) => [i, { w: c.width ?? 140 }])),
        freeze: { startRow: 1, startColumn: -1, xSplit: 0, ySplit: 1 },
      },
    },
  }
}
export function readRows(data: SheetCapture, visibleOnly = true): QueryRow[] {
  const sheet = data.snapshot.sheets.result
  if (!sheet || data.snapshot.sheetOrder.length !== 1) throw new SheetError('unsupported')
  const hidden = new Set(data.hidden)
  const rows: QueryRow[] = []
  for (let r = 1; r <= data.rowCount; r++) {
    if (visibleOnly && (hidden.has(r) || sheet.rowData?.[r]?.hd)) continue
    const row: QueryRow = {}
    data.columns.forEach((column, c) => {
      const cell = sheet.cellData?.[r]?.[c]
      if (
        cell?.f &&
        (cell.v == null ||
          /^#(?:REF!|DIV\/0!|VALUE!|NAME\?|N\/A|NUM!|NULL!|SPILL!|CALC!)/.test(String(cell.v)))
      )
        throw new SheetError('formulaError')
      row[column.field] =
        cell?.p?.body?.dataStream.replace(/\r\n$/, '') ??
        (cell?.t === CellValueType.BOOLEAN ? !!cell.v : (cell?.v ?? null))
      const style = typeof cell?.s === 'string' ? data.snapshot.styles[cell.s] : cell?.s
      if (
        typeof row[column.field] === 'number' &&
        style?.n?.pattern &&
        numfmt.isDateFormat(style.n.pattern)
      )
        row[column.field] = numfmt.format(style.n.pattern, row[column.field] as number)
    })
    rows.push(row)
  }
  return rows
}
export function chartRows(rows: QueryRow[], category: string, value: string, aggregate: string) {
  const groups = new Map<string, { sum: number; count: number }>()
  for (const row of rows) {
    if (aggregate !== 'count' && (row[value] == null || row[value] === '')) continue
    const label = String(row[category] ?? '')
    const current = groups.get(label) ?? { sum: 0, count: 0 }
    if (aggregate !== 'count') {
      if (typeof row[value] !== 'number' || !Number.isFinite(row[value]))
        throw new SheetError('chartInvalid')
      current.sum += row[value] as number
    }
    current.count++
    groups.set(label, current)
    if (groups.size > 100 || !Number.isFinite(current.sum)) throw new SheetError('chartInvalid')
  }
  return [...groups].map(([label, group]) => ({
    category: label,
    value:
      aggregate === 'count'
        ? group.count
        : aggregate === 'avg'
          ? group.sum / group.count
          : group.sum,
  }))
}

// Export the visible, calculated result. Compacting formula rows requires freezing values,
// otherwise relative references would change after filtered rows are removed.
export function visibleCapture(data: SheetCapture): SheetCapture {
  readRows(data)
  const copy = structuredClone(data),
    source = data.snapshot.sheets.result!,
    sheet = copy.snapshot.sheets.result!
  sheet.cellData = { 0: structuredClone(source.cellData?.[0] ?? {}) }
  sheet.rowData = {}
  sheet.mergeData = []
  let target = 0
  for (let r = 1; r <= data.rowCount; r++) {
    if (data.hidden.includes(r) || source.rowData?.[r]?.hd) continue
    const cells = structuredClone(source.cellData?.[r] ?? {})
    for (const cell of Object.values(cells)) {
      delete cell.f
      delete cell.si
    }
    sheet.cellData[++target] = cells
  }
  copy.rowCount = target
  copy.hidden = []
  copy.filters = []
  sheet.rowCount = Math.max(2, target + 1)
  return copy
}
