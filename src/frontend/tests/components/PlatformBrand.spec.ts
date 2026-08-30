import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'

import PlatformBrand from '@/components/brand/PlatformBrand.vue'

describe('PlatformBrand', () => {
  function readPngDimensions(path: string): { width: number; height: number } {
    const bytes = readFileSync(path)
    return { width: bytes.readUInt32BE(16), height: bytes.readUInt32BE(20) }
  }

  it.each(['light', 'dark', 'monochrome'] as const)(
    'renders the %s asset with an accessible name',
    (variant) => {
      const wrapper = mount(PlatformBrand, { props: { variant } })
      const image = wrapper.get('img')
      expect(image.attributes('src')).toBe(
        variant === 'monochrome' ? '/brand/horizontal-light.png' : `/brand/horizontal-${variant}.png`,
      )
      expect(image.attributes('alt')).toBe('Industrial Platform')
      expect(wrapper.findAll('.ip-brand__name')).toHaveLength(0)
    },
  )

  it('uses the undistorted mark asset for compact horizontal brands', () => {
    const wrapper = mount(PlatformBrand, { props: { variant: 'dark', compact: true } })
    expect(wrapper.get('img').attributes('src')).toBe('/brand/mark.png')
    expect(wrapper.findAll('.ip-brand__name')).toHaveLength(0)
    expect(wrapper.classes()).toContain('ip-brand--compact')
  })

  it('uses the standalone mark when the public name flag is disabled', () => {
    const wrapper = mount(PlatformBrand, { props: { variant: 'dark', showName: false } })
    expect(wrapper.get('img').attributes('src')).toBe('/brand/mark.png')
    expect(wrapper.findAll('.ip-brand__name')).toHaveLength(0)
  })

  it('renders the standalone mark without a product name by default', () => {
    const wrapper = mount(PlatformBrand, { props: { variant: 'mark', showName: false } })
    expect(wrapper.get('img').attributes('src')).toBe('/brand/mark.png')
    expect(wrapper.text()).not.toContain('Industrial Platform')
  })

  it('uses a text fallback when the asset cannot load', async () => {
    const wrapper = mount(PlatformBrand, { props: { variant: 'dark' } })
    await wrapper.get('img').trigger('error')
    expect(wrapper.get('[role="img"]').attributes('aria-label')).toBe('Industrial Platform')
    expect(wrapper.text()).toContain('IP')
  })

  it('ships a tightly cropped mark and matching PWA icon dimensions', () => {
    expect(readPngDimensions(resolve(process.cwd(), 'public/brand/mark.png'))).toEqual({
      width: 370,
      height: 371,
    })
    expect(readPngDimensions(resolve(process.cwd(), 'public/brand/mark-master.png'))).toEqual({
      width: 370,
      height: 371,
    })

    const manifest = JSON.parse(
      readFileSync(resolve(process.cwd(), 'public/site.webmanifest'), 'utf8'),
    ) as { icons: Array<{ src: string; sizes: string }> }
    expect(manifest.icons).toContainEqual({
      src: '/brand/mark.png',
      sizes: '370x371',
      type: 'image/png',
      purpose: 'any maskable',
    })
  })
})
