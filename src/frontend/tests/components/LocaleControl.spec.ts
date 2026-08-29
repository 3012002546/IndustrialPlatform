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

  it('exposes an accessible locale selector and changes only the locale', async () => {
    const wrapper = mount(LocaleControl)
    const select = wrapper.get('select[aria-label="语言"]')
    expect(select.findAll('option').map((option) => option.text())).toEqual(['中文', 'English'])

    await select.setValue('en-US')

    expect(setLocale).toHaveBeenCalledWith('en-US')
  })
})
