/**
 * ThemeControl 组件测试(PF-01 §7.6):
 * PC 显示密度、PDA/Mobile 隐藏密度、radio 即时生效、ARIA 与 Escape 关闭。
 * 焦点断言依赖 attachTo: document.body(游离容器中 focus() 不更新 activeElement)。
 */

import { mount, type VueWrapper } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { afterEach, describe, expect, it, vi } from 'vitest'

import ThemeControl from '@/components/theme/ThemeControl.vue'
import { useThemeStore } from '@/stores/themeStore'

type Terminal = 'pc' | 'pda' | 'mobile'

const wrappers: VueWrapper[] = []

function mountControl(terminal: Terminal): {
  wrapper: VueWrapper
  store: ReturnType<typeof useThemeStore>
} {
  const pinia = createPinia()
  setActivePinia(pinia)
  vi.stubGlobal(
    'matchMedia',
    vi.fn(() => ({
      matches: false,
      addEventListener: vi.fn(),
      removeEventListener: vi.fn(),
      addListener: vi.fn(),
      removeListener: vi.fn(),
    })),
  )
  const wrapper = mount(ThemeControl, {
    props: { terminal },
    attachTo: document.body,
    global: { plugins: [pinia] },
  })
  wrappers.push(wrapper)
  return { wrapper, store: useThemeStore() }
}

describe('ThemeControl', () => {
  afterEach(() => {
    wrappers.splice(0).forEach((w) => w.unmount())
    document.body.innerHTML = ''
    vi.unstubAllGlobals()
  })

  it('PC 触发按钮 aria-haspopup 与展开状态;点击展开面板', async () => {
    const { wrapper } = mountControl('pc')
    const trigger = wrapper.get('[data-testid="theme-control-trigger"]')
    expect(trigger.attributes('aria-haspopup')).toBe('true')
    expect(trigger.attributes('aria-expanded')).toBe('false')
    await trigger.trigger('click')
    expect(trigger.attributes('aria-expanded')).toBe('true')
    expect(wrapper.get('.theme-control__panel').attributes('role')).toBe('group')
  })

  it('PC 面板包含配色、明暗与密度三组', async () => {
    const { wrapper } = mountControl('pc')
    await wrapper.get('[data-testid="theme-control-trigger"]').trigger('click')
    const legends = wrapper.findAll('legend').map((el) => el.text())
    expect(legends).toEqual(['配色', '明暗模式', '密度'])
  })

  it('PDA/Mobile 隐藏密度组,仍显示配色与明暗', async () => {
    for (const terminal of ['pda', 'mobile'] as const) {
      const { wrapper } = mountControl(terminal)
      await wrapper.get('[data-testid="theme-control-trigger"]').trigger('click')
      const legends = wrapper.findAll('legend').map((el) => el.text())
      expect(legends).toEqual(['配色', '明暗模式'])
      expect(wrapper.find('[data-testid="theme-density-comfortable"]').exists()).toBe(false)
    }
  })

  it('选择配色立即生效:更新 store 与根节点 data-ip-palette', async () => {
    const { wrapper, store } = mountControl('pc')
    await wrapper.get('[data-testid="theme-control-trigger"]').trigger('click')
    await wrapper.get('[data-testid="theme-palette-technology-blue"]').setValue(true)
    expect(store.preferences.palette).toBe('technology-blue')
    expect(document.documentElement.getAttribute('data-ip-palette')).toBe('technology-blue')
  })

  it('选择明暗立即生效:暗色更新 store 与 data-ip-color-mode', async () => {
    const { wrapper, store } = mountControl('pc')
    await wrapper.get('[data-testid="theme-control-trigger"]').trigger('click')
    await wrapper.get('[data-testid="theme-mode-dark"]').setValue(true)
    expect(store.preferences.mode).toBe('dark')
    expect(document.documentElement.getAttribute('data-ip-color-mode')).toBe('dark')
  })

  it('选择密度立即生效(仅 PC)', async () => {
    const { wrapper, store } = mountControl('pc')
    await wrapper.get('[data-testid="theme-control-trigger"]').trigger('click')
    await wrapper.get('[data-testid="theme-density-compact"]').setValue(true)
    expect(store.preferences.density).toBe('compact')
    expect(document.documentElement.getAttribute('data-ip-density')).toBe('compact')
  })

  it('Escape 关闭面板并把焦点归还触发按钮', async () => {
    const { wrapper } = mountControl('pc')
    const trigger = wrapper.get('[data-testid="theme-control-trigger"]')
    await trigger.trigger('click')
    expect(wrapper.find('.theme-control__panel').exists()).toBe(true)
    await wrapper.get('.theme-control__panel').trigger('keydown', { key: 'Escape' })
    expect(wrapper.find('.theme-control__panel').exists()).toBe(false)
    expect(document.activeElement).toBe(trigger.element)
  })
})
