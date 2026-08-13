/**
 * PC 标签快照持久化测试(PF-01 §8.2):
 * 用户键、严格结构校验(非法 JSON/版本/字段→null)、单标签丢弃、
 * 业务封顶 12、固定工作台至多一个置前、activeTabId 回退、
 * 读写安全回退(存储抛错不冒泡)。
 */

import { describe, expect, it } from 'vitest'

import { createFixedWorkbench, MAX_BUSINESS_TABS } from '@/workspace/identity'
import {
  buildUserTabsKey,
  parseTabsSnapshot,
  readTabsSnapshot,
  serializeTabsSnapshot,
  writeTabsSnapshot,
  type WorkspaceStorage,
  type WorkspaceTabsSnapshot,
} from '@/workspace/persistence'
import type { UserUiScope } from '@/theme/types'
import type { WorkspaceTab } from '@/workspace/types'

const SCOPE: UserUiScope = { tenantId: 't1', userId: 'u1' }

function makeStorage(initial: Record<string, string> = {}): WorkspaceStorage {
  const map = new Map(Object.entries(initial))
  return {
    getItem: (key) => map.get(key) ?? null,
    setItem: (key, value) => {
      map.set(key, value)
    },
    removeItem: (key) => {
      map.delete(key)
    },
  }
}

function businessTab(slot: number, extra: Partial<WorkspaceTab> = {}): WorkspaceTab {
  return {
    id: `sandbox:${slot}`,
    title: `沙箱 ${slot}`,
    kind: 'business',
    route: { name: 'workspace-tabs-sandbox', params: {}, query: { slot: String(slot) } },
    reloadVersion: 1,
    ...extra,
  }
}

function snapshot(overrides: Partial<WorkspaceTabsSnapshot> = {}): WorkspaceTabsSnapshot {
  return {
    version: 1,
    tabs: [createFixedWorkbench()],
    activeTabId: 'pc-home',
    updatedAt: '2026-08-12T00:00:00.000Z',
    ...overrides,
  }
}

describe('workspace persistence — 用户键', () => {
  it('键前缀 + 编码后的租户/用户', () => {
    expect(buildUserTabsKey(SCOPE)).toBe('industrial-platform.pc.tabs.v1:t1:u1')
    expect(buildUserTabsKey({ tenantId: 'a/b', userId: 'c d' })).toBe(
      'industrial-platform.pc.tabs.v1:a%2Fb:c%20d',
    )
  })
})

describe('workspace persistence — parseTabsSnapshot', () => {
  it('null 与非法 JSON → null', () => {
    expect(parseTabsSnapshot(null)).toBeNull()
    expect(parseTabsSnapshot('not json')).toBeNull()
    expect(parseTabsSnapshot('{"version":1')).toBeNull()
  })

  it('未知版本 / 缺字段 / tabs 非数组 → null', () => {
    expect(parseTabsSnapshot('{"version":2,"tabs":[],"activeTabId":"","updatedAt":"x"}')).toBeNull()
    expect(parseTabsSnapshot('{"version":1}')).toBeNull()
    expect(
      parseTabsSnapshot('{"version":1,"tabs":"x","activeTabId":"","updatedAt":"x"}'),
    ).toBeNull()
  })

  it('单个非法标签丢弃,合法标签保留', () => {
    const tabs = [
      createFixedWorkbench(),
      businessTab(0),
      { bad: true },
      businessTab(1),
    ] as WorkspaceTab[]
    const parsed = parseTabsSnapshot(JSON.stringify(snapshot({ tabs })))
    expect(parsed?.tabs).toHaveLength(3) // fixed + 2 合法业务
    expect(parsed?.tabs.map((t) => t.id)).toEqual(['pc-home', 'sandbox:0', 'sandbox:1'])
  })

  it('业务标签封顶 12,固定工作台至多一个并置前', () => {
    const tabs: WorkspaceTab[] = [
      businessTab(0),
      { ...businessTab(100), kind: 'fixed', id: 'extra-fixed' },
      ...Array.from({ length: 13 }, (_, i) => businessTab(i + 1)),
      { ...businessTab(200), kind: 'fixed', id: 'another-fixed' },
    ]
    const parsed = parseTabsSnapshot(JSON.stringify(snapshot({ tabs })))
    expect(parsed?.tabs[0]?.kind).toBe('fixed')
    expect(parsed?.tabs[0]?.id).toBe('extra-fixed') // 首个固定保留
    const businesses = parsed?.tabs.filter((t) => t.kind === 'business') ?? []
    expect(businesses).toHaveLength(MAX_BUSINESS_TABS)
  })

  it('activeTabId 不合法时回退到首个标签', () => {
    const parsed = parseTabsSnapshot(
      JSON.stringify(
        snapshot({ tabs: [createFixedWorkbench(), businessTab(0)], activeTabId: 'nope' }),
      ),
    )
    expect(parsed?.activeTabId).toBe('pc-home')
  })

  it('reloadVersion 非整数时丢弃该标签', () => {
    const parsed = parseTabsSnapshot(
      JSON.stringify(
        snapshot({ tabs: [createFixedWorkbench(), businessTab(0, { reloadVersion: 1.5 })] }),
      ),
    )
    expect(parsed?.tabs.map((t) => t.id)).toEqual(['pc-home'])
  })
})

describe('workspace persistence — 读写安全回退', () => {
  it('readTabsSnapshot 读取并解析;存储 getItem 抛错 → null', () => {
    const storage = makeStorage({
      [buildUserTabsKey(SCOPE)]: JSON.stringify(snapshot()),
    })
    expect(readTabsSnapshot(storage, SCOPE)?.tabs[0]?.id).toBe('pc-home')
    const throwing: WorkspaceStorage = {
      getItem: () => {
        throw new Error('boom')
      },
      setItem: () => undefined,
      removeItem: () => undefined,
    }
    expect(readTabsSnapshot(throwing, SCOPE)).toBeNull()
  })

  it('writeTabsSnapshot 写入序列化快照;setItem 抛错 → false', () => {
    const storage = makeStorage()
    expect(writeTabsSnapshot(storage, SCOPE, snapshot())).toBe(true)
    expect(storage.getItem(buildUserTabsKey(SCOPE))).toBe(serializeTabsSnapshot(snapshot()))
    const throwing: WorkspaceStorage = {
      getItem: () => null,
      setItem: () => {
        throw new Error('quota')
      },
      removeItem: () => undefined,
    }
    expect(writeTabsSnapshot(throwing, SCOPE, snapshot())).toBe(false)
  })
})
