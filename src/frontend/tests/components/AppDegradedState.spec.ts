/**
 * AppDegradedState 组件测试(PF-01 §7.10):
 * 必须同时说明不可用与仍可继续的能力,并提供 retry 槽。
 */

import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'

import AppDegradedState from '@/components/base/AppDegradedState.vue'

describe('AppDegradedState', () => {
  it('同时渲染不可用与仍可用的能力清单', () => {
    const wrapper = mount(AppDegradedState, {
      props: {
        unavailable: ['设备实时监控', '远程下发'],
        available: ['历史报表', '本地查询'],
      },
    })
    const text = wrapper.text()
    expect(text).toContain('暂不可用')
    expect(text).toContain('设备实时监控')
    expect(text).toContain('远程下发')
    expect(text).toContain('仍可使用')
    expect(text).toContain('历史报表')
    expect(text).toContain('本地查询')
  })

  it('role=alert 表达需注意的降级状态', () => {
    const wrapper = mount(AppDegradedState, {
      props: { unavailable: ['a'], available: ['b'] },
    })
    expect(wrapper.attributes('role')).toBe('alert')
  })

  it('retry 槽渲染重试入口', () => {
    const wrapper = mount(AppDegradedState, {
      props: { unavailable: ['a'], available: ['b'] },
      slots: { retry: '<button type="button">重试</button>' },
    })
    expect(wrapper.get('.app-degraded-state__retry button').text()).toBe('重试')
  })
})
