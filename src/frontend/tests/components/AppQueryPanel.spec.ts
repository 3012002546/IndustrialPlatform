/**
 * AppQueryPanel 组件测试(PF-01 §7.10):
 * 标题、default/actions 槽、折叠开关与 aria 状态。
 */

import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'

import AppQueryPanel from '@/components/management/AppQueryPanel.vue'
import appQueryPanelSource from '@/components/management/AppQueryPanel.vue?raw'

describe('AppQueryPanel', () => {
  it('渲染标题与默认槽内容', () => {
    const wrapper = mount(AppQueryPanel, {
      props: { title: '查询条件' },
      slots: { default: '<input aria-label="名称" />' },
    })
    expect(wrapper.get('h2').text()).toBe('查询条件')
    expect(wrapper.find('input[aria-label="名称"]').exists()).toBe(true)
  })

  it('渲染 actions 槽;无 title/collapsible/actions 时不渲染头部', () => {
    const wrapper = mount(AppQueryPanel, {
      props: {},
      slots: { actions: '<button type="button">重置</button>' },
    })
    expect(wrapper.get('button').text()).toBe('重置')
    const bare = mount(AppQueryPanel, { props: {}, slots: { default: '<p>x</p>' } })
    expect(bare.find('.app-query-panel__header').exists()).toBe(false)
  })

  it('非 collapsible 时不渲染折叠按钮', () => {
    const wrapper = mount(AppQueryPanel, { props: { title: '查询' } })
    expect(wrapper.find('[data-testid="query-panel-toggle"]').exists()).toBe(false)
  })

  it('collapsible 时点击发出 update:collapsed 新值;受控 collapsed 由父级更新', async () => {
    const wrapper = mount(AppQueryPanel, { props: { title: '查询', collapsible: true } })
    const toggle = wrapper.get('[data-testid="query-panel-toggle"]')
    expect(toggle.attributes('aria-expanded')).toBe('true')
    await toggle.trigger('click')
    expect(wrapper.emitted('update:collapsed')).toEqual([[true]])
    // 父级收到事件后回写 collapsed,aria-expanded 反映受控状态
    await wrapper.setProps({ collapsed: true })
    expect(toggle.attributes('aria-expanded')).toBe('false')
  })

  it('collapsed 时隐藏内容区并关联 aria-controls', () => {
    const wrapper = mount(AppQueryPanel, {
      props: { title: '查询', collapsible: true, collapsed: true },
      slots: { default: '<p>内容</p>' },
    })
    const toggle = wrapper.get('[data-testid="query-panel-toggle"]')
    expect(toggle.attributes('aria-expanded')).toBe('false')
    const bodyId = toggle.attributes('aria-controls')
    const body = wrapper.get('.app-query-panel__body')
    expect(body.attributes('id')).toBe(bodyId)
    expect(body.isVisible()).toBe(false)
  })

  it('提供标准提交、重置动作并向外输出 QueryDescriptor', async () => {
    const descriptor = {
      filters: [],
      orderBy: [],
      select: ['userName'],
      pageIndex: 1,
      pageSize: 20,
    }
    const wrapper = mount(AppQueryPanel, {
      props: { title: '查询条件', showActions: true, descriptor },
      slots: { default: '<input aria-label="用户名" />' },
    })

    await wrapper.get('[data-testid="query-panel-submit"]').trigger('click')
    await wrapper.get('[data-testid="query-panel-reset"]').trigger('click')
    expect(wrapper.emitted('submit')).toEqual([[descriptor]])
    expect(wrapper.emitted('reset')).toEqual([[]])
  })

  it('网格查询把动作放在字段同一 surface 内并支持自定义更多条件文案', async () => {
    const wrapper = mount(AppQueryPanel, {
      props: {
        grid: true,
        showActions: true,
      },
      slots: {
        default: '<label class="query-field">字段</label>',
        'body-actions': '<button type="button" data-testid="more-conditions">更多条件</button>',
      },
    })

    expect(wrapper.get('.app-query-panel__body').find('[data-testid="query-panel-submit"]').exists()).toBe(true)
    expect(wrapper.get('.app-query-panel__body').find('[data-testid="query-panel-reset"]').exists()).toBe(true)
    expect(wrapper.find('.app-query-panel__header').exists()).toBe(false)
    expect(wrapper.get('.app-query-panel__body').get('[data-testid="more-conditions"]').text()).toBe('更多条件')
  })

  it('网格查询沿用紧凑横向字段流,不把字段继承成纵向查询列', () => {
    expect(appQueryPanelSource).toMatch(
      /\.app-query-panel__body--grid\s*\{[\s\S]*?flex-direction:\s*row;/,
    )
  })

  it('查询操作使用与字段一致的紧凑字号', () => {
    expect(appQueryPanelSource).toMatch(
      /\.app-query-panel__actions button\s*\{[\s\S]*?font-size:\s*var\(--ip-font-size-sm\);/,
    )
  })

})
