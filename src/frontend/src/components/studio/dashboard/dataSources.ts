import type { DashboardRuntime, Json } from '@/api/studio'
import type { DashboardDocument } from '@/pages/pc/studio/dashboard/model'

/** Move inline demo values into a shared source without replacing layout or live bindings. */
export function shareDashboardConstants(
  document: DashboardDocument,
  label: string,
): DashboardDocument {
  const next = JSON.parse(JSON.stringify(document)) as DashboardDocument
  const metricId = `sample-${crypto.randomUUID()}`
  const metrics: Json = {}
  for (const component of next.components) {
    const bindings = component.bindings.filter((binding) => binding.source?.from === 'Constant')
    if (!bindings.length) continue
    const isMetric = component.type === 'Number'
    const id = isMetric ? metricId : `sample-${crypto.randomUUID()}`
    const data = Object.fromEntries(
      bindings.map((binding) => [binding.property, binding.source.value]),
    )
    const single = bindings.length === 1
    for (const binding of bindings) {
      const key = `${component.id}_${binding.property}`
      if (isMetric) metrics[key] = binding.source.value
      binding.source = {
        ...binding.source,
        from: 'Instance',
        instanceId: id,
        path: isMetric ? [key] : single ? [] : [binding.property],
      }
      delete binding.source.value
    }
    if (!isMetric)
      next.instances.push({
        instanceId: id,
        sourceType: 'Constant',
        label: component.name,
        value: single ? Object.values(data)[0] : data,
        refreshSeconds: 0,
      })
  }
  if (Object.keys(metrics).length)
    next.instances.push({
      instanceId: metricId,
      sourceType: 'Constant',
      label,
      value: metrics,
      refreshSeconds: 0,
    })
  return next
}

export function constantInstances(document: Json): Record<string, Json> {
  return Object.fromEntries(
    ((document.instances as Json[]) ?? [])
      .filter((source) => source.sourceType === 'Constant')
      .map((source) => [String(source.instanceId), { data: source.value, constant: true }]),
  )
}

export function dataPaths(value: unknown, prefix = '', depth = 0): string[] {
  if (depth > 12 || !value || typeof value !== 'object') return []
  return Object.entries(value)
    .flatMap(([key, child]) => {
      const path = prefix ? `${prefix}.${key}` : key
      return [path, ...dataPaths(child, path, depth + 1)]
    })
    .slice(0, 500)
}

/** Each source owns its due time; callers serialize requests and mark completed reads. */
export function createDataSchedule() {
  const completed = new Map<string, number>()
  return {
    reset() {
      completed.clear()
    },
    mark(ids: string[], now = Date.now()) {
      ids.forEach((id) => completed.set(id, now))
    },
    due(document: Json, now = Date.now()): string[] {
      return ((document.instances as Json[]) ?? [])
        .filter((source) => {
          const seconds = Number(source.refreshSeconds ?? 0)
          return (
            source.sourceType !== 'Constant' &&
            Number.isFinite(seconds) &&
            seconds > 0 &&
            now - (completed.get(String(source.instanceId)) ?? now) >= seconds * 1000
          )
        })
        .map((source) => String(source.instanceId))
    },
    delay(document: Json, ceiling = 30000, now = Date.now()): number {
      const delays = ((document.instances as Json[]) ?? [])
        .filter((source) => source.sourceType !== 'Constant' && Number(source.refreshSeconds) > 0)
        .map(
          (source) =>
            Number(source.refreshSeconds) * 1000 -
            (now - (completed.get(String(source.instanceId)) ?? now)),
        )
      return Math.max(1000, Math.min(ceiling, ...delays))
    },
  }
}

/** Partial screen data is reusable only for the same server-resolved parameters. */
export function requiresFullRuntime(
  previous: DashboardRuntime,
  incoming: DashboardRuntime,
): boolean {
  return (
    previous.revisionNId === incoming.revisionNId &&
    JSON.stringify((previous as Json).parameters ?? {}) !==
      JSON.stringify((incoming as Json).parameters ?? {})
  )
}

export function mergeRuntime<T extends DashboardRuntime>(previous: T | null, incoming: T): T {
  if (!previous || previous.revisionNId !== incoming.revisionNId) return incoming
  const instances = { ...previous.instances, ...incoming.instances }
  for (const [id, execution] of Object.entries(incoming.instances ?? {})) {
    // Keep last readable data for transient failures, with the failure still visible.
    if (execution.error && previous.instances[id])
      instances[id] = { ...execution, data: previous.instances[id].data }
  }
  return { ...incoming, instances }
}
