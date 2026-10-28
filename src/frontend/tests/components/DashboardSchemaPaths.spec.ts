import { describe, expect, it } from 'vitest'
import { dashboardSchemaPaths } from '@/components/studio/dashboard/schemaPaths'

describe('dashboard result field selection', () => {
  const result = {
    name: 'schemaRootNameIsNotAWrapper',
    type: 'Array',
    item: {
      name: 'row',
      type: 'Object',
      children: [
        { name: 'line_code', type: 'String' },
        { name: 'process', type: 'Object', children: [{ name: 'temperature', type: 'Decimal' }] },
        { name: 'readings', type: 'Array', item: { type: 'Decimal' } },
      ],
    },
  }
  it('uses real result array indices and nested JSON keys', () => {
    expect(dashboardSchemaPaths(result)).toEqual([
      '0',
      '0.line_code',
      '0.process',
      '0.process.temperature',
      '0.readings',
      '0.readings.0',
    ])
  })
  it('offers row-relative fields for row navigation', () => {
    expect(dashboardSchemaPaths(result, true)).toEqual([
      'line_code',
      'process',
      'process.temperature',
      'readings',
      'readings.0',
    ])
  })
  it('preserves named array fields inside an object result', () => {
    expect(
      dashboardSchemaPaths({ type: 'Object', children: [{ ...result, name: 'lines' }] }),
    ).toContain('lines.0.process.temperature')
  })
  it('uses the bound collection inside a shared API object for row navigation', () => {
    const shared = { type: 'Object', children: [{ ...result, name: 'batches' }] }
    expect(dashboardSchemaPaths(shared, true, ['batches'])).toContain('line_code')
    expect(dashboardSchemaPaths(shared, true, ['batches'])).not.toContain('batches.0.line_code')
    expect(dashboardSchemaPaths(shared, true, ['missing'])).toEqual([])
  })
})
