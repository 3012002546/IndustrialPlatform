import { describe, expect, it } from 'vitest'
import {
  parseDashboardDesign,
  serializeDashboardDesign,
} from '@/components/studio/dashboard/designFile'
import { dashboardSamples, type DashboardDocument } from '@/pages/pc/studio/dashboard/model'
import overviewFile from '@/pages/pc/studio/dashboard/samples/semiconductor-cmp-slurry.dashboard.json'
import detailFile from '@/pages/pc/studio/dashboard/samples/semiconductor-cmp-slurry-batch-detail.dashboard.json'

const overview = parseDashboardDesign(JSON.stringify(overviewFile)).document
const detail = parseDashboardDesign(JSON.stringify(detailFile)).document
const component = (document: DashboardDocument, id: string) =>
  document.components.find((c) => c.id === id)!
function constantValue(document: DashboardDocument, id: string, property: string): unknown {
  const source = component(document, id).bindings.find((b) => b.property === property)!.source
  let value =
    source.from === 'Constant'
      ? source.value
      : document.instances.find((i) => i.instanceId === source.instanceId)!.value
  for (const part of source.path ?? []) value = value?.[part]
  return value
}

describe('CMP slurry industry sample pair', () => {
  it('round trips both designs without external sources or photoresist data', () => {
    for (const file of [overviewFile, detailFile]) {
      const parsed = parseDashboardDesign(
        serializeDashboardDesign(file.name, file.document as DashboardDocument),
      )
      expect(parsed.document).toEqual(file.document)
      expect(parsed.document.components.length).toBeGreaterThanOrEqual(18)
      expect(JSON.stringify(parsed.document)).not.toMatch(/PR-DEMO|光刻胶|DUV-/)
      expect(new Set(parsed.document.components.map((c) => c.id)).size).toBe(
        parsed.document.components.length,
      )
      for (const c of parsed.document.components) {
        expect(c.x).toBeGreaterThanOrEqual(0)
        expect(c.y).toBeGreaterThanOrEqual(0)
        expect(c.x + c.width).toBeLessThanOrEqual(parsed.document.canvas.width)
        expect(c.y + c.height).toBeLessThanOrEqual(parsed.document.canvas.height)
        for (const binding of c.bindings) {
          if (binding.source.from === 'Instance')
            expect(
              parsed.document.instances.some((i) => i.instanceId === binding.source.instanceId),
            ).toBe(true)
        }
      }
      for (const instance of parsed.document.instances) {
        expect(instance.sourceType).toBe('Constant')
        expect(instance.refreshSeconds).toBe(0)
      }
    }
  })

  it('keeps overview totals, release rate, pending results and actual zero consistent', () => {
    expect(overview.instances).toHaveLength(1)
    expect(overview.instances[0]!.instanceId).toBe('cmp-overview-data')
    const rows = constantValue(overview, 'batch-table', 'rows') as Record<string, unknown>[]
    expect(rows).toHaveLength(6)
    expect(new Set(rows.map((r) => r.batch)).size).toBe(6)
    expect(constantValue(overview, 'output', 'value')).toBe(
      rows.reduce((sum, row) => sum + Number(row.volume), 0),
    )
    const released = rows.filter((r) => r.quality === '已放行').length
    expect(constantValue(overview, 'fpy', 'value')).toBe(
      Number(((released / rows.length) * 100).toFixed(1)),
    )
    expect(constantValue(overview, 'pending', 'value')).toBe(rows.length - released)
    expect(constantValue(overview, 'standby-tank', 'level')).toBe(0)
    const measured = constantValue(overview, 'qc-chart', 'rows') as Record<string, unknown>[]
    expect(measured).toHaveLength(5)
    expect(measured.some((r) => r.batch === '204')).toBe(false)
    expect(measured.every((r) => typeof r.d50 === 'number' && r.d50 > 0)).toBe(true)
  })

  it('uses one batch parameter with a complete matching detail for all six batches', () => {
    const rows = constantValue(overview, 'batch-table', 'rows') as Record<string, unknown>[]
    const keys = rows.map((r) => r.batch)
    expect(detail.parameters).toHaveLength(1)
    expect(detail.parameters[0]).toMatchObject({
      name: 'batchNo',
      defaultValue: 'SL-DEMO-205',
      enumValues: keys,
    })
    for (const c of detail.components) {
      for (const b of c.bindings) {
        expect(b.source.from).toBe('PageParam')
        expect(b.source.path).toEqual(['batchNo'])
        const entries = b.source.transform?.args.entries
        if (entries) expect(entries.map((e: Record<string, unknown>) => e.from)).toEqual(keys)
      }
    }
    const checks = component(detail, 'quality-records').bindings[0]!.source.transform!.args.entries
    expect(
      checks.every((entry: Record<string, unknown>) => (entry.to as unknown[]).length === 7),
    ).toBe(true)
    const pending = checks.find((e: Record<string, unknown>) => e.from === 'SL-DEMO-204').to
    expect(pending.find((r: Record<string, unknown>) => r.item === 'D50 / nm').result).toBe('待检')
    expect(component(detail, 'quality-records').props.rowHeight).toBe(28)
  })

  it('exposes both samples as independent clones and leaves navigation deployment-specific', () => {
    const first = dashboardSamples()
    const sample = first.find((s) => s.key === 'cmp-slurry')!
    const second = dashboardSamples().find((s) => s.key === 'cmp-slurry')!
    expect(first.some((s) => s.key === 'cmp-slurry-detail')).toBe(true)
    expect(first.some((s) => s.key === 'photoresist')).toBe(true)
    sample.document.components[0]!.props.text = 'changed'
    expect(second.document.components[0]!.props.text).toContain('CMP SLURRY')
    expect(second.document.navigation).toEqual([])
  })
})
