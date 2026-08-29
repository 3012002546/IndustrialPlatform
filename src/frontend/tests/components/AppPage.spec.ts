import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'

import AppPage from '@/components/base/AppPage.vue'

describe('AppPage', () => {
  it('renders the title and description when provided', () => {
    const wrapper = mount(AppPage, {
      props: { title: '首页', description: '页面说明' },
    })
    expect(wrapper.find('h1').text()).toBe('首页')
    expect(wrapper.find('.app-page__description').text()).toBe('页面说明')
  })

  it('does not render the header when title is absent', () => {
    const wrapper = mount(AppPage)
    expect(wrapper.find('h1').exists()).toBe(false)
    expect(wrapper.find('.app-page__header').exists()).toBe(false)
  })

  it('renders slot content', () => {
    const wrapper = mount(AppPage, {
      props: { title: '首页' },
      slots: { default: '<p class="slot-content">内容</p>' },
    })
    expect(wrapper.find('.slot-content').text()).toBe('内容')
  })

  it('supports header extension slots without replacing the shared page header', () => {
    const wrapper = mount(AppPage, {
      props: { title: '用户管理', description: '管理用户' },
      slots: {
        breadcrumb: '<nav data-testid="page-breadcrumb">首页 / 用户</nav>',
        meta: '<span data-testid="page-meta">共 3 人</span>',
        actions: '<button data-testid="page-action">新建</button>',
      },
    })

    expect(wrapper.get('h1').text()).toBe('用户管理')
    expect(wrapper.get('[data-testid="page-breadcrumb"]').text()).toContain('首页')
    expect(wrapper.get('[data-testid="page-meta"]').text()).toContain('3')
    expect(wrapper.get('[data-testid="page-action"]').text()).toBe('新建')
  })
})
