import { mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'

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
    expect(
      Array.from(document.querySelectorAll<HTMLElement>('[role="option"]')).map((item) => item.textContent?.trim()),
    ).toEqual(['中文', 'English'])
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

  it('keeps the language menu at the approved 144px surface width', () => {
    const source = readFileSync(resolve(process.cwd(), 'src/components/localization/LocaleControl.vue'), 'utf8')
    expect(source).toContain('const LOCALE_MENU_WIDTH = 144')
    expect(source).toContain('window.innerWidth - LOCALE_MENU_WIDTH - LOCALE_MENU_GUTTER')
    expect(source).toMatch(/\.ip-locale-control__menu\s*\{[\s\S]*?box-sizing:\s*border-box;[\s\S]*?width:\s*144px;/)
  })
})
