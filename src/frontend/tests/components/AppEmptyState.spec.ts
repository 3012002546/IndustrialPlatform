import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'

import AppEmptyState from '@/components/base/AppEmptyState.vue'

describe('AppEmptyState', () => {
  it('renders title and description', () => {
    const wrapper = mount(AppEmptyState, {
      props: { title: '暂无数据', description: '业务指标将在后续阶段接入' },
    })
    expect(wrapper.get('h2').text()).toBe('暂无数据')
    expect(wrapper.text()).toContain('业务指标将在后续阶段接入')
  })

  it('exposes role=status for assistive technology', () => {
    const wrapper = mount(AppEmptyState, { props: { title: '空' } })
    expect(wrapper.attributes('role')).toBe('status')
  })

  it('renders action slot content when provided', () => {
    const wrapper = mount(AppEmptyState, {
      props: { title: '空' },
      slots: { default: '<button type="button">去创建</button>' },
    })
    expect(wrapper.find('button').text()).toBe('去创建')
  })
})
