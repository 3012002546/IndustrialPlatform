/**
 * 工作台标签 Store 测试(PF-01 §7.9/§8.2):
 * bindUser 幂等恢复、requestOpen 打开/复用/固定激活/第 13 个阻断、
 * closeTab 确定性相邻导航、closeOthers/closeRight 仅激活被移除时改写 activeTabId、
 * reloadCurrent、resolvePending 三决议、prune 授权过滤。
 */

import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, describe, expect, it } from 'vitest'

import { useWorkspaceTabsStore } from '@/stores/workspaceTabsStore'
import { createFixedWorkbench, MAX_BUSINESS_TABS } from '@/workspace/identity'
import { writeTabsSnapshot } from '@/workspace/persistence'
import type {
  PersistedRouteLocation,
  WorkspaceRouteCandidate,
  WorkspaceTab,
} from '@/workspace/types'
import type { UserUiScope } from '@/theme/types'

const SCOPE: UserUiScope = { tenantId: 't1', userId: 'u1' }
const TABS_KEY = 'industrial-platform.pc.tabs.v1:t1:u1'

function sandboxCandidate(slot: number): WorkspaceRouteCandidate {
  return {
    id: `sandbox:${slot}`,
    title: `沙箱 ${slot}`,
    kind: 'business',
    route: { name: 'workspace-tabs-sandbox', params: {}, query: { slot: String(slot) } },
  }
}

function fixedCandidate(): WorkspaceRouteCandidate {
  return {
    id: 'pc-home',
    title: '工作台',
    kind: 'fixed',
    route: { name: 'pc-home', params: {}, query: {} },
  }
}

function openMany(slotCount: number): void {
  const store = useWorkspaceTabsStore()
  for (let i = 0; i < slotCount; i += 1) store.requestOpen(sandboxCandidate(i))
}

/** 候选 + reloadVersion → 可持久化标签。 */
function businessTab(slot: number, reloadVersion = 1): WorkspaceTab {
  return { ...sandboxCandidate(slot), kind: 'business' as const, reloadVersion }
}

describe('workspaceTabsStore — bindUser', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    localStorage.clear()
  })

  it('首次绑定保证固定工作台并持久化', () => {
    const store = useWorkspaceTabsStore()
    store.bindUser(SCOPE)
    expect(store.ready).toBe(true)
    expect(store.scope).toEqual(SCOPE)
    expect(store.tabs).toHaveLength(1)
    expect(store.tabs[0]).toEqual(createFixedWorkbench())
    expect(store.activeTabId).toBe('pc-home')
    expect(localStorage.getItem(TABS_KEY)).not.toBeNull()
  })

  it('同一 scope 幂等:不重置已打开的业务标签', () => {
    const store = useWorkspaceTabsStore()
    store.bindUser(SCOPE)
    store.requestOpen(sandboxCandidate(0))
    expect(store.tabs).toHaveLength(2)
    store.bindUser(SCOPE)
    expect(store.tabs).toHaveLength(2)
  })

  it('从已有快照恢复标签与活跃项', () => {
    writeTabsSnapshot(localStorage, SCOPE, {
      version: 1,
      tabs: [createFixedWorkbench(), businessTab(3, 4)],
      activeTabId: 'sandbox:3',
      updatedAt: '2026-08-12T00:00:00.000Z',
    })
    const store = useWorkspaceTabsStore()
    store.bindUser(SCOPE)
    expect(store.tabs.map((t) => t.id)).toEqual(['pc-home', 'sandbox:3'])
    expect(store.activeTabId).toBe('sandbox:3')
    expect(store.activeTab?.reloadVersion).toBe(4)
  })

  it('恢复时业务标签封顶 12', () => {
    const businesses = Array.from({ length: 14 }, (_, i) => businessTab(i))
    writeTabsSnapshot(localStorage, SCOPE, {
      version: 1,
      tabs: [createFixedWorkbench(), ...businesses],
      activeTabId: 'sandbox:13',
      updatedAt: '2026-08-12T00:00:00.000Z',
    })
    const store = useWorkspaceTabsStore()
    store.bindUser(SCOPE)
    expect(store.businessTabs).toHaveLength(MAX_BUSINESS_TABS)
  })
})

