/**
 * 工作台标签 Store(PF-01 §7.9/§8.2):12 标签治理与独立用户持久化。
 * - 固定工作台始终存在且不可关闭;业务标签最多 12 个,第 13 个导航前阻断。
 * - 恢复时只做结构校验与配额封顶;路由存在/权限过滤由守卫经 prune(isAllowed) 完成,
 *   Store 不持有 Router 实例、不导入 AuthStore。
 * - bindUser() 同一 scope 幂等;标签快照与主题 bootstrap 分离。
 */

import { defineStore } from 'pinia'
import { computed, ref } from 'vue'

import type { UserUiScope } from '@/theme/types'
import { createFixedWorkbench, MAX_BUSINESS_TABS } from '@/workspace/identity'
import { clearPageState } from '@/workspace/pageState'
import { buildUserTabsKey, readTabsSnapshot, writeTabsSnapshot, type WorkspaceStorage } from '@/workspace/persistence'
import type {
  OpenTabResult,
  PersistedRouteLocation,
  TabLimitResolution,
  WorkspaceRouteCandidate,
  WorkspaceTab,
} from '@/workspace/types'

function defaultStorage(): WorkspaceStorage {
  return globalThis.localStorage
}

function clearStoredPageState(tabId: string): void {
  try {
    clearPageState(globalThis.sessionStorage, tabId)
  } catch {
    // Closing a tab remains best-effort even when sessionStorage is unavailable.
  }
}

function sameScope(a: UserUiScope, b: UserUiScope): boolean {
  return a.tenantId === b.tenantId && a.userId === b.userId
}

