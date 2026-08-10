import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'

import AppErrorAlert from '@/components/base/AppErrorAlert.vue'

describe('AppErrorAlert', () => {
  it('renders title and message', () => {
    const wrapper = mount(AppErrorAlert, {
      props: { title: '请求失败', message: '网络不可用' },
    })
    expect(wrapper.get('h2').text()).toBe('请求失败')
    expect(wrapper.text()).toContain('网络不可用')
  })

  it('exposes role=alert', () => {
    const wrapper = mount(AppErrorAlert, { props: { title: '错误' } })
    expect(wrapper.attributes('role')).toBe('alert')
  })

  it('shows traceId when provided', () => {
    const wrapper = mount(AppErrorAlert, {
      props: { title: '服务错误', traceId: 'abc-123' },
    })
    expect(wrapper.text()).toContain('TraceId: abc-123')
  })

  it('renders action slot content when provided', () => {
    const wrapper = mount(AppErrorAlert, {
      props: { title: '错误' },
      slots: { default: '<button type="button">重试</button>' },
    })
    expect(wrapper.find('button').text()).toBe('重试')
  })
})
