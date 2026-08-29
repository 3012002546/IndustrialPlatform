import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'

import PlatformEnvironmentBadge from '@/components/shell/PlatformEnvironmentBadge.vue'

describe('PlatformEnvironmentBadge', () => {
  it.each(['DEV', 'TEST', 'UAT', 'PROD'] as const)('renders the controlled %s environment', (environment) => {
    const wrapper = mount(PlatformEnvironmentBadge, { props: { environment } })
    expect(wrapper.get('[data-testid="environment-badge"]').text()).toBe(environment)
  })
})
