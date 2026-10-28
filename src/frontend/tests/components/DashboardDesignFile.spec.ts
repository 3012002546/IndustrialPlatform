import { describe, expect, it } from 'vitest'
import {
  parseDashboardDesign,
  serializeDashboardDesign,
  MAX_DESIGN_FILE_BYTES,
} from '@/components/studio/dashboard/designFile'
import { initialDashboardDocument } from '@/pages/pc/studio/dashboard/model'
import photoresist from '@/pages/pc/studio/dashboard/samples/semiconductor-photoresist.dashboard.json'

describe('dashboard design files', () => {
  it('round-trips the complete editable definition, zero values, and fixed dependency references', () => {
    const document = initialDashboardDocument()
    document.theme = { background: '#081525' }
    document.samples = { label: 'test' }
    document.instances.push({
      instanceId: 'shared',
      datasetNId: 'dataset-a',
      revisionNId: 'revision-a',
      parameters: [],
    })
    document.components[0]!.bindings = [
      { property: 'text', source: { from: 'Constant', value: 0 } },
    ]
    document.runtimeResults = { secretRuntimeRows: ['not part of design'] }
    const text = serializeDashboardDesign('演示', document)
    const restored = parseDashboardDesign(text)
    const expected = { ...document }
    delete expected.runtimeResults
    expect(restored.document).toEqual(expected)
    expect(text).not.toContain('secretRuntimeRows')
    expect(restored.name).toBe('演示')
  })

  it('accepts a raw schema-v1 definition and UTF-8 BOM files', () => {
    const document = initialDashboardDocument()
    expect(parseDashboardDesign('\uFEFF' + JSON.stringify(document)).document).toEqual(document)
  })

  it.each([
    ['broken JSON', '{'],
    ['wrong root', '[]'],
    ['wrong document', '{"schemaVersion":1}'],
    [
      'wrong format',
      JSON.stringify({ format: 'Report', version: 1, document: initialDashboardDocument() }),
    ],
    [
      'future file',
      JSON.stringify({
        format: 'IndustrialPlatform.DashboardDesign',
        version: 2,
        document: initialDashboardDocument(),
      }),
    ],
    ['future canvas', JSON.stringify({ ...initialDashboardDocument(), schemaVersion: 2 })],
    ['bad component', JSON.stringify({ ...initialDashboardDocument(), components: [null] })],
  ])('rejects %s', (_name, text) => {
    expect(() => parseDashboardDesign(text)).toThrow()
  })

  it('limits UTF-8 bytes rather than string length', () => {
    expect(() => parseDashboardDesign('中'.repeat(Math.ceil(MAX_DESIGN_FILE_BYTES / 3)))).toThrow(
      'designFileSize',
    )
  })

  it('ships a self-contained industry design with readable batch/quality data and true zero', () => {
    const parsed = parseDashboardDesign(JSON.stringify(photoresist))
    expect(parsed.document.instances).toEqual([])
    expect(parsed.document.components.length).toBeGreaterThan(20)
    const zeroTank = parsed.document.components.find(
      (component) => component.id === 'standby-tank',
    )!
    expect(zeroTank.bindings.find((binding) => binding.property === 'level')?.source.value).toBe(0)
    expect(
      parsed.document.components.find((component) => component.id === 'batch-table')?.bindings[0]
        ?.source.value,
    ).toHaveLength(6)
    expect(
      parsed.document.components.find((component) => component.id === 'demo-note')?.props.text,
    ).toContain('合成样例')
    const ids = new Set(parsed.document.components.map((component) => component.id))
    expect(ids.size).toBe(parsed.document.components.length)
    for (const component of parsed.document.components) {
      for (const binding of component.bindings) expect(binding.source.nullPolicy).toBe('Error')
      expect(component.x + component.width).toBeLessThanOrEqual(parsed.document.canvas.width)
      expect(component.y + component.height).toBeLessThanOrEqual(parsed.document.canvas.height)
    }
  })
})
