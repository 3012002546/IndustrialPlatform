import type { DashboardRuntime, Json } from '@/api/studio'

// 仅传递刚完成授权校验的一次跳转结果；不持久化、不供后续刷新复用。
let handoff: { path: string; value: DashboardRuntime; created: number } | undefined
export function handoffDashboardRuntime(path: string, value: DashboardRuntime) {
  handoff = { path, value, created: Date.now() }
}
export function takeDashboardRuntime(path: string): DashboardRuntime | undefined {
  const pending = handoff
  handoff = undefined
  return pending?.path === path && Date.now() - pending.created < 5000 ? pending.value : undefined
}

export function parseQueryObject(value: unknown): Json {
  if (typeof value !== 'string' || !value) return {}
  try {
    const parsed: unknown = JSON.parse(value)
    return parsed && typeof parsed === 'object' && !Array.isArray(parsed) ? (parsed as Json) : {}
  } catch {
    return {}
  }
}

export function rowKeyFor(component: Json, row: Json): Json {
  const path = component.props?.rowKeyPath
  let current: unknown = row
  for (const segment of Array.isArray(path) ? path : []) {
    if (current == null || typeof current !== 'object') break
    current = (current as Json)[String(segment)]
  }
  return { key: current ?? row.nId ?? row.id ?? row.key }
}
