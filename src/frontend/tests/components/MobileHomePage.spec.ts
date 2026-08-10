/**
 * Mobile 首页组件测试(FE-009,§17):
 * 欢迎信息、当前终端(Mobile)与认证模式、数据来源标识、业务空状态;
 * 不出现任务/消息/审批等可点击假入口,不伪造生产数值。
 */

import { mount, type VueWrapper } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, describe, expect, it } from 'vitest'

import { persistAuthSession } from '../fixtures/session'
import MobileHomePage from '@/pages/mobile/MobileHomePage.vue'
import { useAuthStore } from '@/stores/authStore'
import { useDeviceStore } from '@/stores/deviceStore'

async function mountHome(permissions: string[] = ['platform.mobile.view']): Promise<VueWrapper> {
  // 生效终端置为 mobile:真实 360×800 设备宽度 <768 属 Mobile 断点(§11.1),
  // 覆盖键显式保证(§11.2),与设备 Store 单元测试一致。
  localStorage.setItem('industrial-platform.terminal.override.v1', 'mobile')
  const pinia = createPinia()
  setActivePinia(pinia)
  persistAuthSession(permissions)
  await useAuthStore().restore()
  useDeviceStore().init()
  return mount(MobileHomePage, { global: { plugins: [pinia] } })
}

describe('MobileHomePage', () => {
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
    expect(wrapper.get('[data-testid="terminal"]').text()).toBe('Mobile')
    expect(wrapper.get('[data-testid="auth-mode"]').text()).toContain('Mock')
    expect(wrapper.get('[data-testid="data-source"]').text()).toContain('Mock')
  })

  it('展示业务空状态,不出现任何可点击的任务/消息/审批入口', async () => {
    const wrapper = await mountHome()
    expect(wrapper.text()).toContain('业务功能将在后续阶段接入')
    // 文案可提及暂缓能力,但不得渲染为可交互按钮/链接(§17:无虚假入口)
    for (const label of ['任务', '消息', '审批']) {
      const interactive = wrapper.findAll('button, a').filter((w) => w.text().includes(label))
      expect(interactive).toHaveLength(0)
    }
  })

  it('不伪造任何生产数值', async () => {
    const wrapper = await mountHome()
    expect(wrapper.text()).not.toMatch(/\d+(\.\d+)?\s*%/)
  })

  it('页面标题为「首页」', async () => {
    const wrapper = await mountHome()
    expect(wrapper.get('h1').text()).toBe('首页')
  })
})
