import { mount } from '@vue/test-utils'
import { createPinia } from 'pinia'
import { ElConfigProvider } from 'element-plus'
import { describe, expect, it } from 'vitest'
import { createMemoryHistory, createRouter } from 'vue-router'

import App from '@/App.vue'
import { useLocalizationStore } from '@/stores/localizationStore'

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

  it('keeps Element Plus pagination copy in sync with the platform locale', async () => {
    const router = createRouter({
      history: createMemoryHistory(),
      routes: [{ path: '/', component: TestRoute }],
    })
    await router.push('/')
    await router.isReady()
    const pinia = createPinia()
    const wrapper = mount(App, { global: { plugins: [pinia, router] } })
    const localization = useLocalizationStore(pinia)
    const config = wrapper.findComponent(ElConfigProvider)

    localization.setLocale('en-US', null)
    await wrapper.vm.$nextTick()
    expect(
      (config.props('locale') as unknown as { el: { pagination: { goto: string } } }).el.pagination.goto,
    ).toBe('Go to')

    localization.setLocale('zh-CN', null)
    await wrapper.vm.$nextTick()
    expect(
      (config.props('locale') as unknown as { el: { pagination: { goto: string } } }).el.pagination.goto,
    ).toBe('前往')
  })
})
