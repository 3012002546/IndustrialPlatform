import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'

import App from '@/App.vue'

describe('App', () => {
  it('renders the application shell title', () => {
    const wrapper = mount(App)
    expect(wrapper.find('h1').text()).toContain('Industrial Platform')
  })

  it('shows the milestone placeholder', () => {
    const wrapper = mount(App)
    expect(wrapper.text()).toContain('统一前端工程已初始化')
  })
})
