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
})
