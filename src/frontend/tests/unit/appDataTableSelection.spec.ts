import { describe, expect, it } from 'vitest'

import { findAppDataTableRowByKey } from '@/components/management/appDataTable/selection'
import { filterAppDataTableTreeRows } from '@/components/management/AppDataTable'

describe('findAppDataTableRowByKey', () => {
  it('finds a selected radio row in nested tree children', () => {
    const child = { id: 'child-b', name: 'B' }
    const rows = [
      { id: 'root-a', name: 'A', organizations: [child] },
      { id: 'root-c', name: 'C', organizations: [] },
    ]

    expect(findAppDataTableRowByKey(rows, 'id', 'child-b', 'organizations')).toBe(child)
  })

  it('returns undefined when no row matches', () => {
    expect(findAppDataTableRowByKey([{ id: 'root-a' }], 'id', 'missing')).toBeUndefined()
  })
})

describe('filterAppDataTableTreeRows', () => {
  it('keeps matching descendants together with their ancestors', () => {
    const rows = [
      {
        id: 'root-a',
        label: 'Root A',
        children: [{ id: 'child-a', label: 'Target', children: [] }],
      },
      { id: 'root-b', label: 'Root B', children: [] },
    ]

    expect(filterAppDataTableTreeRows(rows, 'children', (row) => row.label === 'Target')).toEqual([
      rows[0],
    ])
    expect(
      filterAppDataTableTreeRows(rows, 'children', (row) => row.label === 'Target')[0]?.children,
    ).toEqual(rows[0]?.children)
  })

  it('filters deep descendants without retaining unmatched sibling branches', () => {
    const rows = [
      {
        id: 'root',
        label: 'Root',
        children: [
          {
            id: 'directory',
            label: 'Directory',
            children: [
              { id: 'target', label: 'Target', children: [] },
              { id: 'unrelated', label: 'Unrelated', children: [] },
            ],
          },
        ],
      },
    ]

    const filtered = filterAppDataTableTreeRows(rows, 'children', (row) => row.label === 'Target')

    expect(filtered[0]?.children).toHaveLength(1)
    expect(filtered[0]?.children?.[0]?.children).toEqual([
      { id: 'target', label: 'Target', children: [] },
    ])
  })
})
