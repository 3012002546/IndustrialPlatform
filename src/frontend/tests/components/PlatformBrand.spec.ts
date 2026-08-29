import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'

import PlatformBrand from '@/components/brand/PlatformBrand.vue'

describe('PlatformBrand', () => {
  it.each(['light', 'dark', 'monochrome'] as const)('renders the %s asset with an accessible name', (variant) => {
    const wrapper = mount(PlatformBrand, { props: { variant } })
    const image = wrapper.get('img')
    expect(image.attributes('src')).toBe(
      variant === 'monochrome' ? '/brand/monochrome.svg' : `/brand/horizontal-${variant}.svg`,
    )
    expect(image.attributes('alt')).toBe('Industrial Platform')
    expect(wrapper.text()).toContain('Industrial Platform')
  })

  it('renders the standalone mark without a product name by default and supports compact mode', () => {
    const wrapper = mount(PlatformBrand, { props: { variant: 'mark', compact: true, showName: false } })
    expect(wrapper.get('img').attributes('src')).toBe('/brand/mark.svg')
    expect(wrapper.text()).not.toContain('Industrial Platform')
    expect(wrapper.classes()).toContain('ip-brand--compact')
  })

  it('uses a text fallback when the asset cannot load', async () => {
    const wrapper = mount(PlatformBrand, { props: { variant: 'dark' } })
    await wrapper.get('img').trigger('error')
    expect(wrapper.get('[role="img"]').attributes('aria-label')).toBe('Industrial Platform')
    expect(wrapper.text()).toContain('IP')
  })
})
