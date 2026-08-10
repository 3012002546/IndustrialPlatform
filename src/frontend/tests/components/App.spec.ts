import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import { createMemoryHistory, createRouter } from 'vue-router'

import App from '@/App.vue'

const TestRoute = {
  render: () => 'router-outlet-content',
}

describe('App', () => {
  it('renders the active route through the router outlet', async () => {
    const router = createRouter({
      history: createMemoryHistory(),
      routes: [{ path: '/', component: TestRoute }],
    })
    await router.push('/')
    await router.isReady()
    const wrapper = mount(App, { global: { plugins: [router] } })
    expect(wrapper.text()).toContain('router-outlet-content')
  })
})
