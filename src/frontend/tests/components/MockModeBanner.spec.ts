import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'

import MockModeBanner from '@/components/base/MockModeBanner.vue'

describe('MockModeBanner', () => {
  it('shows the default mock mode label', () => {
    const wrapper = mount(MockModeBanner)
    expect(wrapper.text()).toContain('开发 Mock 模式')
    expect(wrapper.text()).toContain('仅本地开发演示账号')
  })

  it('uses a custom label when provided', () => {
    const wrapper = mount(MockModeBanner, { props: { label: '测试模式' } })
    expect(wrapper.text()).toContain('测试模式')
  })

  it('exposes role=status', () => {
    const wrapper = mount(MockModeBanner)
    expect(wrapper.attributes('role')).toBe('status')
  })
})
