import { describe, expect, it } from 'vitest'
import {
  constantInstances,
  createDataSchedule,
  mergeRuntime,
  shareDashboardConstants,
} from '@/components/studio/dashboard/dataSources'
import type { DashboardRuntime } from '@/api/studio'
import sample from '@/pages/pc/studio/dashboard/samples/semiconductor-photoresist.dashboard.json'
import type { DashboardDocument } from '@/pages/pc/studio/dashboard/model'

describe('dashboard data sources', () => {
  it('turns the current sample into shared source bindings without changing layout or live sources', () => {
    const original = sample.document as DashboardDocument
    const next = shareDashboardConstants(original, '共享指标')
    expect(original.instances).toEqual([])
    expect(
      next.components.map((component) => [component.id, component.x, component.y, component.props]),
    ).toEqual(
      original.components.map((component) => [
        component.id,
        component.x,
        component.y,
        component.props,
      ]),
    )
    const instances = constantInstances(next)
    for (let i = 0; i < original.components.length; i++) {
      for (let j = 0; j < original.components[i]!.bindings.length; j++) {
        const before = original.components[i]!.bindings[j]!
        const after = next.components[i]!.bindings[j]!
        const source = after.source
        const resolved = source.path.reduce(
          (value: unknown, segment: string) => (value as Record<string, unknown>)[segment],
          instances[source.instanceId]!.data,
        )
        expect(resolved).toEqual(before.source.value)
      }
    }
    expect(next.instances.find((source) => source.label === '共享指标')?.value.output_value).toBe(
      1480,
    )
    expect(shareDashboardConstants(next, '二次应用').instances).toEqual(next.instances)
  })
  it('schedules fast and slow sources independently and never polls constants or manual sources', () => {
    const document = {
      instances: [
        { instanceId: 'api', sourceType: 'Api', refreshSeconds: 2 },
        { instanceId: 'sql', refreshSeconds: 10 },
        { instanceId: 'constant', sourceType: 'Constant', value: { output: 0 }, refreshSeconds: 1 },
        { instanceId: 'manual', refreshSeconds: 0 },
      ],
    }
    const schedule = createDataSchedule()
    schedule.mark(['api', 'sql', 'constant', 'manual'], 0)
    expect(schedule.due(document, 2000)).toEqual(['api'])
    schedule.mark(['api'], 2000)
    expect(schedule.due(document, 4000)).toEqual(['api'])
    expect(schedule.due(document, 10000)).toEqual(['api', 'sql'])
    expect(constantInstances(document)).toEqual({
      constant: { data: { output: 0 }, constant: true },
    })
  })

  it('merges partial results, preserves failed source data and clears failures on recovery', () => {
    const original = {
      revisionNId: 'v1',
      instances: { api: { data: { value: 12 } }, sql: { data: [] } },
    } as unknown as DashboardRuntime
    const failed = mergeRuntime(original, {
      ...original,
      instances: { api: { data: null, error: { code: 'SOURCE_TIMEOUT' } } },
    })
    expect(failed.instances.api?.data).toEqual({ value: 12 })
    expect(failed.instances.api?.error).toBeDefined()
    expect(failed.instances.sql).toEqual({ data: [] })
    const recovered = mergeRuntime(failed, {
      ...original,
      instances: { api: { data: { value: 0 } } },
    })
    expect(recovered.instances.api).toEqual({ data: { value: 0 } })
    const newRevision = { ...original, revisionNId: 'v2', instances: {} }
    expect(mergeRuntime(original, newRevision).instances).toEqual({})
  })
})
