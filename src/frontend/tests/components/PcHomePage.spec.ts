/**
 * PC 首页组件测试(FE-007,§15.2):
 * 欢迎信息、权限感知快捷入口、运行环境与真实审计空状态;禁止伪造生产指标。
 */

import { mount, type VueWrapper } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import { persistAuthSession } from '../fixtures/session'
import PcHomePage from '@/pages/pc/PcHomePage.vue'
import { PERMISSIONS } from '@/permissions'
import { useAuthStore } from '@/stores/authStore'

async function mountHome(permissions: string[] = ['platform.home.view']): Promise<VueWrapper> {
  const pinia = createPinia()
  setActivePinia(pinia)
  persistAuthSession(permissions)
  await useAuthStore().restore()
  return mount(PcHomePage, {
    global: {
      plugins: [pinia],
      stubs: { RouterLink: { template: '<a><slot /></a>' } },
      directives: { loading: {} },
    },
  })
}

describe('PcHomePage', () => {
  beforeEach(() => {
    sessionStorage.clear()
    localStorage.clear()
    // 首页展示 Mock 演示数据标识,显式声明 mock,不依赖产品默认(现为 http)。
    vi.stubEnv('VITE_AUTH_MODE', 'mock')
  })

  afterEach(() => {
    vi.useRealTimers()
    vi.unstubAllEnvs()
  })

  it.each([
    [2, '凌晨好', 'overnight'],
    [6, '清晨好', 'dawn'],
    [9, '上午好', 'morning'],
    [13, '中午好', 'midday'],
    [16, '下午好', 'afternoon'],
    [20, '晚上好', 'evening'],
    [23, '夜深了', 'late-night'],
  ])('在 %i 点展示对应问候与时间主题', async (hour, greeting, period) => {
    vi.useFakeTimers()
    vi.setSystemTime(new Date(2026, 7, 19, hour, 13, 3))

    const wrapper = await mountHome()

    expect(wrapper.get('[data-testid="time-greeting"]').text()).toContain(greeting)
    expect(wrapper.get('[data-testid="time-header"]').classes()).toContain(
      `pc-home__page-header--${period}`,
    )
    wrapper.unmount()
  })

  it('七个时间段使用各自的单色主题环境光', async () => {
    vi.useFakeTimers()
    const palettes = new Set<string>()

    for (const hour of [2, 6, 9, 13, 16, 20, 23]) {
      vi.setSystemTime(new Date(2026, 7, 19, hour, 13, 3))
      const wrapper = await mountHome()
      const style = wrapper.get('[data-testid="time-header"]').attributes('style') ?? ''

      expect(style).toContain('--pc-home-period-accent:')
      expect(style).toContain('--pc-home-period-strength:')
      expect(style).not.toContain('--pc-home-period-end:')
      palettes.add(style)
      wrapper.unmount()
    }

    expect(palettes.size).toBe(7)
  })

  it('每秒更新首页时钟', async () => {
    vi.useFakeTimers()
    vi.setSystemTime(new Date(2026, 7, 19, 16, 13, 3))
    const wrapper = await mountHome()

    expect(wrapper.get('[data-testid="live-clock"]').text()).toBe('16:13:03')
    await vi.advanceTimersByTimeAsync(1000)
    expect(wrapper.get('[data-testid="live-clock"]').text()).toBe('16:13:04')
    wrapper.unmount()
  })

  it('展示当前用户欢迎信息', async () => {
    const wrapper = await mountHome()
    expect(wrapper.get('[data-testid="time-greeting"]').text()).toContain('Mock 演示账号')
    expect(wrapper.get('[data-testid="refresh-home"]').text()).toContain('刷新')
  })

  it('展示当前终端、认证模式与数据来源标识', async () => {
    const wrapper = await mountHome()
    expect(wrapper.get('[data-testid="terminal"]').text()).toBe('PC')
    expect(wrapper.get('[data-testid="auth-mode"]').text()).toContain('Mock')
    expect(wrapper.get('[data-testid="data-source"]').text()).toContain('演示数据')
  })

  it('无管理权限时不展示伪入口或伪造生产指标', async () => {
    const wrapper = await mountHome()
    expect(wrapper.text()).toContain('当前账号暂无其他管理入口')
    expect(wrapper.text()).toContain('暂无可展示的登录审计记录')
    expect(wrapper.text()).not.toMatch(/\d+(\.\d+)?\s*%/)
  })

  it('仅展示当前用户有权访问的快捷入口', async () => {
    const wrapper = await mountHome([PERMISSIONS.platformHomeView, PERMISSIONS.userView])
    expect(wrapper.text()).toContain('用户管理')
    expect(wrapper.text()).not.toContain('用户组管理')
  })

  it('页面主标题展示当前时段问候', async () => {
    const wrapper = await mountHome()
    expect(wrapper.get('h1').text()).toMatch(/凌晨好|清晨好|上午好|中午好|下午好|晚上好|夜深了/)
  })
})
