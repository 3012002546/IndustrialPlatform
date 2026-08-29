import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import PcOperationHomePage from '@/pages/pc/PcOperationHomePage.vue'

describe('PcOperationHomePage', () => {
  beforeEach(() => setActivePinia(createPinia()))

  it('渲染九个一跳入口,八个待实现入口不可导航', async () => {
    const wrapper = mount(PcOperationHomePage)
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
    const wrapper = mount(PcOperationHomePage)
    const settings = wrapper.get('[data-operation-launcher="interface-settings"]')
    expect(settings.attributes('aria-disabled')).toBeUndefined()
    expect(wrapper.find('[data-testid="operation-settings-panel"]').exists()).toBe(false)
    await settings.trigger('click')
    expect(wrapper.find('[data-testid="operation-settings-panel"]').exists()).toBe(true)
  })
})
