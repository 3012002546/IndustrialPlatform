import { describe, expect, it } from 'vitest'

import { normalizeQueryDescriptor, QueryDescriptorError } from '@/querying'

const schema = {
  selectable: ['id', 'name', 'createdOn'],
  filterable: ['name', 'createdOn'],
  sortable: ['name', 'createdOn', 'id'],
  tieBreaker: 'id',
} as const

describe('normalizeQueryDescriptor', () => {
  it('固定分页边界、字段白名单和稳定排序 tie-break', () => {
    const result = normalizeQueryDescriptor(
      {
        filters: [{ field: 'name', operator: 'contains', value: 'pump' }],
        orderBy: [{ field: 'name', direction: 'asc' }],
        select: ['name', 'id'],
        pageIndex: 0,
        pageSize: 500,
      },
      schema,
    )
    expect(result.pageIndex).toBe(1)
    expect(result.pageSize).toBe(100)
    expect(result.select).toEqual(['id', 'name'])
    expect(result.orderBy).toEqual([
      { field: 'name', direction: 'asc' },
      { field: 'id', direction: 'asc' },
    ])
  })

  it('拒绝展示 label、非法操作符、重复过滤和超出排序上限', () => {
    expect(() =>
      normalizeQueryDescriptor(
        {
          filters: [{ field: '姓名', operator: 'eq', value: 'A' }],
          orderBy: [],
          select: [],
          pageIndex: 1,
          pageSize: 20,
        },
        schema,
      ),
    ).toThrow(QueryDescriptorError)
    expect(() =>
      normalizeQueryDescriptor(
        {
          filters: [
            { field: 'name', operator: 'eq', value: 'A' },
            { field: 'name', operator: 'eq', value: 'B' },
          ],
          orderBy: [],
          select: [],
          pageIndex: 1,
          pageSize: 20,
        },
        schema,
      ),
    ).toThrow('Duplicate filter')
    expect(() =>
      normalizeQueryDescriptor(
        {
          filters: [],
          orderBy: [
            { field: 'name', direction: 'asc' },
            { field: 'createdOn', direction: 'asc' },
            { field: 'id', direction: 'asc' },
            { field: 'name', direction: 'desc' },
          ],
          select: [],
          pageIndex: 1,
          pageSize: 20,
        },
        { ...schema, sortable: [...schema.sortable, 'id'] },
      ),
    ).toThrow(/at most 3/i)
  })
})
