/**
 * PC 标签快照解析与用户键(PF-01 §8.2)。
 * 纯模块:非法 JSON、未知版本、非法字段与存储异常一律安全回退,不抛到页面。
 * 恢复时路由与权限双校验由守卫经 Store.prune 完成;本模块只做结构校验与配额封顶。
 */

import type { UserUiScope } from '@/theme/types'

import { MAX_BUSINESS_TABS } from './identity'
import type { WorkspaceTab } from './types'

/** PC 标签快照存储键前缀(实际键见 buildUserTabsKey)。 */
const PC_TABS_STORAGE_KEY_PREFIX = 'industrial-platform.pc.tabs.v1'

/** 存储抽象:与 theme/preferences 同形,jsdom/浏览器均可用。 */
export interface WorkspaceStorage {
  getItem(key: string): string | null
  setItem(key: string, value: string): void
  removeItem(key: string): void
}

export interface WorkspaceTabsSnapshot {
  version: 1
  tabs: WorkspaceTab[]
  activeTabId: string
  /** ISO 8601,仅用于恢复诊断,不参与业务排序或授权。 */
  updatedAt: string
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null
}

function isString(value: unknown): value is string {
  return typeof value === 'string'
}

function isKind(value: unknown): value is WorkspaceTab['kind'] {
  return value === 'fixed' || value === 'business'
}

function optionalString(value: unknown): string | undefined {
  return value === undefined ? undefined : isString(value) && value.length > 0 ? value : undefined
}

function isLocationPart(value: unknown): value is Record<string, string | string[]> {
  if (!isRecord(value)) return false
  return Object.values(value).every((v) => isString(v) || (Array.isArray(v) && v.every(isString)))
}

/** 用户作用域 → 版本化用户标签键;标识只出现在键中,不重复进入 JSON 值。 */
export function buildUserTabsKey(scope: UserUiScope): string {
  const tenant = encodeURIComponent(scope.tenantId)
  const user = encodeURIComponent(scope.userId)
  return `${PC_TABS_STORAGE_KEY_PREFIX}:${tenant}:${user}`
}

/** 解析单个标签:字段非法即丢弃该标签(整体仍可用)。 */
function parseTab(value: unknown): WorkspaceTab | null {
  if (!isRecord(value)) return null
  if (!isString(value['id']) || value['id'].length === 0) return null
  const title = optionalString(value['title'])
  const fallbackTitle = optionalString(value['fallbackTitle']) ?? title
  const titleKey = optionalString(value['titleKey'])
  if (fallbackTitle === undefined) return null
  if (!isKind(value['kind'])) return null
  const route = value['route']
  if (!isRecord(route)) return null
  if (!isString(route['name']) || route['name'].length === 0) return null
  if (!isLocationPart(route['params'])) return null
  if (!isLocationPart(route['query'])) return null
  if (typeof value['reloadVersion'] !== 'number' || !Number.isInteger(value['reloadVersion'])) {
    return null
  }
  return {
    id: value['id'],
    title: fallbackTitle,
    ...(titleKey === undefined ? {} : { titleKey }),
    fallbackTitle,
    kind: value['kind'],
    ...(value['pinned'] === true ? { pinned: true } : {}),
    route: { name: route['name'], params: route['params'], query: route['query'] },
    reloadVersion: value['reloadVersion'],
  }
}

/**
 * 解析标签快照:版本未知或核心结构非法返回 null;单个非法标签丢弃;
 * 业务标签封顶 12 个,固定工作台保留至多一个并置前。
 */
export function parseTabsSnapshot(raw: string | null): WorkspaceTabsSnapshot | null {
  if (raw === null) return null
  let data: unknown
  try {
    data = JSON.parse(raw)
  } catch {
    return null
  }
  if (!isRecord(data)) return null
  if (data['version'] !== 1) return null
  if (!Array.isArray(data['tabs'])) return null
  if (!isString(data['activeTabId'])) return null
  if (!isString(data['updatedAt'])) return null

  const tabs: WorkspaceTab[] = []
  let fixed: WorkspaceTab | null = null
  let businessCount = 0
  for (const value of data['tabs']) {
    const tab = parseTab(value)
    if (tab === null) continue
    if (tab.kind === 'fixed') {
      if (fixed === null) fixed = tab
      continue
    }
    if (businessCount >= MAX_BUSINESS_TABS) continue
    tabs.push(tab)
    businessCount += 1
  }
  const ordered = fixed === null ? tabs : [fixed, ...tabs]
  const activeTabId = ordered.some((t) => t.id === data['activeTabId'])
    ? data['activeTabId']
    : (ordered[0]?.id ?? '')
  return { version: 1, tabs: ordered, activeTabId, updatedAt: data['updatedAt'] }
}

/** 序列化标签快照(写入方保证结构合法)。 */
export function serializeTabsSnapshot(snapshot: WorkspaceTabsSnapshot): string {
  return JSON.stringify({
    ...snapshot,
    tabs: snapshot.tabs.map((tab) => ({
      id: tab.id,
      titleKey: tab.titleKey,
      fallbackTitle: tab.fallbackTitle ?? tab.title,
      kind: tab.kind,
      ...(tab.pinned === true ? { pinned: true } : {}),
      route: tab.route,
      reloadVersion: tab.reloadVersion,
    })),
  })
}

/** 读取用户标签快照:存储异常/非法 JSON 均返回 null。 */
export function readTabsSnapshot(
  storage: WorkspaceStorage,
  scope: UserUiScope,
): WorkspaceTabsSnapshot | null {
  try {
    return parseTabsSnapshot(storage.getItem(buildUserTabsKey(scope)))
  } catch {
    return null
  }
}

/** 写入用户标签快照:返回是否成功(QuotaExceeded/SecurityError 等返回 false)。 */
export function writeTabsSnapshot(
  storage: WorkspaceStorage,
  scope: UserUiScope,
  snapshot: WorkspaceTabsSnapshot,
): boolean {
  try {
    storage.setItem(buildUserTabsKey(scope), serializeTabsSnapshot(snapshot))
    return true
  } catch {
    return false
  }
}
