import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { describe, expect, it } from 'vitest'

import PermissionGate from '@/permissions/PermissionGate.vue'
import { useAuthStore } from '@/stores/authStore'
import { makeAuthSession } from '../fixtures/session'

describe('PermissionGate', () => {
  it('hides content without the requested permission', () => {
    const pinia = createPinia()
    setActivePinia(pinia)
    useAuthStore().adoptSession(makeAuthSession([]))

    const wrapper = mount(PermissionGate, {
      props: { permissionNId: 'systemdata.navigation.publish' },
      slots: { default: '<button>发布</button>', denied: '<span>无权限</span>' },
      global: { plugins: [pinia] },
    })

    expect(wrapper.find('button').exists()).toBe(false)
    expect(wrapper.text()).toContain('无权限')
  })

  it('renders a disabled slot when mode is disabled', () => {
    const pinia = createPinia()
    setActivePinia(pinia)
    useAuthStore().adoptSession(makeAuthSession([]))

    const wrapper = mount(PermissionGate, {
      props: { permissionNId: 'systemdata.navigation.publish', mode: 'disabled' },
      slots: { default: '<button :disabled="disabled">发布</button>' },
      global: { plugins: [pinia] },
    })

    expect(wrapper.get('button').attributes('disabled')).toBeDefined()
  })
})