describe('workspaceTabsStore — requestOpen', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    localStorage.clear()
  })

  it('固定候选激活固定工作台且不新增', () => {
    const store = useWorkspaceTabsStore()
    store.bindUser(SCOPE)
    store.requestOpen(sandboxCandidate(0))
    const result = store.requestOpen(fixedCandidate())
    expect(result.kind).toBe('activated')
    expect(store.tabs).toHaveLength(2)
    expect(store.tabs[0]?.kind).toBe('fixed')
    expect(store.activeTabId).toBe('pc-home')
  })

  it('打开业务标签并激活;同一身份重复打开仅复用', () => {
    const store = useWorkspaceTabsStore()
    store.bindUser(SCOPE)
    const first = store.requestOpen(sandboxCandidate(1))
    expect(first).toMatchObject({ kind: 'opened' })
    expect(store.businessTabs).toHaveLength(1)
    expect(store.activeTabId).toBe('sandbox:1')
    const second = store.requestOpen(sandboxCandidate(1))
    expect(second).toMatchObject({ kind: 'activated' })
    expect(store.businessTabs).toHaveLength(1)
  })

  it('第 13 个业务标签被阻断:不新增,保存 pending', () => {
    const store = useWorkspaceTabsStore()
    store.bindUser(SCOPE)
    openMany(MAX_BUSINESS_TABS)
    expect(store.businessTabs).toHaveLength(MAX_BUSINESS_TABS)
    const result = store.requestOpen(sandboxCandidate(12))
    expect(result.kind).toBe('limit-reached')
    expect(store.businessTabs).toHaveLength(MAX_BUSINESS_TABS)
    expect(store.pending).toEqual(sandboxCandidate(12).route)
    expect(store.activeTabId).toBe('sandbox:11')
  })
})

describe('workspaceTabsStore — closeTab', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    localStorage.clear()
  })

  it('关闭激活标签激活右邻、左邻或固定工作台', () => {
    const store = useWorkspaceTabsStore()
    store.bindUser(SCOPE)
    openMany(3)
    store.requestOpen(sandboxCandidate(1))
    // 激活右邻(从沙箱2 关闭中间 → 右邻沙箱3? 此处用 3 标签演示右邻)
    // 构造 tabs = [fixed, s0, s1, s2],激活 s1,关闭 s1 → 右邻 s2
    const next = store.closeTab('sandbox:1')
    expect(next.id).toBe('sandbox:2')
    expect(store.activeTabId).toBe('sandbox:2')
    // 关闭最右(无右邻) → 左邻
    const left = store.closeTab('sandbox:2')
    expect(left.id).toBe('sandbox:0')
    expect(store.activeTabId).toBe('sandbox:0')
    // 关闭最后一个业务 → 固定工作台
    const fixed = store.closeTab('sandbox:0')
    expect(fixed.kind).toBe('fixed')
    expect(store.activeTabId).toBe('pc-home')
  })

  it('关闭非激活标签不改变 activeTabId', () => {
    const store = useWorkspaceTabsStore()
    store.bindUser(SCOPE)
    openMany(3)
    store.activeTabId = 'sandbox:2'
    store.closeTab('sandbox:0')
    expect(store.tabs.map((t) => t.id)).toEqual(['pc-home', 'sandbox:1', 'sandbox:2'])
    expect(store.activeTabId).toBe('sandbox:2')
  })

  it('固定工作台不可关闭', () => {
    const store = useWorkspaceTabsStore()
    store.bindUser(SCOPE)
    const result = store.closeTab('pc-home')
    expect(result.kind).toBe('fixed')
    expect(store.tabs).toHaveLength(1)
  })
})

