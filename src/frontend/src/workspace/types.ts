/**
 * 受控业务标签类型(PF-01 §7.9)。纯类型,无 Vue/Router 依赖。
 * 稳定出口见 index.ts。
 */

/** 可持久化的路由位置:只按 Router 已注册记录恢复,禁止任意 URL/脚本。 */
export interface PersistedRouteLocation {
  name: string
  params: Record<string, string | string[]>
  query: Record<string, string | string[]>
}

/** 工作台标签:固定工作台不计入配额;业务标签最多 12 个。 */
export interface WorkspaceTab {
  id: string
  title: string
  kind: 'fixed' | 'business'
  route: PersistedRouteLocation
  /** 递增触发 RouterView 内容重挂载(不整页刷新)。 */
  reloadVersion: number
}

export type OpenTabResult =
  | { kind: 'opened'; tab: WorkspaceTab }
  | { kind: 'activated'; tab: WorkspaceTab }
  | { kind: 'limit-reached'; pending: PersistedRouteLocation }
  | { kind: 'ignored' }

export type TabLimitResolution =
  | { action: 'close-and-open'; tabId: string }
  | { action: 'reuse'; tabId: string }
  | { action: 'cancel' }

/** 守卫把 RouteLocationNormalized 归一化为候选;Store 不持有 Router 实例。 */
export interface WorkspaceRouteCandidate {
  id: string
  title: string
  kind: 'fixed' | 'business' | 'none'
  route: PersistedRouteLocation
  permission?: string
}

export type { UserUiScope as WorkspaceUserScope } from '@/theme/types'
