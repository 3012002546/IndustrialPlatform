import { mount } from '@vue/test-utils'
import { createPinia } from 'pinia'
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
    // App.vue 顶层调用 useAuthStore(TASK-ID-011 权限响应),需先安装 Pinia。
    const wrapper = mount(App, { global: { plugins: [createPinia(), router] } })
    expect(wrapper.text()).toContain('router-outlet-content')
  })
})
