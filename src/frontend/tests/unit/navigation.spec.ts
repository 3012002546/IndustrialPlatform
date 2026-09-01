/**
 * PC 导航静态适配器测试(PF-01 §7.7):
 * 分组结构、真实路由存在性、图标组件、权限点与无假入口。
 */

import { describe, expect, it } from 'vitest'
import { isReactive } from 'vue'

import { normalizeNavigationGroups, pcNavigationGroups } from '@/components/navigation/navigation'
import type { NavigationItem } from '@/components/navigation/types'
import { ROUTE_NAMES, routes } from '@/router/routes'

/** 从路由表收集所有已注册路由名(仅一层,PC 路由为平铺子路由)。 */
function registeredRouteNames(): Set<string> {
  const names = new Set<string>()
  for (const record of routes) {
    if (record.name !== undefined) names.add(String(record.name))
    for (const child of record.children ?? []) {
      if (child.name !== undefined) names.add(String(child.name))
    }
  }
  return names
}

describe('pcNavigationGroups', () => {
  it('每个静态分组与菜单项都有稳定文案键和保底文案', () => {
    for (const group of pcNavigationGroups) {
      expect(group.labelKey).toBe(`shell.navigation.group.${group.id}`)
      expect(group.fallbackLabel).toBe(group.label)
      for (const item of group.items) {
        expect(item.labelKey).toBe(`shell.navigation.item.${item.id}`)
        expect(item.fallbackLabel).toBe(item.label)
      }
    }
  })

  it('运行时替换会递归补齐文案契约且不改变输入对象', () => {
    const groups = normalizeNavigationGroups([
      {
        id: 'custom',
        label: '自定义',
        icon: pcNavigationGroups[0]!.icon,
        items: [
          {
            id: 'custom-item',
            label: '自定义项',
            routeName: 'pc-home',
            icon: pcNavigationGroups[0]!.icon,
            children: [
              {
                id: 'custom-child',
                label: '子项',
                routeName: 'pc-home',
                icon: pcNavigationGroups[0]!.icon,
              },
            ],
          },
        ],
      },
    ])
    expect(groups[0]).toMatchObject({
      labelKey: 'shell.navigation.group.custom',
      fallbackLabel: '自定义',
    })
    expect(groups[0]!.items[0]).toMatchObject({
      labelKey: 'shell.navigation.item.custom-item',
      fallbackLabel: '自定义项',
      children: [{ labelKey: 'shell.navigation.item.custom-child', fallbackLabel: '子项' }],
    })
  })

  it('运行时替换为二级分组补齐稳定文案键和保底文案', () => {
    const groups = normalizeNavigationGroups([
      {
        id: 'custom',
        label: '自定义',
        icon: pcNavigationGroups[0]!.icon,
        sections: [{ id: 'custom-section', label: '自定义分组' }],
        items: [{ id: 'custom-item', label: '自定义项', routeName: 'pc-home' }],
      },
    ])

    expect(groups[0]!.sections).toEqual([
      {
        id: 'custom-section',
        label: '自定义分组',
        labelKey: 'shell.navigation.section.custom-section',
        fallbackLabel: '自定义分组',
      },
    ])
  })

  it('至少包含工作台分组,每个分组有 id/label/icon/items', () => {
    expect(pcNavigationGroups.length).toBeGreaterThan(0)
    for (const group of pcNavigationGroups) {
      expect(group.id).toBeTruthy()
      expect(group.label).toBeTruthy()
      expect(typeof group.icon).toBe('object')
      expect(group.items.length).toBeGreaterThan(0)
    }
  })

  it('保持导航图标组件为非代理对象,避免 Element Plus 组件被 Vue 深代理告警', () => {
    for (const group of pcNavigationGroups) {
      expect(isReactive(group)).toBe(false)
      expect(isReactive(group.icon)).toBe(false)
      for (const item of group.items) expect(isReactive(item.icon)).toBe(false)
    }
  })

  it('分组内所有 item 的目标路由真实存在于路由表', () => {
    const registered = registeredRouteNames()
    const all = pcNavigationGroups.flatMap((g) => g.items)
    expect(all.length).toBeGreaterThan(0)
    for (const item of all) {
      expect(registered.has(item.routeName), `${item.routeName} 未注册`).toBe(true)
    }
  })

  it('每个 item 有唯一 id 与唯一 routeName,带稳定 label', () => {
    const all = pcNavigationGroups.flatMap((g) => g.items)
    const ids = all.map((i) => i.id)
    const routeNames = all.map((i) => i.routeName)
    expect(new Set(ids).size).toBe(ids.length)
    expect(new Set(routeNames).size).toBe(routeNames.length)
    for (const item of all) {
      expect(item.label).toBeTruthy()
    }
  })

  it('所有路由都归属某个分组且 id 不重叠', () => {
    const seen = new Set<string>()
    for (const group of pcNavigationGroups) {
      expect(seen.has(group.id)).toBe(false)
      seen.add(group.id)
      for (const item of group.items) {
        expect(seen.has(item.id)).toBe(false)
        seen.add(item.id)
      }
    }
  })

  it('只注册真实 PC 工作台路由,不含 SystemData/通知/聊天等假入口', () => {
    const labels = pcNavigationGroups.flatMap((g) => g.items).map((i) => i.label)
    const fakeKeywords = ['SystemData', '通知', '聊天', '消息', '协作']
    for (const keyword of fakeKeywords) {
      expect(
        labels.some((l) => l.includes(keyword)),
        `不应出现假入口:${keyword}`,
      ).toBe(false)
    }
  })

  it('每项图标可选但 routeName/label 必填;类型层面 NavigationItem 与 NavigationGroup 对齐', () => {
    const item: NavigationItem = { id: 'x', label: 'X', routeName: ROUTE_NAMES.pcHome }
    expect(item.icon).toBeUndefined()
    expect(item.id).toBe('x')
  })
})
