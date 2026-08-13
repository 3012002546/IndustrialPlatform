/**
 * 稳定页面身份与可持久化路由测试(PF-01 §7.9):
 * serializeLocationParts 排序/编码、buildTabId 确定性、toPersistedRoute 空值过滤、
 * 固定工作台工厂与配额常量。
 */

import { describe, expect, it } from 'vitest'

import {
  buildTabId,
  createFixedWorkbench,
  FIXED_WORKBENCH_ID,
  FIXED_WORKBENCH_TITLE,
  MAX_BUSINESS_TABS,
  serializeLocationParts,
  toPersistedRoute,
} from '@/workspace/identity'

describe('workspace identity — serializeLocationParts', () => {
  it('键排序后连接,值经 encodeURIComponent', () => {
    expect(serializeLocationParts({ b: '2', a: '1' })).toBe('a=1&b=2')
    expect(serializeLocationParts({ tag: 'x y' })).toBe('tag=x%20y')
  })

  it('数组值逐项展开为重复键', () => {
    expect(serializeLocationParts({ tag: ['x', 'y'] })).toBe('tag=x&tag=y')
  })

  it('空对象返回空串', () => {
    expect(serializeLocationParts({})).toBe('')
  })
})

describe('workspace identity — buildTabId', () => {
  it('无参数时退化为路由名', () => {
    expect(buildTabId('identity-users')).toBe('identity-users')
    expect(buildTabId('identity-users', {}, {})).toBe('identity-users')
  })

  it('带 params/query 时拼接排序后的 p/q 段', () => {
    expect(buildTabId('sandbox', { slot: '5' }, { mode: 'x' })).toBe('sandbox&p=slot=5&q=mode=x')
  })

  it('参数顺序不影响结果(确定性身份)', () => {
    expect(buildTabId('a', { b: '1', a: '2' }, {})).toBe(buildTabId('a', { a: '2', b: '1' }, {}))
  })
})

describe('workspace identity — toPersistedRoute', () => {
  it('过滤 null/undefined 值并归一化为 string', () => {
    const route = toPersistedRoute({
      name: 'sandbox',
      params: { id: 7, drop: null },
      query: { slot: '3', empty: undefined, tags: ['a', 'b'] },
    })
    expect(route).toEqual({
      name: 'sandbox',
      params: { id: '7' },
      query: { slot: '3', tags: ['a', 'b'] },
    })
  })

  it('数组参数保留为数组', () => {
    const route = toPersistedRoute({
      name: 'a',
      params: { ids: ['1', '2'] },
      query: {},
    })
    expect(route.params.ids).toEqual(['1', '2'])
  })
})

describe('workspace identity — 固定工作台与常量', () => {
  it('配额常量为 12', () => {
    expect(MAX_BUSINESS_TABS).toBe(12)
  })

  it('固定工作台工厂:id/title/kind/route/reloadVersion', () => {
    const workbench = createFixedWorkbench()
    expect(workbench).toEqual({
      id: FIXED_WORKBENCH_ID,
      title: FIXED_WORKBENCH_TITLE,
      kind: 'fixed',
      route: { name: FIXED_WORKBENCH_ID, params: {}, query: {} },
      reloadVersion: 0,
    })
  })

  it('固定工作台 id 与 pc-home 路由名一致', () => {
    expect(FIXED_WORKBENCH_ID).toBe('pc-home')
  })
})
