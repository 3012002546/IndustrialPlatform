import type { Json } from '@/api/studio'

export type StructuredField = {
  key: string
  type?: 'number' | 'color' | 'scalar' | 'path' | 'children' | 'asset' | 'localized'
  options?: string[]
  min?: number
  max?: number
  when?: string
}
export const structuredKinds = [
  'columns',
  'series',
  'cases',
  'assetIds',
  'items',
  'points',
  'thresholds',
  'colorStops',
  'parameterMap',
  'rows',
]
export function structuredFields(kind: string, componentType: string): StructuredField[] {
  switch (kind) {
    case 'columns':
      return [
        { key: 'field' },
        { key: 'label', type: 'localized' },
        { key: 'widthPercent', type: 'number', min: 0.1, max: 100 },
        { key: 'align', options: ['left', 'center', 'right'] },
      ]
    case 'series':
      return [{ key: 'name' }, { key: 'valueField' }, { key: 'color', type: 'color' }]
    case 'cases':
      return [
        { key: 'value', type: 'scalar' },
        { key: 'label', type: 'localized' },
        componentType === 'StateImage'
          ? { key: 'assetId', type: 'asset' }
          : { key: 'color', type: 'color' },
      ]
    case 'assetIds':
      return [{ key: '', type: 'asset' }]
    case 'items':
      return [
        { key: 'id' },
        { key: 'label', type: 'localized' },
        { key: 'children', type: 'children' },
      ]
    case 'points':
      return [
        { key: '0', type: 'number' },
        { key: '1', type: 'number' },
      ]
    case 'thresholds':
      return [
        { key: 'min', type: 'number' },
        { key: 'max', type: 'number' },
        { key: 'color', type: 'color' },
        { key: 'label', type: 'localized' },
      ]
    case 'colorStops':
      return [
        { key: 'value', type: 'number' },
        { key: 'color', type: 'color' },
      ]
    case 'parameterMap':
      return [
        { key: 'target' },
        { key: 'source.from', options: ['Constant', 'PageParam', 'Instance', 'Context'] },
        { key: 'source.instanceId', when: 'Instance' },
        { key: 'source.path', type: 'path', when: 'path' },
        { key: 'source.value', type: 'scalar', when: 'Constant' },
      ]
    default:
      return []
  }
}
export function newStructuredItem(
  kind: string,
  index: number,
  componentType: string,
  columns: Json[] = [],
): unknown {
  switch (kind) {
    case 'columns':
      return { field: `field${index + 1}`, label: `field${index + 1}` }
    case 'series':
      return { name: `Series ${index + 1}`, valueField: 'value' }
    case 'cases':
      return {
        value: `state${index + 1}`,
        ...(componentType === 'StateImage'
          ? { assetId: 'dashboard.machine-running' }
          : { color: '#2dd4bf' }),
      }
    case 'assetIds':
      return 'dashboard.factory-overview'
    case 'items':
      return {
        id: `item-${crypto.randomUUID().slice(0, 8)}`,
        label: `Item ${index + 1}`,
        children: [],
      }
    case 'points':
      return [index * 40, 50]
    case 'thresholds':
      return { min: 0, max: 100, color: '#2dd4bf' }
    case 'colorStops':
      return { value: index * 10, color: '#2dd4bf' }
    case 'parameterMap':
      return {
        target: `parameter${index + 1}`,
        source: { from: 'Constant', value: '', nullPolicy: 'Error' },
      }
    case 'rows':
      return Object.fromEntries(
        columns
          .map((column) => [String(column.field ?? column.key ?? ''), ''])
          .filter(([key]) => key),
      )
    default:
      return {}
  }
}
export function structuredError(kind: string, value: unknown): string {
  if (!Array.isArray(value)) return 'structuredArrayInvalid'
  if (kind === 'assetIds')
    return value.every((item) => typeof item === 'string') ? '' : 'structuredArrayInvalid'
  if (kind === 'points')
    return value.every(
      (item) =>
        Array.isArray(item) &&
        item.length === 2 &&
        item.every((n) => typeof n === 'number' && Number.isFinite(n)),
    )
      ? ''
      : 'structuredArrayInvalid'
  if (!value.every((item) => item !== null && typeof item === 'object' && !Array.isArray(item)))
    return 'structuredArrayInvalid'
  if (kind === 'columns') {
    let total = 0
    for (const item of value) {
      if (item.widthPercent !== undefined) {
        if (
          typeof item.widthPercent !== 'number' ||
          !Number.isFinite(item.widthPercent) ||
          item.widthPercent <= 0 ||
          item.widthPercent > 100
        )
          return 'columnWidthInvalid'
        total += item.widthPercent
      }
    }
    if (total > 100.000001) return 'columnWidthInvalid'
  }
  if (
    kind === 'thresholds' &&
    value.some(
      (item) =>
        typeof item.min !== 'number' ||
        typeof item.max !== 'number' ||
        !Number.isFinite(item.min) ||
        !Number.isFinite(item.max) ||
        item.min > item.max,
    )
  )
    return 'structuredRangeInvalid'
  if (
    kind === 'colorStops' &&
    value.some((item) => typeof item.value !== 'number' || !Number.isFinite(item.value))
  )
    return 'structuredArrayInvalid'
  return ''
}
