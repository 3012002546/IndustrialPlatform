import { mount } from '@vue/test-utils'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import MockModeBanner from '@/components/base/MockModeBanner.vue'

describe('MockModeBanner', () => {
  // Mock 横幅只在 VITE_AUTH_MODE=mock 下渲染(产品默认已是 http),必须显式声明。
  beforeEach(() => {
    vi.stubEnv('VITE_AUTH_MODE', 'mock')
  })

  afterEach(() => {
    vi.unstubAllEnvs()
  })

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
