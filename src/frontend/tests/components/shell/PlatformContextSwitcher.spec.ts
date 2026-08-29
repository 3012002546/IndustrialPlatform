import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'

import PlatformContextSwitcher from '@/components/shell/PlatformContextSwitcher.vue'

describe('PlatformContextSwitcher', () => {
  it('renders only a real authenticated tenant context', () => {
    const wrapper = mount(PlatformContextSwitcher, {
      props: { tenant: { id: 'tenant-1', name: '甲公司' } },
    })
    expect(wrapper.text()).toContain('甲公司')
  })

  it('renders no static choices when the session has no tenant', () => {
    const wrapper = mount(PlatformContextSwitcher, { props: { tenant: null } })
    expect(wrapper.find('[data-testid="tenant-context"]').exists()).toBe(false)
  })
})
