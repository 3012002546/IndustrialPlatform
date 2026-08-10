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
})
