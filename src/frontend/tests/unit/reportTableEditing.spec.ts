import { describe, expect, it } from 'vitest'
import {
  editTable,
  tableCell,
  tableRows,
  tableCells,
  mergeRange,
  splitCell,
} from '@/components/studio/report/tableEditing'
import type { Json } from '@/api/studio'
const fixture = (): Json => ({
  columns: [
    { id: 'a', label: 'A', widthMm: 40, value: { from: 'Row', path: ['a'] } },
    { id: 'b', label: 'B', widthMm: 60, value: { from: 'Row', path: ['b'] } },
  ],
})
const id = () => crypto.randomUUID()
describe('report table editing', () => {
  it('merges a template rectangle without losing hidden bindings and splits it back', () => {
    const table = fixture()
    editTable(table, 'rowAfter', 0, 'a', id)
    tableCell(table, 1, 'b')!.value = { from: 'Constant', value: 'restored' }
    expect(mergeRange(table, { top: 0, bottom: 1, left: 0, right: 1 })).toBe(true)
    expect(tableCells(table)).toHaveLength(1)
    expect(tableCells(table)[0]).toMatchObject({ rowSpan: 2, colSpan: 2 })
    expect(mergeRange(table, { top: 0, bottom: 0, left: 1, right: 1 })).toBe(false)
    splitCell(table, 0, 'a')
    expect(tableCells(table)).toHaveLength(4)
    expect(tableCell(table, 1, 'b')!.value.value).toBe('restored')
    expect(tableCell(table, 0, 'a')!.value.path).toEqual(['a'])
    expect(mergeRange(table, { top: -1, bottom: 0, left: 0, right: 1 })).toBe(false)
  })
  it('adjusts spans around inserted and deleted rows and columns, preserving the anchor', () => {
    const table = fixture()
    editTable(table, 'rowAfter', 0, 'a', id)
    mergeRange(table, { top: 0, bottom: 1, left: 0, right: 1 })
    const inserted = editTable(table, 'columnAfter', 0, 'a', id)
    editTable(table, 'rowAfter', 0, 'a', id)
    expect(tableCells(table)[0]).toMatchObject({ rowSpan: 3, colSpan: 3 })
    editTable(table, 'deleteColumn', 0, 'a', id)
    expect(tableCell(table, 0, inserted.columnId)!.value.path).toEqual(['a'])
    editTable(table, 'deleteRow', 0, inserted.columnId, id)
    expect(tableCells(table)[0]).toMatchObject({ rowSpan: 2, colSpan: 2 })
    expect(tableCells(table)[0]!.cell.value.path).toEqual(['a'])
  })
  it('merges headers independently and restores both labels on split', () => {
    const table = fixture()
    mergeRange(table, { top: -1, bottom: -1, left: 0, right: 1 })
    expect(tableCells(table, true)).toHaveLength(1)
    expect(tableCells(table)).toHaveLength(2)
    splitCell(table, -1, 'a')
    expect(table.columns.map((c: Json) => c.label)).toEqual(['A', 'B'])
    expect(tableCells(table, true)).toHaveLength(2)
  })
  it('preserves legacy bindings, materializes independent rows and edits cells by column identity', () => {
    const table = fixture()
    expect(tableRows(table)).toHaveLength(1)
    tableCell(table, 0, 'a')!.value = { from: 'Constant', value: 'original' }
    editTable(table, 'rowAfter', 0, 'a', id)
    expect(tableRows(table)).toHaveLength(2)
    expect(tableCell(table, 0, 'a')!.value.value).toBe('original')
    tableCell(table, 1, 'a')!.value.value = 'new'
    table.columns.reverse()
    expect(tableCell(table, 1, 'a')!.value.value).toBe('new')
    expect(tableCell(table, 0, 'a')!.value.value).toBe('original')
    editTable(table, 'rowBefore', 0, 'a', id)
    expect(tableCell(table, 1, 'a')!.value.value).toBe('original')
    editTable(table, 'deleteRow', 0, 'a', id)
    expect(tableCell(table, 0, 'a')!.value.value).toBe('original')
  })
  it('inserts and removes columns in every row while preserving width and surviving bindings', () => {
    const table = fixture()
    editTable(table, 'rowAfter', 0, 'a', id)
    const added = editTable(table, 'columnBefore', 1, 'b', id)
    expect(table.columns.map((c: Json) => c.id)).toEqual(['a', added.columnId, 'b'])
    expect(table.columns.reduce((n: number, c: Json) => n + c.widthMm, 0)).toBe(100)
    expect(table.rows.every((r: Json) => r.cells.length === 3)).toBe(true)
    editTable(table, 'deleteColumn', 1, added.columnId, id)
    expect(table.columns.map((c: Json) => c.widthMm)).toEqual([40, 60])
    expect(tableCell(table, 0, 'b')!.value.path).toEqual(['b'])
  })
  it('keeps one row/column and rejects insertion into a column too narrow to split', () => {
    const table = fixture()
    editTable(table, 'deleteRow', 0, 'a', id)
    editTable(table, 'deleteColumn', 0, 'b', id)
    editTable(table, 'deleteColumn', 0, 'a', id)
    table.columns[0].widthMm = 1
    editTable(table, 'columnAfter', 0, 'a', id)
    expect(table.columns).toHaveLength(1)
    expect(tableRows(table)).toHaveLength(1)
  })
})
