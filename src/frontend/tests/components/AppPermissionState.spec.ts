/**
 * AppPermissionState 组件测试(PF-01 §7.10):
 * 无权限语义、默认/自定义文案与默认槽。
 */

import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'

import AppPermissionState from '@/components/base/AppPermissionState.vue'

describe('AppPermissionState', () => {
  it('默认文案表达无权限语义,不渲染为空数据', () => {
    const wrapper = mount(AppPermissionState)
    expect(wrapper.get('h2').text()).toBe('暂无访问权限')
    expect(wrapper.text()).toContain('你暂无查看此页面的权限')
  })

  it('自定义 title/description', () => {
    const wrapper = mount(AppPermissionState, {
      props: { title: '需管理员授权', description: '请向系统管理员申请该操作权限。' },
    })
    expect(wrapper.get('h2').text()).toBe('需管理员授权')
    expect(wrapper.text()).toContain('请向系统管理员申请该操作权限')
  })

  it('默认槽渲染动作入口', () => {
    const wrapper = mount(AppPermissionState, {
      slots: { default: '<button type="button">联系管理员</button>' },
    })
    expect(wrapper.get('button').text()).toBe('联系管理员')
  })
})
