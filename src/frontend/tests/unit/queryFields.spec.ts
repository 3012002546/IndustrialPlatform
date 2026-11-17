import { expect, it } from 'vitest'
import { initialReportDocument } from '@/components/studio/report/model'
import { querySourceFields, syncQueryFields } from '@/components/studio/report/queryFields'

it('derives typed and nested fields from schema without executing or sampling data', () => {
  expect(
    querySourceFields({
      type: 'Array',
      item: {
        type: 'Object',
        children: [
          { name: 'code', label: '编号', type: 'String' },
          { name: 'amount', type: 'Decimal' },
          { name: 'day', type: 'Date' },
          { name: 'detail', type: 'Object', children: [{ name: 'valid', type: 'Boolean' }] },
          { name: 'items', type: 'Array', item: { type: 'String' } },
        ],
      },
    }),
  ).toEqual([
    { path: ['code'], label: '编号', dataType: 'String' },
    { path: ['amount'], label: 'amount', dataType: 'Decimal' },
    { path: ['day'], label: 'day', dataType: 'Date' },
    { path: ['detail', 'valid'], label: 'detail.valid', dataType: 'Boolean' },
  ])
})

it('syncs 60 fields while retaining edited names, hidden state, order and IDs', () => {
  const fields = Array.from({ length: 60 }, (_, index) => ({
    path: ['f' + index],
    label: 'Field ' + index,
  }))
  const initial = initialReportDocument('Report', 'Query')
  const doc = syncQueryFields(initial, fields)
  expect(initial.query.columns).toHaveLength(1)
  expect(doc.query.columns).toHaveLength(60)
  doc.query.columns[1].label = '自定义名称'
  doc.query.columns[1].visible = false
  const moved = doc.query.columns.splice(1, 1)[0]
  doc.query.columns.unshift(moved)
  const updated = syncQueryFields(doc, [...fields, { path: ['new'], label: 'New' }])
  expect(updated.query.columns[0]).toEqual(moved)
  expect(updated.query.columns).toHaveLength(61)
  expect(updated.bands[0].controls[0].table.columns[0].value.path).toEqual(['f1'])
  expect(syncQueryFields(updated, [])).toBe(updated)
})
