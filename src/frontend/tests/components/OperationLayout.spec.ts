import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, describe, expect, it } from 'vitest'
import { h } from 'vue'
import { createMemoryHistory, createRouter } from 'vue-router'

import OperationLayout from '@/layouts/OperationLayout.vue'

describe('OperationLayout', () => {
  beforeEach(() => setActivePinia(createPinia()))

  it('是无管理导航、无工作区页签的独立简洁壳', () => {
    const router = createRouter({
      history: createMemoryHistory(),
      routes: [{ path: '/', component: { render: () => h('div', 'home') } }],
    })
    const wrapper = mount(OperationLayout, {
      global: { plugins: [router] },
      slots: { default: () => h('div', { 'data-testid': 'operation-slot' }, 'content') },
    })
    expect(wrapper.find('.ip-toolrail').exists()).toBe(false)
    expect(wrapper.find('.ip-function-tree').exists()).toBe(false)
    expect(wrapper.find('.ip-pc-tabs').exists()).toBe(false)
    expect(wrapper.get('[data-testid="operation-slot"]').text()).toBe('content')
  })
})
