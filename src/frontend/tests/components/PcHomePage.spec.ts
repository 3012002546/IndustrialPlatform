/**
 * PC 首页组件测试(FE-007,§15.2):
 * 欢迎信息、当前终端与认证模式、数据来源标识、空状态;禁止伪造生产指标。
 */

import { mount, type VueWrapper } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, describe, expect, it } from 'vitest'

import { persistAuthSession } from '../fixtures/session'
import PcHomePage from '@/pages/pc/PcHomePage.vue'
import { useAuthStore } from '@/stores/authStore'

async function mountHome(permissions: string[] = ['platform.home.view']): Promise<VueWrapper> {
  const pinia = createPinia()
  setActivePinia(pinia)
  persistAuthSession(permissions)
  await useAuthStore().restore()
  return mount(PcHomePage, { global: { plugins: [pinia] } })
}

describe('PcHomePage', () => {
  beforeEach(() => {
    sessionStorage.clear()
    localStorage.clear()
  })

  it('展示当前用户欢迎信息', async () => {
    const wrapper = await mountHome()
    expect(wrapper.get('[data-testid="welcome"]').text()).toContain('Mock 演示账号')
  })

  it('展示当前终端、认证模式与数据来源标识', async () => {
    const wrapper = await mountHome()
    expect(wrapper.get('[data-testid="terminal"]').text()).toBe('PC')
    expect(wrapper.get('[data-testid="auth-mode"]').text()).toContain('Mock')
    expect(wrapper.get('[data-testid="data-source"]').text()).toContain('Mock')
  })

  it('展示业务指标空状态,且不出现任何伪造的生产指标数值', async () => {
    const wrapper = await mountHome()
    expect(wrapper.text()).toContain('业务指标将在后续阶段接入')
    // 空状态免责文案会提及指标名称,但不得伪造任何具体数值(如百分比/产量数字)
    expect(wrapper.text()).not.toMatch(/\d+(\.\d+)?\s*%/)
  })

  it('页面标题为「首页」', async () => {
    const wrapper = await mountHome()
    expect(wrapper.get('h1').text()).toBe('首页')
  })
})
