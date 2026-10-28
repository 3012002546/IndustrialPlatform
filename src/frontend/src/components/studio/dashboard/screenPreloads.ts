import type { Json } from '@/api/studio'
import type { loadDashboardScreen } from '@/api/dashboardScreen'

type ScreenRuntime = Awaited<ReturnType<typeof loadDashboardScreen>>
type Entry = {
  steps: Json[]
  value?: ScreenRuntime
  received?: number
  controller?: AbortController
  promise?: Promise<ScreenRuntime>
  bytes?: number
}

/** A bounded, page-owned preview buffer. Every actual navigation still revalidates access. */
export function createScreenPreloads(
  request: (steps: Json[], signal: AbortSignal) => Promise<ScreenRuntime>,
) {
  const entries = new Map<string, Entry>()
  let queue: Entry[] = []
  let active = 0
  let epoch = 0
  let bytes = 0
  const key = (steps: Json[]) => JSON.stringify(steps)
  function remove(id: string) {
    bytes -= entries.get(id)?.bytes ?? 0
    entries.delete(id)
  }
  function put(steps: Json[], value: ScreenRuntime, received = Date.now()) {
    const id = key(steps)
    remove(id)
    const size = new TextEncoder().encode(JSON.stringify(value)).length
    if (size > 8 * 1024 * 1024) return
    while (entries.size >= 10 || bytes + size > 8 * 1024 * 1024)
      remove(entries.keys().next().value!)
    entries.set(id, { steps, value, received, bytes: size })
    bytes += size
  }
  function pump() {
    while (active < 2 && queue.length) {
      const entry = queue.shift()!
      const current = epoch
      entry.controller = new AbortController()
      const timeout = setTimeout(() => entry.controller?.abort(), 30000)
      active++
      entry.promise = request(entry.steps, entry.controller.signal)
      void entry.promise
        .then((value) => {
          if (current === epoch && entries.get(key(entry.steps)) === entry) put(entry.steps, value)
        })
        .catch(() => {
          if (current === epoch) remove(key(entry.steps))
        })
        .finally(() => {
          clearTimeout(timeout)
          if (current === epoch) {
            active--
            pump()
          }
        })
    }
  }
  function stop() {
    epoch++
    for (const entry of entries.values()) entry.controller?.abort()
    for (const [id, entry] of entries) if (!entry.value) remove(id)
    queue = []
    active = 0
  }
  return {
    put,
    warm(steps: Json[]) {
      const id = key(steps)
      if (entries.has(id)) return
      const entry = { steps }
      entries.set(id, entry)
      queue.push(entry)
      pump()
    },
    take(steps: Json[]): Entry | undefined {
      const id = key(steps)
      const entry = entries.get(id)
      remove(id)
      queue = queue.filter((item) => item !== entry)
      if (!entry?.value) return entry?.promise ? entry : undefined
      const interval = Math.min(
        30000,
        ...((entry.value.document.instances as Json[]) ?? [])
          .filter((source) => source.sourceType !== 'Constant' && Number(source.refreshSeconds) > 0)
          .map((source) => Number(source.refreshSeconds) * 1000),
      )
      return Date.now() - entry.received! < interval &&
        (entry.value.expiresOn == null || Date.parse(entry.value.expiresOn) > Date.now())
        ? entry
        : undefined
    },
    stop,
    clear() {
      stop()
      entries.clear()
      bytes = 0
    },
  }
}
