import type { Json } from '@/api/studio'

/** Opaque-origin iframes authenticate messages by exact WindowProxy + per-document challenge. */
export function createCustomBridge(
  target: () => Window | null,
  bridgeId: string,
  execute: (method: 'query' | 'executeAction', args: Json) => Promise<unknown>,
) {
  let active = true,
    pending = 0
  const requests = new Map<string, { fingerprint: string; result: Promise<unknown> }>()
  async function receive(event: MessageEvent) {
    if (!active || event.source !== target() || !event.source) return
    const data = event.data as Json | null
    if (!data || typeof data !== 'object' || data.bridgeId !== bridgeId || data.type !== 'request')
      return
    if (typeof data.id !== 'string' || !/^[0-9]{1,8}$/.test(data.id)) return
    const reply = (result: unknown, error?: string) => {
      if (active && event.source === target())
        target()?.postMessage({ bridgeId, type: 'response', id: data.id, result, error }, '*')
    }
    if (
      !['query', 'executeAction'].includes(data.method) ||
      !data.args ||
      typeof data.args !== 'object' ||
      Array.isArray(data.args)
    ) {
      reply(null, 'STUDIO_VALIDATION')
      return
    }
    let fingerprint: string
    try {
      fingerprint = JSON.stringify({ method: data.method, args: data.args })
    } catch {
      reply(null, 'STUDIO_VALIDATION')
      return
    }
    if (fingerprint.length > 128 * 1024) {
      reply(null, 'STUDIO_LIMIT')
      return
    }
    const previous = requests.get(data.id)
    if (previous && previous.fingerprint !== fingerprint) {
      reply(null, 'STUDIO_IDEMPOTENCY_CONFLICT')
      return
    }
    if (!previous && (pending >= 8 || requests.size >= 64)) {
      reply(null, 'STUDIO_LIMIT')
      return
    }
    if (!previous) {
      pending++
      const result = Promise.resolve()
        .then(() => execute(data.method, data.args))
        .finally(() => {
          pending--
        })
      requests.set(data.id, { fingerprint, result })
    }
    try {
      reply(await requests.get(data.id)!.result)
    } catch (e) {
      reply(null, e instanceof Error ? e.message : 'STUDIO_CUSTOM_CHANNEL_CLOSED')
    }
  }
  return {
    receive,
    close: () => {
      active = false
      requests.clear()
    },
  }
}
