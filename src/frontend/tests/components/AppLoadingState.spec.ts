/**
 * AppLoadingState 组件测试(PF-01 §7.10):role=status 与可读加载文案。
 */

import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'

import AppLoadingState from '@/components/base/AppLoadingState.vue'

describe('AppLoadingState', () => {
  it('默认文案为「加载中…」并带 role=status', () => {
    const wrapper = mount(AppLoadingState)
    expect(wrapper.attributes('role')).toBe('status')
    expect(wrapper.get('p').text()).toBe('加载中…')
  })

  it('自定义 label', () => {
    const wrapper = mount(AppLoadingState, { props: { label: '正在同步设备…' } })
    expect(wrapper.get('p').text()).toBe('正在同步设备…')
  })
})
