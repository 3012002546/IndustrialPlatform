/**
 * PC 工作区标签栏测试(PF-01 §7.9/§10.1):
 * 固定工作台标签恒在首位且不可关闭、业务标签关闭/菜单按钮、aria-selected 跟随活跃项、
 * 点击/关闭/菜单命令 emit、语义 token 消费。
 */

import { mount } from '@vue/test-utils'
import { ElDropdown } from 'element-plus'
import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, describe, expect, it } from 'vitest'

import PcWorkspaceTabs from '@/components/shell/PcWorkspaceTabs.vue'
import { useWorkspaceTabsStore } from '@/stores/workspaceTabsStore'
import type { WorkspaceRouteCandidate } from '@/workspace'

function sandboxCandidate(slot: number): WorkspaceRouteCandidate {
  return {
    id: `sandbox:${slot}`,
    title: `沙箱 ${slot}`,
    kind: 'business',
    route: { name: 'workspace-tabs-sandbox', params: {}, query: { slot: String(slot) } },
  }
}

describe('PcWorkspaceTabs', () => {
  let pinia: ReturnType<typeof createPinia>

  beforeEach(() => {
    pinia = createPinia()
    setActivePinia(pinia)
    localStorage.clear()
  })

  function mountWithTabs(slots: number[]): ReturnType<typeof mount> {
    const tabsStore = useWorkspaceTabsStore()
    tabsStore.bindUser({ tenantId: 't1', userId: 'u1' })
    for (const slot of slots) tabsStore.requestOpen(sandboxCandidate(slot))
    return mount(PcWorkspaceTabs, { global: { plugins: [pinia] } })
  }

  it('固定工作台标签恒在首位,无关闭/菜单按钮', () => {
    const wrapper = mountWithTabs([0, 1])
    const nav = wrapper.get('nav.ip-pc-tabs')
    expect(nav.attributes('aria-label')).toBe('工作台标签')
    expect(nav.attributes('role')).toBe('tablist')
    const items = nav.findAll('.ip-pc-tabs__item')
    expect(items[0]?.text()).toContain('工作台')
    // 固定工作台项不含关闭/更多按钮
    expect(items[0]?.findAll('button')).toHaveLength(1)
    // 两个业务标签各含关闭 + 更多
    expect(items[1]?.findAll('button')).toHaveLength(3)
  })

  it('固定工作台前提供加宽且权限感知的菜单搜索', () => {
    const wrapper = mountWithTabs([])
    expect(wrapper.get('[aria-label="搜索菜单"]')).toBeTruthy()
    expect(wrapper.find('.ip-pc-tabs__menu-search').exists()).toBe(true)
    expect(wrapper.get('.ip-pc-tabs').element.firstElementChild?.classList).toContain(
      'ip-pc-tabs__menu-search',
    )
  })

  it('业务标签带关闭按钮与更多菜单,点击 emit close', async () => {
    const wrapper = mountWithTabs([0])
    const close = wrapper.get('.ip-pc-tabs__close')
    expect(close.attributes('aria-label')).toBe('关闭 沙箱 0')
    await close.trigger('click')
    expect(wrapper.emitted('close')).toEqual([['sandbox:0']])
  })

  it('aria-selected 跟随活跃标签', () => {
    const wrapper = mountWithTabs([0, 1])
    const tabs = wrapper.findAll('[role="tab"]')
    // 最后打开的沙箱1 为活跃
    expect(tabs[2]?.attributes('aria-selected')).toBe('true')
    expect(tabs[1]?.attributes('aria-selected')).toBe('false')
  })

  it('点击标签 emit activate', async () => {
    const wrapper = mountWithTabs([0, 1])
    const tabs = wrapper.findAll('[role="tab"]')
    await tabs[1]!.trigger('click')
    expect(wrapper.emitted('activate')).toEqual([['sandbox:0']])
  })

  it('更多菜单命令 emit close-others / close-right / reload', async () => {
    const wrapper = mountWithTabs([0])
    const dropdown = wrapper.get('nav.ip-pc-tabs').findAllComponents(ElDropdown)[0]!
    await dropdown.vm.$emit('command', 'close-others')
    expect(wrapper.emitted('close-others')).toEqual([['sandbox:0']])
    await dropdown.vm.$emit('command', 'close-right')
    expect(wrapper.emitted('close-right')).toEqual([['sandbox:0']])
    await dropdown.vm.$emit('command', 'reload')
    expect(wrapper.emitted('reload')).toEqual([['sandbox:0']])
  })

  it('消费标签栏语义 token(--ip-shell-tabs-height)', async () => {
    const loader = import.meta.glob('../../../src/components/shell/PcWorkspaceTabs.vue', {
      query: '?raw',
      import: 'default',
    })
    const source = (await loader['../../../src/components/shell/PcWorkspaceTabs.vue']!()) as string
    expect(source).toContain('--ip-shell-tabs-height')
  })
})