describe('workspaceTabsStore — closeOthers / closeRight', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    localStorage.clear()
  })

  it('closeOthers 保留固定与目标;激活被移除时改为目标', () => {
    const store = useWorkspaceTabsStore()
    store.bindUser(SCOPE)
    openMany(3)
    store.activeTabId = 'sandbox:2'
    store.closeOthers('sandbox:0')
    expect(store.tabs.map((t) => t.id)).toEqual(['pc-home', 'sandbox:0'])
    expect(store.activeTabId).toBe('sandbox:0')
  })

  it('closeOthers 激活为目标时保持不动', () => {
    const store = useWorkspaceTabsStore()
    store.bindUser(SCOPE)
    openMany(2)
    store.activeTabId = 'sandbox:1'
    store.closeOthers('sandbox:1')
    expect(store.tabs.map((t) => t.id)).toEqual(['pc-home', 'sandbox:1'])
    expect(store.activeTabId).toBe('sandbox:1')
  })

  it('closeRight 关闭右侧业务标签;激活被移除时改为目标', () => {
    const store = useWorkspaceTabsStore()
    store.bindUser(SCOPE)
    openMany(3)
    store.activeTabId = 'sandbox:2'
    store.closeRight('sandbox:0')
    expect(store.tabs.map((t) => t.id)).toEqual(['pc-home', 'sandbox:0'])
    expect(store.activeTabId).toBe('sandbox:0')
  })

  it('closeRight 激活在目标左侧/固定时不改动', () => {
    const store = useWorkspaceTabsStore()
    store.bindUser(SCOPE)
    openMany(3)
    store.activeTabId = 'pc-home'
    store.closeRight('sandbox:0')
    expect(store.tabs.map((t) => t.id)).toEqual(['pc-home', 'sandbox:0'])
    expect(store.activeTabId).toBe('pc-home')
  })
})

describe('workspaceTabsStore — reloadCurrent / resolvePending / prune', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    localStorage.clear()
  })

  it('reloadCurrent 仅递增激活业务标签', () => {
    const store = useWorkspaceTabsStore()
    store.bindUser(SCOPE)
    store.requestOpen(sandboxCandidate(0))
    const version = store.activeTab?.reloadVersion ?? 0
    store.reloadCurrent()
    expect(store.activeTab?.reloadVersion).toBe(version + 1)
    store.requestOpen(fixedCandidate())
    store.reloadCurrent() // 固定工作台不递增
    expect(store.tabs.find((t) => t.id === 'sandbox:0')?.reloadVersion).toBe(version + 1)
  })

  it('resolvePending cancel → null 且清空 pending', () => {
    const store = useWorkspaceTabsStore()
    store.bindUser(SCOPE)
    openMany(MAX_BUSINESS_TABS)
    store.requestOpen(sandboxCandidate(12))
    expect(store.pending).not.toBeNull()
    expect(store.resolvePending({ action: 'cancel' })).toBeNull()
    expect(store.pending).toBeNull()
    expect(store.businessTabs).toHaveLength(MAX_BUSINESS_TABS)
  })

  it('resolvePending reuse → 激活所选标签并返回其路由', () => {
    const store = useWorkspaceTabsStore()
    store.bindUser(SCOPE)
    openMany(MAX_BUSINESS_TABS)
    store.requestOpen(sandboxCandidate(12))
    const target = store.resolvePending({ action: 'reuse', tabId: 'sandbox:0' })
    expect(target).toEqual(sandboxCandidate(0).route)
    expect(store.activeTabId).toBe('sandbox:0')
    expect(store.pending).toBeNull()
  })

  it('resolvePending close-and-open → 关闭所选并返回 pending 路由', () => {
    const store = useWorkspaceTabsStore()
    store.bindUser(SCOPE)
    openMany(MAX_BUSINESS_TABS)
    const blocked = store.requestOpen(sandboxCandidate(12))
    const pendingRoute: PersistedRouteLocation = (blocked as { pending: PersistedRouteLocation })
      .pending
    const target = store.resolvePending({ action: 'close-and-open', tabId: 'sandbox:0' })
    expect(target).toEqual(pendingRoute)
    expect(store.businessTabs.map((t) => t.id)).not.toContain('sandbox:0')
    expect(store.businessTabs).toHaveLength(MAX_BUSINESS_TABS - 1)
  })

  it('prune 丢弃未授权业务标签并保留固定工作台;激活被移除时回退固定工作台', () => {
    const store = useWorkspaceTabsStore()
    store.bindUser(SCOPE)
    openMany(3)
    store.activeTabId = 'sandbox:2'
    store.prune((tab) => tab.id === 'sandbox:0')
    expect(store.tabs.map((t) => t.id)).toEqual(['pc-home', 'sandbox:0'])
    expect(store.activeTabId).toBe('pc-home')
  })
})
