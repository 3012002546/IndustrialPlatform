import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'

import PlatformServiceStatus from '@/components/shell/PlatformServiceStatus.vue'

describe('PlatformServiceStatus', () => {
  it('projects the existing SystemData runtime state without claiming global health', () => {
    const wrapper = mount(PlatformServiceStatus, {
      props: { degraded: true, unavailable: false },
    })
    expect(wrapper.get('[data-testid="platform-service-status"]').text()).toContain('SystemData')
    expect(wrapper.text()).not.toContain('全平台健康')
  })

  it('does not render a status badge when runtime facts are healthy and absent', () => {
    const wrapper = mount(PlatformServiceStatus, {
      props: { degraded: false, unavailable: false },
    })
    expect(wrapper.find('[data-testid="platform-service-status"]').exists()).toBe(false)
  })

  it('bounds long degraded copy so it cannot expand the topbar actions', async () => {
    const loader = import.meta.glob('../../../src/components/shell/PlatformServiceStatus.vue', {
      query: '?raw',
      import: 'default',
    })
    const source = (await loader['../../../src/components/shell/PlatformServiceStatus.vue']!()) as string

    expect(source).toMatch(
      /\.ip-platform-service-status\s*\{[\s\S]*?max-width:\s*min\(280px,\s*24vw\);[\s\S]*?overflow:\s*hidden;/,
    )
    expect(source).toMatch(
      /\.ip-platform-service-status\s+span\s*\{[\s\S]*?text-overflow:\s*ellipsis;[\s\S]*?white-space:\s*nowrap;/,
    )
    expect(source).toMatch(/@media\s*\(max-width:\s*1440px\)[\s\S]*?flex:\s*0\s+0\s+32px;/)
    expect(source).toContain('ip-platform-service-status__retry-icon')
  })
})
