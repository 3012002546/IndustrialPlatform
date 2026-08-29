import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, describe, expect, it } from 'vitest'
import { h } from 'vue'
import { createMemoryHistory, createRouter } from 'vue-router'

import OperationLayout from '@/layouts/OperationLayout.vue'
import { useLocalizationStore } from '@/stores/localizationStore'

describe('OperationLayout', () => {
  let pinia: ReturnType<typeof createPinia>

  beforeEach(() => {
    pinia = createPinia()
    setActivePinia(pinia)
  })

  it('是无管理导航、无工作区页签的独立简洁壳', () => {
    const router = createRouter({
      history: createMemoryHistory(),
      routes: [{ path: '/', component: { render: () => h('div', 'home') } }],
    })
    const wrapper = mount(OperationLayout, {
      global: { plugins: [router, pinia] },
      slots: { default: () => h('div', { 'data-testid': 'operation-slot' }, 'content') },
    })
    expect(wrapper.find('.ip-toolrail').exists()).toBe(false)
    expect(wrapper.find('.ip-function-tree').exists()).toBe(false)
    expect(wrapper.find('.ip-pc-tabs').exists()).toBe(false)
    expect(wrapper.get('[data-testid="operation-slot"]').text()).toBe('content')
  })

  it('en-US renders localized shell copy without Chinese fallback text', () => {
    useLocalizationStore().setLocale('en-US', null)
    const router = createRouter({
      history: createMemoryHistory(),
      routes: [{ path: '/', component: { render: () => h('div', 'home') } }],
    })
    const wrapper = mount(OperationLayout, { global: { plugins: [router, pinia] } })

    expect(wrapper.text()).toContain('Not signed in')
    expect(wrapper.get('[data-testid="operation-fullscreen"]').attributes('aria-label')).toBe('Browser fullscreen')
    expect(wrapper.text()).not.toMatch(/[\u3400-\u9fff]/)
  })
})
