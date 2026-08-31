/**
 * 稳定页面身份与可持久化路由(PF-01 §7.9)。
 * 纯模块:params/query 排序必须确定,非法值不得抛出。
 * 业务标签 ID = route.name + 排序后的 params + 排序后的 query。
 */

import type { PersistedRouteLocation, WorkspaceTab } from './types'

/**
 * 静态 PC 工作区路由的标题资源键。
 *
 * 标签快照早于 titleKey 字段时无法从 Router meta 恢复资源键；这里保留
 * 一份纯数据契约，避免 Store 依赖 Router，同时让旧快照在切换语言后仍
 * 能即时解析为当前语言。未列出的 DEV/动态路由继续使用其 fallbackTitle。
 */
const STATIC_WORKSPACE_TITLE_KEYS: Readonly<Record<string, string>> = {
  'pc-home': 'shell.navigation.workspace',
  'identity-users': 'shell.navigation.item.identity-users',
  'identity-user-groups': 'shell.navigation.item.identity-user-groups',
  'identity-roles': 'shell.navigation.item.identity-roles',
  'identity-permissions': 'shell.navigation.item.identity-permissions',
  'identity-audits': 'shell.navigation.item.identity-audits',
  'sso-providers': 'shell.navigation.item.identity-sso-providers',
  'sso-clients': 'shell.navigation.item.identity-sso-clients',
  'systemdata-organizations': 'shell.navigation.item.systemdata-organizations',
  'systemdata-assignments': 'shell.navigation.item.systemdata-assignments',
  'systemdata-navigation': 'shell.navigation.item.systemdata-navigation',
  'systemdata-features': 'shell.navigation.item.systemdata-features',
  'systemdata-services': 'shell.navigation.item.systemdata-services',
  'systemdata-themes': 'shell.navigation.item.systemdata-themes',
  'systemdata-service-initialization': 'shell.navigation.item.systemdata-service-initialization',
}

/** 固定工作台(id 对应 pc-home 路由名);不参与业务标签配额。 */
export const FIXED_WORKBENCH_ID = 'pc-home'

/** 固定工作台默认标题(路由 meta.title 为页面标题,标签标题稳定为「工作台」)。 */
export const FIXED_WORKBENCH_TITLE = '工作台'

/** 业务标签上限:第 13 个必须在导航前被阻止(§6/§7.9)。 */
export const MAX_BUSINESS_TABS = 12

/** 序列化路由参数/查询:键排序、值编码、数组逐项展开,非法值安全转字符串。 */
export function serializeLocationParts(parts: Readonly<Record<string, unknown>>): string {
  return Object.keys(parts)
    .sort()
    .map((key) => {
      const value = parts[key]
      const values = Array.isArray(value) ? value.map((v) => String(v)) : [String(value)]
      return values.map((v) => `${encodeURIComponent(key)}=${encodeURIComponent(v)}`).join('&')
    })
    .filter((segment) => segment.length > 0)
    .join('&')
}

/** 业务标签稳定 ID:name + 排序后的 params + 排序后的 query;无参数时退化为 name。 */
export function buildTabId(
  name: string,
  params: Readonly<Record<string, unknown>> = {},
  query: Readonly<Record<string, unknown>> = {},
): string {
  const p = serializeLocationParts(params)
  const q = serializeLocationParts(query)
  const suffix = [p.length > 0 ? `p=${p}` : '', q.length > 0 ? `q=${q}` : '']
    .filter((s) => s.length > 0)
    .join('&')
  return suffix.length > 0 ? `${name}&${suffix}` : name
}

/**
 * 把路由输入归一化为稳定可持久化路由位置(vue-router 的 query 允许 null,
 * 仅保留已注册路由名;null/undefined 值丢弃)。
 */
export function toPersistedRoute(route: {
  name: string
  params: Readonly<Record<string, unknown>>
  query: Readonly<Record<string, unknown>>
}): PersistedRouteLocation {
  const params: Record<string, string | string[]> = {}
  for (const [key, value] of Object.entries(route.params)) {
    if (value === null || value === undefined) continue
    params[key] = Array.isArray(value) ? value.map((v) => String(v)) : String(value)
  }
  const query: Record<string, string | string[]> = {}
  for (const [key, value] of Object.entries(route.query)) {
    if (value === null || value === undefined) continue
    query[key] = Array.isArray(value) ? value.map((v) => String(v)) : String(value)
  }
  return { name: route.name, params, query }
}

/** 为缺少资源键的历史静态标签补齐当前路由的规范化标题键。 */
export function normalizeWorkspaceTabTitle(tab: WorkspaceTab): WorkspaceTab {
  if (tab.titleKey !== undefined) return tab
  const titleKey = STATIC_WORKSPACE_TITLE_KEYS[tab.route.name]
  return titleKey === undefined ? tab : { ...tab, titleKey }
}

/** 固定工作台标签(store 恢复时保证存在,不计入业务配额)。 */
export function createFixedWorkbench(): WorkspaceTab {
  return {
    id: FIXED_WORKBENCH_ID,
    title: FIXED_WORKBENCH_TITLE,
    titleKey: 'shell.navigation.workspace',
    fallbackTitle: FIXED_WORKBENCH_TITLE,
    kind: 'fixed',
    route: { name: FIXED_WORKBENCH_ID, params: {}, query: {} },
    reloadVersion: 0,
  }
}
