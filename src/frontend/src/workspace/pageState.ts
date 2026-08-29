/**
 * 当前浏览器会话内的业务页状态。只允许查询/分页/排序/滚动这些可恢复 UI 状态，
 * 不使用 localStorage，也不接受 token、权限或个人资料等身份/安全字段。
 */

export interface WorkspacePageSort {
  field: string
  direction: 'asc' | 'desc'
}

export interface WorkspacePageState {
  query?: Readonly<Record<string, string | string[]>>
  pageIndex?: number
  pageSize?: number
  sort?: readonly WorkspacePageSort[]
  scrollTop?: number
}

export interface WorkspacePageStateStorage {
  getItem(key: string): string | null
  setItem(key: string, value: string): void
  removeItem(key: string): void
}

const PAGE_STATE_KEY_PREFIX = 'industrial-platform.pc.page-state.v1'

export function buildPageStateKey(tabId: string): string {
  return `${PAGE_STATE_KEY_PREFIX}:${encodeURIComponent(tabId)}`
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null
}

function isStringRecord(value: unknown): value is Record<string, string | string[]> {
  if (!isRecord(value)) return false
  return Object.values(value).every(
    (item) => typeof item === 'string' || (Array.isArray(item) && item.every((entry) => typeof entry === 'string')),
  )
}

function isValidState(value: unknown): value is WorkspacePageState {
  if (!isRecord(value)) return false
  const allowed = new Set(['query', 'pageIndex', 'pageSize', 'sort', 'scrollTop'])
  if (Object.keys(value).some((key) => !allowed.has(key))) return false
  if (value['query'] !== undefined && !isStringRecord(value['query'])) return false
  if (
    value['pageIndex'] !== undefined &&
    (!Number.isInteger(value['pageIndex']) || (value['pageIndex'] as number) < 1)
  ) return false
  if (
    value['pageSize'] !== undefined &&
    (!Number.isInteger(value['pageSize']) || (value['pageSize'] as number) < 1 || (value['pageSize'] as number) > 100)
  ) return false
  if (value['scrollTop'] !== undefined && (!Number.isFinite(value['scrollTop']) || (value['scrollTop'] as number) < 0)) {
    return false
  }
  if (value['sort'] !== undefined) {
    if (!Array.isArray(value['sort']) || value['sort'].length > 3) return false
    for (const entry of value['sort']) {
      if (!isRecord(entry) || typeof entry['field'] !== 'string' || entry['field'].length === 0) return false
      if (entry['direction'] !== 'asc' && entry['direction'] !== 'desc') return false
    }
  }
  return true
}

export function readPageState(
  storage: WorkspacePageStateStorage,
  tabId: string,
): WorkspacePageState | null {
  try {
    const raw = storage.getItem(buildPageStateKey(tabId))
    if (raw === null) return null
    const value: unknown = JSON.parse(raw)
    return isValidState(value) ? value : null
  } catch {
    return null
  }
}

export function writePageState(
  storage: WorkspacePageStateStorage,
  tabId: string,
  state: WorkspacePageState,
): boolean {
  if (!isValidState(state)) return false
  try {
    storage.setItem(buildPageStateKey(tabId), JSON.stringify(state))
    return true
  } catch {
    return false
  }
}

export function clearPageState(storage: WorkspacePageStateStorage, tabId: string): void {
  try {
    storage.removeItem(buildPageStateKey(tabId))
  } catch {
    // Session storage errors must not block closing a tab.
  }
}

export const removePageState = clearPageState
