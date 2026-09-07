/**
 * Mobile 首页组件测试(FE-009,§17):
 * 欢迎信息、当前终端(Mobile)与认证模式、数据来源标识、业务空状态;
 * 不出现任务/消息/审批等可点击假入口,不伪造生产数值。
 */

import { flushPromises, mount, type VueWrapper } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { nextTick } from 'vue'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { createMemoryHistory, createRouter } from 'vue-router'

import { persistAuthSession } from '../fixtures/session'
import MobileHomePage from '@/pages/mobile/MobileHomePage.vue'
import { useLocalizationStore } from '@/stores/localizationStore'
import { PERMISSIONS } from '@/permissions'
import { routes } from '@/router/routes'
import { useAuthStore } from '@/stores/authStore'

async function mountHome(permissions: string[] = ['platform.mobile.view']): Promise<VueWrapper> {
  // 终端文案单事实源为路由 meta.terminal(/mobile/home = 'mobile',PF-01 §7.11),
  // 不再写入 override 键;即使设备建议缺省为 pc,显式路由仍解析为 Mobile。
  const pinia = createPinia()
  setActivePinia(pinia)
  persistAuthSession(permissions)
  await useAuthStore().restore()
  const router = createRouter({ history: createMemoryHistory(), routes })
  await router.push('/mobile/home')
  await router.isReady()
  return mount(MobileHomePage, { global: { plugins: [pinia, router] } })
}

async function mountHomeWithRouter(permissions: string[]): Promise<{ wrapper: VueWrapper; router: ReturnType<typeof createRouter> }> {
  const pinia = createPinia()
  setActivePinia(pinia)
  persistAuthSession(permissions)
  await useAuthStore().restore()
  const router = createRouter({ history: createMemoryHistory(), routes })
  await router.push('/mobile/home')
  await router.isReady()
  const wrapper = mount(MobileHomePage, { global: { plugins: [pinia, router] } })
  return { wrapper, router }
}

describe('MobileHomePage', () => {
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

  it('同步时间问候、日期时钟、低突兀环境光与刷新入口', async () => {
    vi.useFakeTimers()
    vi.setSystemTime(new Date(2026, 7, 20, 20, 13, 3))

    const wrapper = await mountHome()

    expect(wrapper.get('[data-testid="time-header"]').classes()).toContain(
      'time-greeting-header--evening',
    )
    expect(wrapper.get('[data-testid="time-greeting"]').text()).toContain('晚上好')
    expect(wrapper.get('[data-testid="time-date"]').text()).toContain('2026年8月20日')
    expect(wrapper.get('[data-testid="live-clock"]').text()).toBe('20:13:03')
    expect(wrapper.get('[data-testid="time-header"]').attributes('style')).toContain(
      '--time-greeting-accent:',
    )
    expect(wrapper.get('[data-testid="refresh-home"]').text()).toContain('刷新')

    await vi.advanceTimersByTimeAsync(1000)
    expect(wrapper.get('[data-testid="live-clock"]').text()).toBe('20:13:04')
  })

  it('展示当前用户欢迎信息', async () => {
    const wrapper = await mountHome()
    expect(wrapper.get('[data-testid="welcome"]').text()).toContain('Mock 演示账号')
  })

  it('展示当前终端、认证模式与数据来源标识', async () => {
    const wrapper = await mountHome()
    expect(wrapper.get('[data-testid="terminal"]').text()).toBe('Mobile')
    expect(wrapper.get('[data-testid="auth-mode"]').text()).toContain('Mock')
    expect(wrapper.get('[data-testid="data-source"]').text()).toBe('演示数据')
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

  it('有文件读取权限时显示文件上传功能并进入 Mobile 文件路由', async () => {
    const { wrapper, router } = await mountHomeWithRouter([
      'platform.mobile.view',
      PERMISSIONS.systemDataFileRead,
    ])
    const fileLink = wrapper.get('[data-testid="terminal-feature-menu-file"]')
    expect(fileLink.text()).toContain('文件上传')
    await fileLink.trigger('click')
    await flushPromises()
    expect(router.currentRoute.value.name).toBe('mobile-files')
  })

  it('没有文件读取权限时不显示功能菜单并保留 Mobile 空状态', async () => {
    const wrapper = await mountHome(['platform.mobile.view'])
    expect(wrapper.find('[data-testid="terminal-feature-menu"]').exists()).toBe(false)
    expect(wrapper.text()).toContain('业务功能将在后续阶段接入')
  })

  it('功能菜单随语言切换显示英文文案', async () => {
    const { wrapper } = await mountHomeWithRouter([
      'platform.mobile.view',
      PERMISSIONS.systemDataFileRead,
    ])
    useLocalizationStore().setLocale('en-US')
    await nextTick()
    expect(wrapper.get('[data-testid="terminal-feature-menu"]').text()).toContain('Available features')
    expect(wrapper.get('[data-testid="terminal-feature-menu-file"]').text()).toContain('File upload')
    expect(wrapper.get('[data-testid="welcome-description"]').text()).toContain(
      'Mobile workspace status and available features',
    )
    expect(wrapper.findAll('dt').map((item) => item.text())).toEqual([
      'Current terminal',
      'Authentication mode',
      'Data source',
    ])
    expect(wrapper.get('[data-testid="auth-mode"]').text()).toBe('Mock (demo mode)')
    expect(wrapper.get('[data-testid="data-source"]').text()).toBe('Demo data')
  })

  it('不伪造任何生产数值', async () => {
    const wrapper = await mountHome()
    expect(wrapper.text()).not.toMatch(/\d+(\.\d+)?\s*%/)
  })

  it('仅使用时间问候作为页面主标题', async () => {
    vi.useFakeTimers()
    vi.setSystemTime(new Date(2026, 7, 20, 10, 13, 3))
    const wrapper = await mountHome()
    expect(wrapper.findAll('h1')).toHaveLength(1)
    expect(wrapper.get('h1').text()).toMatch(/好，Mock 演示账号$/)
    expect(wrapper.find('.app-page__header').exists()).toBe(false)
  })
})
