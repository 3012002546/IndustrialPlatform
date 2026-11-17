import { describe, expect, it } from 'vitest'
import ExcelJS from 'exceljs'
import JSZip from 'jszip'
import type { IWorkbookData } from '@univerjs/core'
import {
  createSheet,
  visibleCapture,
  validatePage,
  readRows,
  chartRows,
} from '@/components/management/querySpreadsheet/model'
import { exportXlsx, renderPrint } from '@/components/management/querySpreadsheet/output'
import type { SheetCapture } from '@/components/management/querySpreadsheet/types'

function sample(): SheetCapture {
  const columns = [
    { field: 'id', title: '编号' },
    { field: 'amount', title: '数量', numberFormat: '0.00' },
    { field: 'flag', title: '合格' },
  ]
  return {
    columns,
    rowCount: 3,
    hidden: [],
    filters: [],
    snapshot: createSheet(
      columns,
      [
        { id: '001234567890123456789', amount: 10, flag: true },
        { id: '=1+2', amount: -2, flag: false },
        { id: '<img onerror="alert(1)">', amount: 0, flag: null },
      ],
      'Test',
    ) as IWorkbookData,
  }
}
describe('query spreadsheet data and outputs', () => {
  it('exports ISO dates and ribbon formatting without changing text identifiers', async () => {
    const data = sample()
    data.snapshot.sheets.result!.cellData![1]![0]!.s = {
      bl: 1,
      it: 1,
      ul: { s: 1 },
      st: { s: 1 },
      ht: 2,
      vt: 2,
      tb: 3,
      bd: { t: { s: 7, cl: { rgb: '#FF0000' } } },
    }
    const workbook = new ExcelJS.Workbook()
    await workbook.xlsx.load((await exportXlsx(data, false)).buffer as ArrayBuffer)
    const cell = workbook.worksheets[0]!.getCell('A2')
    expect(cell.font).toMatchObject({ bold: true, italic: true, underline: true, strike: true })
    expect(cell.alignment).toMatchObject({
      horizontal: 'center',
      vertical: 'middle',
      wrapText: true,
    })
    expect(cell.border.top).toMatchObject({ style: 'double', color: { argb: 'FFFF0000' } })
    const columns = [
      { field: 'date', title: 'Date' },
      { field: 'id', title: 'Id' },
    ]
    const dateData: SheetCapture = {
      columns,
      rowCount: 2,
      hidden: [],
      filters: [],
      snapshot: createSheet(
        columns,
        [
          { date: '2026-11-01', id: '001234567890123456789' },
          { date: '2026-11-02', id: '=1+2' },
        ],
        'Dates',
      ) as IWorkbookData,
    }
    expect(readRows(dateData)[0]!.date).toBe('2026-11-01')
    await workbook.xlsx.load((await exportXlsx(dateData, false)).buffer as ArrayBuffer)
    expect(workbook.worksheets[0]!.getCell('A2').value).toEqual(new Date('2026-11-01T00:00:00Z'))
    expect(workbook.worksheets[0]!.getCell('B3').value).toBe('=1+2')
    expect(
      createSheet(columns, [{ date: '2026-02-31', id: 'x' }], 'Invalid').sheets!.result!
        .cellData![1]![0]!.v,
    ).toBe('2026-02-31')
  })
  it('accepts arbitrary dynamic fields and rejects malformed and excessive pages', () => {
    for (const size of [8, 10, 30, 60, 120]) {
      const columns = Array.from({ length: size }, (_, i) => ({
        field: 'f' + i,
        title: 'Field ' + i,
      }))
      const row = Object.fromEntries(columns.map((c) => [c.field, 'value']))
      expect(() =>
        validatePage({ items: [row], total: 500, pageIndex: 2, pageSize: 20 }, columns, 2, 20),
      ).not.toThrow()
      expect(() =>
        validatePage({ items: [{}], total: 500, pageIndex: 2, pageSize: 20 }, columns, 2, 20),
      ).toThrow()
      expect(createSheet(columns, [row], 'Test').sheets!.result!.columnCount).toBe(size + 10)
    }
    const c = sample().columns
    expect(() =>
      validatePage({ items: [], total: 0, pageIndex: 2, pageSize: 20 }, c, 1, 20),
    ).toThrow()
    expect(() =>
      validatePage(
        {
          items: [{ id: 'x', amount: Infinity, flag: null }],
          total: 1,
          pageIndex: 1,
          pageSize: 20,
        },
        c,
        1,
        20,
      ),
    ).toThrow()
    expect(() =>
      validatePage(
        {
          items: [{ id: 'x'.repeat(20 * 1024 * 1024), amount: 1, flag: false }],
          total: 1,
          pageIndex: 1,
          pageSize: 20,
        },
        c,
        1,
        20,
      ),
    ).toThrow()
  })
  it('aggregates only visible edited values and refuses failed formulas or invalid numeric data', () => {
    const data = sample()
    data.hidden = [2]
    data.snapshot.sheets.result!.cellData![1]![1] = { f: '=2*3', v: 6, t: 2 }
    expect(readRows(data)[0]!.flag).toBe(true)
    expect(chartRows(readRows(data), 'flag', 'amount', 'sum').map((r) => r.value)).toEqual([6, 0])
    expect(
      chartRows(
        [
          { x: 'a', y: 2 },
          { x: 'a', y: 4 },
        ],
        'x',
        'y',
        'avg',
      ),
    ).toEqual([{ category: 'a', value: 3 }])
    expect(() => chartRows([{ x: 'a', y: '2' }], 'x', 'y', 'sum')).toThrow()
    expect(chartRows([{ x: 'a', y: null }], 'x', 'y', 'count')).toEqual([
      { category: 'a', value: 1 },
    ])
    data.snapshot.sheets.result!.cellData![1]![1] = { f: '=1/0', v: '#DIV/0!' }
    expect(() => readRows(data)).toThrow()
  })
  it('exports formulas, cached values, types and filter criteria without interpreting literal formulas', async () => {
    const data = sample()
    data.hidden = [2]
    data.filters = [
      { colId: 1, customFilters: { customFilters: [{ operator: 'greaterThanOrEqual', val: 0 }] } },
    ]
    data.snapshot.sheets.result!.cellData![1]![1] = { f: '=5+5', v: 10, t: 2, s: 'column-1' }
    const bytes = await exportXlsx(data, false)
    const book = new ExcelJS.Workbook()
    await book.xlsx.load(bytes.buffer as ArrayBuffer)
    const sheet = book.worksheets[0]!
    expect(sheet.getCell('A2').value).toBe('001234567890123456789')
    expect(sheet.getCell('A3').value).toBe('=1+2')
    expect(sheet.getCell('B2').value).toEqual({ formula: '5+5', result: 10 })
    expect(sheet.getCell('B2').numFmt).toBe('0.00')
    expect(sheet.getCell('C2').value).toBe(true)
    expect(sheet.getRow(3).hidden).toBe(true)
    const zip = await JSZip.loadAsync(bytes)
    expect(await zip.file('xl/worksheets/sheet1.xml')!.async('string')).toContain(
      'operator="greaterThanOrEqual" val="0"',
    )
    const values = new ExcelJS.Workbook()
    await values.xlsx.load((await exportXlsx(data, true)).buffer as ArrayBuffer)
    expect(values.worksheets[0]!.getCell('B2').value).toBe(10)
    data.filters = [{ colId: 0, colorFilters: {} }]
    await expect(exportXlsx(data, false)).rejects.toThrow()
  })
  it('freezes only visible formula results for export without changing the workbook', async () => {
    const data = sample()
    data.hidden = [2]
    data.columns.push({ field: 'formula', title: '计算列' })
    data.snapshot.sheets.result!.cellData![0]![3] = { v: '计算列' }
    data.snapshot.sheets.result!.cellData![1]![3] = { f: '=B2*2', v: 20, t: 2 }
    data.snapshot.sheets.result!.cellData![3]![3] = { f: '=B4*2', v: 0, t: 2 }
    const visible = visibleCapture(data),
      book = new ExcelJS.Workbook()
    await book.xlsx.load((await exportXlsx(visible, true)).buffer as ArrayBuffer)
    expect(book.worksheets[0]!.rowCount).toBe(3)
    expect(book.worksheets[0]!.getCell('D2').value).toBe(20)
    expect(book.worksheets[0]!.getCell('D3').value).toBe(0)
    expect(readRows(visible)).toEqual(readRows(data))
    expect(data.snapshot.sheets.result!.cellData![1]![3]!.f).toBe('=B2*2')
  })
  it('prints visible rows safely with a repeating header and formatted numbers', () => {
    const data = sample()
    data.hidden = [2]
    const doc = document.implementation.createHTMLDocument()
    renderPrint(doc, data, '<script>unsafe</script>')
    expect(doc.querySelectorAll('tbody tr')).toHaveLength(2)
    expect(doc.querySelector('script,img')).toBeNull()
    expect(doc.body.textContent).toContain('10.00')
    expect(doc.body.textContent).toContain('true')
    expect(doc.querySelector('thead')).not.toBeNull()
  })
})

