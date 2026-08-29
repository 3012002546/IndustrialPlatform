import { describe, expect, it } from 'vitest'

import { toODataQuery, type QueryDescriptor } from '@/querying'

const schema = {
  selectable: ['id', 'name', 'active', 'createdOn'],
  filterable: ['name', 'active', 'createdOn'],
  sortable: ['createdOn'],
  fieldTypes: { name: 'string', active: 'boolean', createdOn: 'date' },
} as const

function descriptor(filters: QueryDescriptor['filters']): QueryDescriptor {
  return {
    filters,
    orderBy: [{ field: 'createdOn', direction: 'desc' }],
    select: ['id', 'name', 'active'],
    pageIndex: 2,
    pageSize: 25,
  }
}

describe('toODataQuery', () => {
  it('只输出受控 OData 选项并正确映射值和操作符', () => {
    const query = toODataQuery(
      descriptor([
        { field: 'name', operator: 'contains', value: "O'Reilly" },
        { field: 'active', operator: 'eq', value: true },
        { field: 'createdOn', operator: 'between', value: ['2026-01-01T00:00:00.000Z', '2026-02-01T00:00:00.000Z'] },
      ]),
      schema,
    )
    expect(query).toEqual({
      $filter: "contains(name,'O''Reilly') and active eq true and (createdOn ge 2026-01-01T00:00:00.000Z and createdOn le 2026-02-01T00:00:00.000Z)",
      $select: 'active,id,name',
      $orderby: 'createdOn desc',
      $top: '25',
      $skip: '25',
      $count: 'true',
    })
  })

  it('支持 in/startsWith 且拒绝非法字段、空值和禁用 option', () => {
    expect(
      toODataQuery(
        descriptor([
          { field: 'name', operator: 'startsWith', value: 'A' },
          { field: 'active', operator: 'in', value: [true, false] },
        ]),
        schema,
      ).$filter,
    ).toBe("startsWith(name,'A') and (active eq true or active eq false)")
    expect(() => toODataQuery(descriptor([{ field: 'unknown', operator: 'eq', value: 1 }]), schema)).toThrow()
    expect(() => toODataQuery(descriptor([{ field: 'name', operator: 'eq', value: null }]), schema)).toThrow()
    expect(() => toODataQuery({ ...descriptor([]), search: 'blocked' }, schema)).toThrow(/search/i)
  })
})
