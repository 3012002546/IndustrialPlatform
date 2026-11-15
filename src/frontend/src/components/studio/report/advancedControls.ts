import type { Json } from '@/api/studio'
import { clone } from '@/components/studio/shared/model'

export const advancedControlTypes = [
  'CharacterComb',
  'Shape',
  'Barcode',
  'Chart',
  'Gauge',
  'Sparkline',
  'PivotTable',
  'Line',
] as const
export function isAdvancedControl(type: string): boolean {
  return (advancedControlTypes as readonly string[]).includes(type)
}
export function advancedOptions(type: string): Json {
  switch (type) {
    case 'CharacterComb':
      return { cells: 12 }
    case 'Shape':
      return { kind: 'Rectangle', stroke: '#000000', fill: 'none', strokeWidthMm: 0.2 }
    case 'Line':
      return { direction: 'Horizontal', stroke: '#000000', strokeWidthMm: 0.2, dash: 'Solid' }
    case 'Barcode':
      return { symbology: 'Code39', showText: true }
    case 'Chart':
      return { kind: 'Bar', categoryPath: ['label'], series: [{ name: 'A', valuePath: ['value'] }] }
    case 'Gauge':
      return { min: 0, max: 100, label: '' }
    case 'Sparkline':
      return {}
    case 'PivotTable':
      return {
        rowPath: ['row'],
        columnPath: ['column'],
        valuePath: ['value'],
        aggregate: 'Sum',
        totals: true,
      }
    default:
      return {}
  }
}
// Initial constants are editable examples, never fetched business data.
export function advancedControlDefaults(type: string, text: string): Json {
  if (!isAdvancedControl(type)) return {}
  const values: Record<string, unknown> = {
    CharacterComb: text.length <= 12 ? text : 'ABC123',
    Barcode: 'CODE39',
    Chart: [
      { label: 'A', value: 1 },
      { label: 'B', value: 2 },
    ],
    Gauge: 0,
    Sparkline: [0, 1, 0],
    PivotTable: [{ row: 'A', column: 'B', value: 0 }],
  }
  return {
    advanced: advancedOptions(type),
    canGrow: type === 'PivotTable',
    keepTogether: type !== 'PivotTable',
    heightMm: ['Chart', 'PivotTable'].includes(type)
      ? 40
      : ['Shape', 'Gauge', 'Barcode'].includes(type)
        ? 20
        : 10,
    ...(Object.hasOwn(values, type)
      ? { value: { from: 'Constant', value: values[type], nullPolicy: 'Error' } }
      : {}),
  }
}
export function patchAdvancedControl(control: Json, patch: Json): Json {
  return {
    ...clone(control),
    advanced: {
      ...advancedOptions(String(control.type)),
      ...clone(control.advanced ?? {}),
      ...clone(patch),
    },
  }
}
export function advancedPath(text: string): string[] {
  return text
    .split('.')
    .map((part) => part.trim())
    .filter(Boolean)
}