export const useWorkspaceTabsStore = defineStore('workspaceTabs', () => {
  /** 当前用户标签(固定工作台恒在第 0 位)。 */
  const tabs = ref<WorkspaceTab[]>([])
  /** 当前激活标签 id。 */
  const activeTabId = ref('')
  /** 导航前被阻断的业务路由:非空时 PcLayout 展示上限对话框。 */
  const pending = ref<PersistedRouteLocation | null>(null)
  /** 已绑定用户作用域(null 表示未绑定)。 */
  const scope = ref<UserUiScope | null>(null)
  /** 是否已完成首次恢复(幂等)。 */
  const ready = ref(false)

  /** 已绑定作用域(避免重复恢复)。 */
  let boundScope: UserUiScope | null = null

  const activeTab = computed(() => tabs.value.find((t) => t.id === activeTabId.value) ?? null)
  const businessTabs = computed(() => tabs.value.filter((t) => t.kind === 'business'))
  const fixedTab = computed(
    () => tabs.value.find((t) => t.kind === 'fixed') ?? createFixedWorkbench(),
  )

  function persist(): void {
    if (scope.value === null) return
    writeTabsSnapshot(defaultStorage(), scope.value, {
      version: 1,
      tabs: tabs.value,
      activeTabId: activeTabId.value,
      updatedAt: new Date(Date.now()).toISOString(),
    })
  }

  /**
   * 绑定用户作用域:读取快照并恢复(结构校验 + 业务标签封顶),
   * 保证固定工作台存在;权限/路由过滤由守卫随后调用 prune。
   * 同一 scope 幂等。
   */
  function bindUser(nextScope: UserUiScope): void {
    if (boundScope !== null && sameScope(boundScope, nextScope) && ready.value) return
    boundScope = nextScope
    scope.value = nextScope
    const snapshot = readTabsSnapshot(defaultStorage(), nextScope)
    const restored = snapshot === null ? [] : snapshot.tabs
    const fixed = restored.find((t) => t.kind === 'fixed') ?? createFixedWorkbench()
    const businesses = restored.filter((t) => t.kind === 'business').slice(0, MAX_BUSINESS_TABS)
    tabs.value = [fixed, ...businesses]
    const savedActive = snapshot?.activeTabId ?? ''
    activeTabId.value = tabs.value.some((t) => t.id === savedActive) ? savedActive : fixed.id
    pending.value = null
    persist()
    ready.value = true
  }

  /**
   * 打开/激活业务标签(守卫在导航确认前调用):
   * 同一身份激活不重复新增;已达 12 个时保存 pending 并返回 limit-reached。
   */
  function requestOpen(candidate: WorkspaceRouteCandidate): OpenTabResult {
    if (candidate.kind === 'fixed') {
      const fixed = tabs.value.find((t) => t.kind === 'fixed')
      if (fixed !== undefined) {
        tabs.value = [fixed, ...tabs.value.filter((t) => t.kind === 'business')]
        activeTabId.value = fixed.id
        return { kind: 'activated', tab: fixed }
      }
      const workbench = createFixedWorkbench()
      tabs.value = [workbench, ...tabs.value.filter((t) => t.kind === 'business')]
      activeTabId.value = workbench.id
      return { kind: 'activated', tab: workbench }
    }
    if (candidate.kind !== 'business') return { kind: 'ignored' }

    const existing = tabs.value.find((t) => t.kind === 'business' && t.id === candidate.id)
    if (existing !== undefined) {
      activeTabId.value = existing.id
      return { kind: 'activated', tab: existing }
    }
    if (businessTabs.value.length >= MAX_BUSINESS_TABS) {
      pending.value = candidate.route
      return { kind: 'limit-reached', pending: candidate.route }
    }
    const tab: WorkspaceTab = {
      id: candidate.id,
      title: candidate.fallbackTitle ?? candidate.title,
      ...(candidate.titleKey === undefined ? {} : { titleKey: candidate.titleKey }),
      fallbackTitle: candidate.fallbackTitle ?? candidate.title,
      kind: 'business',
      route: candidate.route,
      reloadVersion: 1,
    }
    tabs.value.push(tab)
    activeTabId.value = tab.id
    persist()
    return { kind: 'opened', tab }
  }

  /**
   * 关闭标签:返回关闭后应激活的标签。
   * 固定工作台传入时原样返回且不删除;业务标签关闭后激活右邻、左邻或固定工作台。
   * 仅当关闭的是激活标签时才改写 activeTabId(关闭后台标签不打断当前页面)。
   */
  function closeTab(tabId: string): WorkspaceTab {
    const target = tabs.value.find((t) => t.id === tabId)
    if (target === undefined || target.kind === 'fixed' || target.pinned === true) return fixedTab.value
    const wasActive = activeTabId.value === tabId
    const index = tabs.value.indexOf(target)
    tabs.value = tabs.value.filter((t) => t.id !== tabId)
    clearStoredPageState(tabId)
    const next = tabs.value[index] ?? tabs.value[index - 1] ?? fixedTab.value
    if (wasActive) activeTabId.value = next.id
    persist()
    return next
  }

  /** 关闭其他业务标签(保留固定工作台与目标标签);激活标签被移除时才改 activeTabId。 */
  function closeOthers(tabId: string): void {
    if (!tabs.value.some((t) => t.id === tabId)) return
    const removed = tabs.value.filter((t) => t.kind === 'business' && t.id !== tabId && t.pinned !== true)
    tabs.value = tabs.value.filter((t) => t.kind === 'fixed' || t.pinned === true || t.id === tabId)
    removed.forEach((tab) => clearStoredPageState(tab.id))
    if (!tabs.value.some((t) => t.id === activeTabId.value)) {
      activeTabId.value = tabs.value.find((t) => t.id === tabId)?.id ?? fixedTab.value.id
    }
    persist()
  }

  /** 关闭目标标签右侧的业务标签(不含目标本身);激活标签被移除时改为目标标签。 */
  function closeRight(tabId: string): void {
    const index = tabs.value.findIndex((t) => t.id === tabId)
    if (index === -1) return
    const kept: WorkspaceTab[] = []
    const removed: WorkspaceTab[] = []
    tabs.value.forEach((t, i) => {
      if (t.kind === 'fixed' || i <= index || t.pinned === true) kept.push(t)
      else removed.push(t)
    })
    tabs.value = kept
    removed.forEach((tab) => clearStoredPageState(tab.id))
    if (!tabs.value.some((t) => t.id === activeTabId.value)) activeTabId.value = tabId
    persist()
  }

  /** 关闭目标左侧的业务标签;固定工作台永远保留,当前标签被移除时激活目标。 */
  function closeLeft(tabId: string): void {
    const index = tabs.value.findIndex((t) => t.id === tabId)
    if (index <= 0) return
    const removed = tabs.value.filter(
      (t, currentIndex) => t.kind === 'business' && currentIndex < index && t.pinned !== true,
    )
    tabs.value = tabs.value.filter(
      (t, currentIndex) => t.kind === 'fixed' || currentIndex >= index || t.pinned === true,
    )
    removed.forEach((tab) => clearStoredPageState(tab.id))
    if (!tabs.value.some((t) => t.id === activeTabId.value)) {
      activeTabId.value = tabs.value.find((t) => t.id === tabId)?.id ?? fixedTab.value.id
    }
    persist()
  }

  /** 关闭全部业务标签,仅保留并激活固定工作台。 */
  function closeAll(): void {
    const removed = tabs.value.filter((t) => t.kind === 'business' && t.pinned !== true)
    tabs.value = tabs.value.filter((t) => t.kind === 'fixed' || t.pinned === true)
    removed.forEach((tab) => clearStoredPageState(tab.id))
    activeTabId.value = fixedTab.value.id
    persist()
  }

  function setTabPinned(tabId: string, pinned: boolean): boolean {
    const tab = tabs.value.find((candidate) => candidate.id === tabId)
    if (tab === undefined || tab.kind === 'fixed') return false
    if (pinned) tab.pinned = true
    else delete tab.pinned
    persist()
    return true
  }

  /** 通过 Store 激活已存在标签并持久化,避免页面直接改写 activeTabId。 */
  function activateTab(tabId: string): WorkspaceTab | null {
    const tab = tabs.value.find((t) => t.id === tabId)
    if (tab === undefined) return null
    if (activeTabId.value !== tab.id) {
      activeTabId.value = tab.id
      persist()
    }
    return tab
  }

  /** 递增指定业务标签 reloadVersion(触发其路由内容重挂载)。 */
  function reloadTab(tabId: string): void {
    const tab = tabs.value.find((t) => t.id === tabId)
    if (tab === undefined || tab.kind !== 'business') return
    tab.reloadVersion += 1
    persist()
  }

  /** 递增当前业务标签 reloadVersion(触发 RouterView 内容重挂载)。 */
  function reloadCurrent(): void {
    reloadTab(activeTabId.value)
  }

  /** 清除当前用户的可恢复工作区状态；认证、主题和语言不在此 Store 内处理。 */
  function clearUiCache(): void {
    tabs.value = [createFixedWorkbench()]
    activeTabId.value = tabs.value[0]!.id
    pending.value = null
    if (scope.value !== null) {
      try {
        globalThis.localStorage.removeItem(buildUserTabsKey(scope.value))
      } catch {
        // Cache cleanup is best effort; the in-memory state is still reset.
      }
    }
  }

  /**
   * 处理上限对话框决议(§7.9):返回应导航的目标路由,取消返回 null。
   * close-and-open → 关闭所选标签并返回 pending 目标;reuse → 激活所选标签并返回其路由。
   */
  function resolvePending(resolution: TabLimitResolution): PersistedRouteLocation | null {
    const route = pending.value
    pending.value = null
    if (resolution.action === 'cancel') return null
    if (resolution.action === 'reuse') {
      const tab = tabs.value.find((t) => t.kind === 'business' && t.id === resolution.tabId)
      if (tab !== undefined) {
        activeTabId.value = tab.id
        persist()
        return tab.route
      }
      return null
    }
    const target = tabs.value.find((t) => t.kind === 'business' && t.id === resolution.tabId)
    if (target !== undefined) closeTab(target.id)
    if (route !== null) persist()
    return route
  }

  /** 按授权过滤业务标签(守卫提供 isAllowed);固定工作台始终保留。 */
  function prune(isAllowed: (tab: WorkspaceTab) => boolean): void {
    const before = tabs.value.length
    const removed = tabs.value.filter((t) => t.kind === 'business' && !isAllowed(t))
    tabs.value = tabs.value.filter((t) => t.kind === 'fixed' || isAllowed(t))
    if (tabs.value.length >= before) return
    removed.forEach((tab) => clearStoredPageState(tab.id))
    if (!tabs.value.some((t) => t.id === activeTabId.value)) {
      activeTabId.value = tabs.value.find((t) => t.kind === 'fixed')?.id ?? tabs.value[0]?.id ?? ''
    }
    persist()
  }

  return {
    tabs,
    activeTabId,
    pending,
    scope,
    ready,
    activeTab,
    businessTabs,
    fixedTab,
    bindUser,
    requestOpen,
    closeTab,
    closeOthers,
    closeRight,
    closeLeft,
    closeAll,
    setTabPinned,
    activateTab,
    reloadTab,
    reloadCurrent,
    clearUiCache,
    resolvePending,
    prune,
  }
})
