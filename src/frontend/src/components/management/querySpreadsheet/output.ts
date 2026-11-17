import ExcelJS from 'exceljs'
import JSZip from 'jszip'
import { CellValueType, numfmt, type ICellData, type IStyleData } from '@univerjs/core'
import type { SheetCapture } from './types'
import { readRows } from './model'
import { SheetError } from './copy'

function styleOf(data: SheetCapture, cell?: ICellData): IStyleData {
  return typeof cell?.s === 'string' ? (data.snapshot.styles[cell.s] ?? {}) : (cell?.s ?? {})
}
const color = (value: unknown) =>
  typeof value === 'string' && /^#[\da-f]{6}$/i.test(value)
    ? 'FF' + value.slice(1).toUpperCase()
    : undefined
const borderStyles: (ExcelJS.BorderStyle | undefined)[] = [
  undefined,
  'thin',
  'hair',
  'dotted',
  'dashed',
  'dashDot',
  'dashDotDot',
  'double',
  'medium',
  'mediumDashed',
  'mediumDashDot',
  'mediumDashDotDot',
  'slantDashDot',
  'thick',
]
export async function exportXlsx(data: SheetCapture, valuesOnly: boolean) {
  readRows(data, false)
  const book = new ExcelJS.Workbook(),
    sheet = book.addWorksheet('Result')
  const source = data.snapshot.sheets.result!
  for (let c = 0; c < data.columns.length; c++) {
    sheet.getColumn(c + 1).width = Math.max(4, ((source.columnData?.[c]?.w ?? 140) - 5) / 7)
    sheet.getColumn(c + 1).hidden = !!source.columnData?.[c]?.hd
  }
  for (let r = 0; r <= data.rowCount; r++) {
    sheet.getRow(r + 1).height = (source.rowData?.[r]?.h ?? 28) * 0.75
    sheet.getRow(r + 1).hidden = data.hidden.includes(r) || !!source.rowData?.[r]?.hd
    for (let c = 0; c < data.columns.length; c++) {
      const cell = source.cellData?.[r]?.[c],
        target = sheet.getCell(r + 1, c + 1),
        style = styleOf(data, cell)
      const value =
        cell?.p?.body?.dataStream.replace(/\r\n$/, '') ??
        (cell?.t === CellValueType.BOOLEAN ? !!cell.v : (cell?.v ?? null))
      target.value =
        cell?.f && !valuesOnly ? { formula: cell.f.replace(/^=/, ''), result: value ?? '' } : value
      target.numFmt = style.n?.pattern ?? 'General'
      target.font = {
        name: style.ff ?? 'Arial',
        size: style.fs ?? 11,
        bold: !!style.bl,
        italic: !!style.it,
        underline: style.ul?.s ? (style.ul.t === 10 ? 'double' : 'single') : false,
        strike: !!style.st?.s,
        ...(color(style.cl?.rgb) ? { color: { argb: color(style.cl?.rgb)! } } : {}),
      }
      if (color(style.bg?.rgb))
        target.fill = {
          type: 'pattern',
          pattern: 'solid',
          fgColor: { argb: color(style.bg?.rgb)! },
        }
      target.alignment = {
        horizontal:
          style.ht === 2
            ? 'center'
            : style.ht === 3
              ? 'right'
              : style.ht === 1
                ? 'left'
                : typeof value === 'number'
                  ? 'right'
                  : 'left',
        vertical: style.vt === 1 ? 'top' : style.vt === 2 ? 'middle' : 'bottom',
        wrapText: style.tb === 3,
        textRotation: style.tr?.v ? 'vertical' : (style.tr?.a ?? 0),
      }
      for (const [key, side] of [
        ['t', 'top'],
        ['b', 'bottom'],
        ['l', 'left'],
        ['r', 'right'],
      ] as const) {
        const border = style.bd?.[key]
        if (border && borderStyles[border.s])
          target.border = {
            ...target.border,
            [side]: {
              style: borderStyles[border.s],
              color: { argb: color(border.cl?.rgb) ?? 'FF000000' },
            },
          }
      }
    }
  }
  if (data.columns.length)
    sheet.autoFilter = {
      from: { row: 1, column: 1 },
      to: { row: Math.max(2, data.rowCount + 1), column: data.columns.length },
    }
  for (const merge of source.mergeData ?? [])
    sheet.mergeCells(
      merge.startRow + 1,
      merge.startColumn + 1,
      merge.endRow + 1,
      merge.endColumn + 1,
    )
  const freeze = source.freeze
  sheet.views =
    freeze && (freeze.xSplit > 0 || freeze.ySplit > 0)
      ? [
          {
            state: 'frozen',
            xSplit: Math.max(0, freeze.xSplit),
            ySplit: Math.max(0, freeze.ySplit),
          },
        ]
      : [{ state: 'normal' }]
  sheet.pageSetup = {
    paperSize: 9,
    orientation: 'landscape',
    fitToPage: true,
    fitToWidth: 1,
    fitToHeight: 0,
    printTitlesRow: '1:1',
  }
  let bytes = new Uint8Array(await book.xlsx.writeBuffer())
  if (!data.filters.length) return bytes
  const zip = await JSZip.loadAsync(bytes),
    file = zip.file('xl/worksheets/sheet1.xml')!
  const xml = new DOMParser().parseFromString(await file.async('string'), 'application/xml')
  const ns = 'http://schemas.openxmlformats.org/spreadsheetml/2006/main'
  const auto = xml.getElementsByTagNameNS(ns, 'autoFilter')[0]
  if (!auto || xml.querySelector('parsererror')) throw new SheetError('unsupportedFilter')
  const element = (name: string, attributes: Record<string, string>) => {
    const node = xml.createElementNS(ns, name)
    for (const [key, value] of Object.entries(attributes)) node.setAttribute(key, value)
    return node
  }
  for (const filter of data.filters) {
    if (
      filter.colorFilters ||
      !Number.isInteger(filter.colId) ||
      filter.colId < 0 ||
      filter.colId >= data.columns.length ||
      Number(!!filter.filters) + Number(!!filter.customFilters) !== 1
    )
      throw new SheetError('unsupportedFilter')
    const column = element('filterColumn', { colId: String(filter.colId) })
    if (filter.filters) {
      const node = element('filters', filter.filters.blank ? { blank: '1' } : {})
      for (const value of filter.filters.filters ?? [])
        node.append(element('filter', { val: String(value) }))
      column.append(node)
    } else if (filter.customFilters) {
      const node = element('customFilters', filter.customFilters.and ? { and: '1' } : {})
      if (
        !filter.customFilters.customFilters.length ||
        filter.customFilters.customFilters.length > 2
      )
        throw new SheetError('unsupportedFilter')
      for (const value of filter.customFilters.customFilters) {
        const operator = value.operator ?? 'equal'
        if (
          ![
            'equal',
            'notEqual',
            'greaterThan',
            'greaterThanOrEqual',
            'lessThan',
            'lessThanOrEqual',
          ].includes(operator)
        )
          throw new SheetError('unsupportedFilter')
        node.append(element('customFilter', { operator, val: String(value.val) }))
      }
      column.append(node)
    }
    auto.append(column)
  }
  zip.file('xl/worksheets/sheet1.xml', new XMLSerializer().serializeToString(xml))
  bytes = new Uint8Array(await zip.generateAsync({ type: 'uint8array', compression: 'DEFLATE' }))
  return bytes
}
export function renderPrint(doc: Document, data: SheetCapture, title: string) {
  readRows(data)
  const source = data.snapshot.sheets.result!
  const style = doc.createElement('style')
  style.textContent =
    '@page{size:A4 landscape;margin:12mm}body{font:12px Arial,"Microsoft YaHei",sans-serif;color:#20304a}h1{font-size:18px}table{border-collapse:collapse;width:100%;table-layout:fixed}thead{display:table-header-group}tr{break-inside:avoid}td,th{border:1px solid #aab3c0;padding:5px;overflow-wrap:anywhere}th{background:#e8eef7}'
  doc.head.replaceChildren(style)
  doc.body.replaceChildren()
  doc.title = title
  const visibleColumns = data.columns.map((_, c) => c).filter((c) => !source.columnData?.[c]?.hd)
  const groups: number[][] = []
  if (visibleColumns.length <= 9) groups.push(visibleColumns)
  else
    for (let start = 1; start < visibleColumns.length; start += 8)
      groups.push([visibleColumns[0]!, ...visibleColumns.slice(start, start + 8)])
  groups.forEach((columns, index) => {
    const section = doc.createElement('section')
    if (index) section.style.breakBefore = 'page'
    const label = doc.createElement('h2')
    label.textContent =
      title + (groups.length > 1 ? ' (' + (index + 1) + '/' + groups.length + ')' : '')
    section.append(label)
    const table = doc.createElement('table'),
      head = doc.createElement('thead'),
      body = doc.createElement('tbody')
    table.append(head, body)
    for (let r = 0; r <= data.rowCount; r++) {
      if (r && (data.hidden.includes(r) || source.rowData?.[r]?.hd)) continue
      const tr = doc.createElement('tr')
      for (const c of columns) {
        const cell = source.cellData?.[r]?.[c],
          format = styleOf(data, cell),
          td = doc.createElement(r ? 'td' : 'th')
        td.textContent =
          cell?.p?.body?.dataStream.replace(/\r\n$/, '') ??
          (cell?.t === CellValueType.BOOLEAN
            ? String(!!cell.v)
            : typeof cell?.v === 'number' && format.n?.pattern
              ? numfmt.format(format.n.pattern, cell.v)
              : String(cell?.v ?? ''))
        if (color(format.bg?.rgb)) td.style.backgroundColor = format.bg!.rgb!
        if (color(format.cl?.rgb)) td.style.color = format.cl!.rgb!
        td.style.fontWeight = format.bl ? 'bold' : 'normal'
        td.style.fontFamily = format.ff ?? 'Arial'
        td.style.fontSize = `${format.fs ?? 11}pt`
        td.style.fontStyle = format.it ? 'italic' : 'normal'
        td.style.textDecoration = [
          format.ul?.s ? 'underline' : '',
          format.st?.s ? 'line-through' : '',
        ].join(' ')
        td.style.textAlign = format.ht === 2 ? 'center' : format.ht === 3 ? 'right' : 'left'
        tr.append(td)
      }
      ;(r ? body : head).append(tr)
    }
    section.append(table)
    doc.body.append(section)
  })
}
