import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import PcOperationHomePage from '@/pages/pc/PcOperationHomePage.vue'
import { useLocalizationStore } from '@/stores/localizationStore'
import operationHomeSource from '@/pages/pc/PcOperationHomePage.vue?raw'

describe('PcOperationHomePage', () => {
  let pinia: ReturnType<typeof createPinia>

  beforeEach(() => {
    pinia = createPinia()
    setActivePinia(pinia)
  })

  it('渲染九个一跳入口,八个待实现入口不可导航', async () => {
    const wrapper = mount(PcOperationHomePage, { global: { plugins: [pinia] } })
    const cards = wrapper.findAll('[data-operation-launcher]')
    expect(cards).toHaveLength(9)
    expect(wrapper.findAll('[aria-disabled="true"]')).toHaveLength(8)
    expect(wrapper.findAll('a')).toHaveLength(0)
    const requestSpy = vi.spyOn(globalThis, 'fetch')
    await cards[0]!.trigger('keydown.enter')
    await cards[0]!.trigger('keydown.space')
    expect(requestSpy).not.toHaveBeenCalled()
  })

  it('界面设置入口可用且提供已有界面设置控件', async () => {
    const wrapper = mount(PcOperationHomePage, { global: { plugins: [pinia] } })
    const settings = wrapper.get('[data-operation-launcher="interface-settings"]')
    expect(settings.attributes('aria-disabled')).toBeUndefined()
    expect(wrapper.find('[data-testid="operation-settings-panel"]').exists()).toBe(false)
    await settings.trigger('click')
    expect(wrapper.find('[data-testid="operation-settings-panel"]').exists()).toBe(true)
  })

  it('en-US renders operation settings without Chinese fallback text', async () => {
    useLocalizationStore().setLocale('en-US', null)
    const wrapper = mount(PcOperationHomePage, { global: { plugins: [pinia] } })
    await wrapper.get('[data-operation-launcher="interface-settings"]').trigger('click')

    expect(wrapper.text()).toContain('Production operations')
    expect(wrapper.text()).toContain('Interface settings')
    expect(wrapper.text()).toContain('Browser fullscreen')
    expect(wrapper.text()).not.toMatch(/[\u3400-\u9fff]/)
  })

  it('维持三列卡片的可视最小高度，避免操作页被压缩截断', () => {
    const wrapper = mount(PcOperationHomePage, { global: { plugins: [pinia] } })
    const cards = wrapper.findAll('.pc-operation-card')

    expect(cards).toHaveLength(9)
    expect(operationHomeSource).toMatch(/\.pc-operation-grid[\s\S]*display:\s*grid/)
    expect(operationHomeSource).toMatch(/\.pc-operation-card[\s\S]*min-height:\s*176px/)
  })
})
