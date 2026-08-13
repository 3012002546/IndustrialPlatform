/**
 * workspace 稳定公共出口(PF-01 §7.9)。
 * 页面/布局/守卫只从本入口消费;纯模块不引用 Vue 组件或 Router 实例。
 */

export {
  FIXED_WORKBENCH_ID,
  FIXED_WORKBENCH_TITLE,
  MAX_BUSINESS_TABS,
  buildTabId,
  createFixedWorkbench,
  serializeLocationParts,
  toPersistedRoute,
} from './identity'
export {
  buildUserTabsKey,
  parseTabsSnapshot,
  readTabsSnapshot,
  serializeTabsSnapshot,
  writeTabsSnapshot,
} from './persistence'
export type {
  OpenTabResult,
  PersistedRouteLocation,
  TabLimitResolution,
  WorkspaceRouteCandidate,
  WorkspaceTab,
  WorkspaceUserScope,
} from './types'
