/**
 * PC 工作区标签栏测试(PF-01 §7.9/§10.1):
 * 固定工作台标签恒在首位且不可关闭、业务标签关闭/菜单按钮、aria-selected 跟随活跃项、
 * 点击/关闭/菜单命令 emit、语义 token 消费。
 */

import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, describe, expect, it } from 'vitest'
import { nextTick } from 'vue'

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

  it('固定工作台标签恒在首位,无关闭按钮', () => {
    const wrapper = mountWithTabs([0, 1])
    const nav = wrapper.get('nav.ip-pc-tabs')
    expect(nav.attributes('aria-label')).toBe('工作台标签')
    expect(nav.attributes('role')).toBe('tablist')
    const items = nav.findAll('.ip-pc-tabs__item')
    expect(items[0]?.text()).toContain('工作台')
    // 固定工作台项不含关闭/更多按钮
    expect(items[0]?.findAll('button')).toHaveLength(1)
    // 业务标签只含标签与关闭按钮;其他操作通过右键菜单
    expect(items[1]?.findAll('button')).toHaveLength(2)
  })

  it('不重复渲染菜单搜索;菜单搜索由二级面板与全局命令搜索承载', () => {
    const wrapper = mountWithTabs([])
    expect(wrapper.find('[aria-label="搜索菜单"]').exists()).toBe(false)
    expect(wrapper.find('.ip-pc-tabs__menu-search').exists()).toBe(false)
  })

  it('业务标签带关闭按钮,点击 emit close', async () => {
    const wrapper = mountWithTabs([0])
    const close = wrapper.get('.ip-pc-tabs__close')
    expect(close.attributes('aria-label')).toBe('关闭 沙箱 0')
    await close.trigger('click')
    expect(wrapper.emitted('close')).toEqual([['sandbox:0']])
  })

  it('固定业务标签不显示关闭按钮,右键可切换固定状态', async () => {
    const tabsStore = useWorkspaceTabsStore()
    tabsStore.bindUser({ tenantId: 't1', userId: 'u1' })
    tabsStore.requestOpen(sandboxCandidate(0))
    tabsStore.setTabPinned('sandbox:0', true)
    const wrapper = mount(PcWorkspaceTabs, { global: { plugins: [pinia] } })
    expect(wrapper.findAll('.ip-pc-tabs__item')[1]?.find('.ip-pc-tabs__close').exists()).toBe(false)
    await wrapper.findAll('.ip-pc-tabs__item')[1]!.trigger('contextmenu')
    expect(wrapper.get('[data-testid="workspace-tab-menu-toggle-pin"]').text()).toContain('取消固定')
    await wrapper.get('[data-testid="workspace-tab-menu-toggle-pin"]').trigger('click')
    expect(wrapper.emitted('toggle-pin')).toEqual([['sandbox:0']])
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

  it('右键菜单命令 emit close / close-left / close-right / close-others / close-all / reload / focus', async () => {
    const wrapper = mountWithTabs([0])
    await wrapper.findAll('.ip-pc-tabs__item')[1]!.trigger('contextmenu')
    expect(wrapper.get('[data-testid="workspace-tab-context-menu"]').text()).toContain('当前页专注')
    await wrapper.get('[data-testid="workspace-tab-menu-close-others"]').trigger('click')
    expect(wrapper.emitted('close-others')).toEqual([['sandbox:0']])
    await wrapper.findAll('.ip-pc-tabs__item')[1]!.trigger('contextmenu')
    await wrapper.get('[data-testid="workspace-tab-menu-close-left"]').trigger('click')
    expect(wrapper.emitted('close-left')).toEqual([['sandbox:0']])
    await wrapper.findAll('.ip-pc-tabs__item')[1]!.trigger('contextmenu')
    await wrapper.get('[data-testid="workspace-tab-menu-close-right"]').trigger('click')
    expect(wrapper.emitted('close-right')).toEqual([['sandbox:0']])
    await wrapper.findAll('.ip-pc-tabs__item')[1]!.trigger('contextmenu')
    await wrapper.get('[data-testid="workspace-tab-menu-close-all"]').trigger('click')
    expect(wrapper.emitted('close-all')).toEqual([[]])
    await wrapper.findAll('.ip-pc-tabs__item')[1]!.trigger('contextmenu')
    await wrapper.get('[data-testid="workspace-tab-menu-reload"]').trigger('click')
    expect(wrapper.emitted('reload')).toEqual([['sandbox:0']])
    await wrapper.findAll('.ip-pc-tabs__item')[1]!.trigger('contextmenu')
    await wrapper.get('[data-testid="workspace-tab-menu-focus"]').trigger('click')
    expect(wrapper.emitted('focus')).toEqual([['sandbox:0']])
  })

  it('右键菜单位置限制在视口内并支持 Escape 关闭', async () => {
    const wrapper = mountWithTabs([0])
    await wrapper.findAll('.ip-pc-tabs__item')[1]!.trigger('contextmenu', {
      clientX: 100000,
      clientY: 100000,
    })
    await wrapper.vm.$nextTick()
    const menu = wrapper.get('[data-testid="workspace-tab-context-menu"]')
    expect(Number.parseInt(menu.attributes('style')?.match(/left: ([^;]+)/)?.[1] ?? '0')).toBeLessThan(
      window.innerWidth,
    )
    expect(Number.parseInt(menu.attributes('style')?.match(/top: ([^;]+)/)?.[1] ?? '0')).toBeLessThan(
      window.innerHeight,
    )
    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }))
    await nextTick()
    expect(wrapper.find('[data-testid="workspace-tab-context-menu"]').exists()).toBe(false)
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