it('prints wide columns in groups with repeated identifiers and preserves all filtered values', () => {
  const columns = Array.from({ length: 25 }, (_, i) => ({ field: 'c' + i, title: 'C' + i }))
  const rows = [0, 1000, 2000].map((n) =>
    Object.fromEntries(columns.map((c, i) => [c.field, n + i])),
  )
  const data: SheetCapture = {
    columns,
    rowCount: 3,
    hidden: [2],
    filters: [],
    snapshot: createSheet(columns, rows, 'Wide') as IWorkbookData,
  }
  const doc = document.implementation.createHTMLDocument()
  renderPrint(doc, data, 'Wide')
  const tables = [...doc.querySelectorAll('table')]
  expect(tables).toHaveLength(3)
  const values: string[] = []
  for (const table of tables) {
    expect(table.querySelectorAll('thead th')).toHaveLength(9)
    const body = [...table.querySelectorAll('tbody tr')]
    expect(body).toHaveLength(2)
    expect(body[0]!.firstElementChild!.textContent).toBe('0')
    expect(body[1]!.firstElementChild!.textContent).toBe('2000')
    values.push(...[...body[0]!.children].slice(1).map((c) => c.textContent!))
  }
  expect(values).toEqual(Array.from({ length: 24 }, (_, i) => String(i + 1)))
  expect(
    [...doc.querySelectorAll('section')].slice(1).every((s) => s.style.breakBefore === 'page'),
  ).toBe(true)
})
