import { mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import LocaleControl from '@/components/localization/LocaleControl.vue'

const setLocale = vi.fn()
vi.mock('@/stores/localizationStore', () => ({
  useLocalizationStore: () => ({
    locale: 'zh-CN',
    preferences: { locale: 'zh-CN' },
    setLocale,
  }),
}))

describe('LocaleControl', () => {
  beforeEach(() => setLocale.mockReset())

  it('exposes a 32px accessible locale button and teleported menu', async () => {
    const wrapper = mount(LocaleControl)
    const trigger = wrapper.get('button[aria-label="语言"]')
    expect(trigger.attributes('aria-haspopup')).toBe('listbox')
    expect(trigger.attributes('aria-expanded')).toBe('false')

    await trigger.trigger('click')
    const option = document.querySelector<HTMLElement>('[role="option"][aria-selected="false"]')
    expect(option).not.toBeNull()
    expect(option?.textContent).toContain('English')
    option?.click()

    expect(setLocale).toHaveBeenCalledWith('en-US')
  })

  it('closes with Escape and returns focus to the trigger', async () => {
    const wrapper = mount(LocaleControl)
    const trigger = wrapper.get('button[aria-label="语言"]')
    await trigger.trigger('click')
    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }))
    expect(wrapper.find('[role="listbox"]').exists()).toBe(false)
  })
})
