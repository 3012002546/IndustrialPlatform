import { flushPromises, mount } from '@vue/test-utils'
import ElementPlus from 'element-plus'
import { createPinia, setActivePinia } from 'pinia'
import { describe, expect, it, vi } from 'vitest'

import { persistAuthSession } from '../fixtures/session'
import NavigationAdminPage from '@/components/systemData/NavigationAdminPage.vue'
import { useAuthStore } from '@/stores/authStore'

const api = {
  getNavigationDraft: vi.fn().mockResolvedValue({ draftRevision: 1, nodes: [] }),
  listResources: vi.fn().mockResolvedValue([]),
  listFeatures: vi.fn().mockResolvedValue([]),
}

vi.mock('@/api/systemData/managementRegistry', () => ({
  getSystemDataManagementApi: () => api,
}))

describe('SystemData action permissions', () => {
  it('hides navigation mutations when the user has view only', async () => {
    const pinia = createPinia()
    setActivePinia(pinia)
    useAuthStore().adoptSession(persistAuthSession(['systemdata.navigation.view']))

    const wrapper = mount(NavigationAdminPage, {
      global: { plugins: [pinia, ElementPlus] },
    })
    await flushPromises()

    expect(wrapper.find('[data-testid="systemdata-navigation-validate"]').exists()).toBe(false)
    expect(wrapper.find('[data-testid="systemdata-navigation-publish"]').exists()).toBe(false)
    expect(wrapper.find('[data-testid="systemdata-navigation-rollback"]').exists()).toBe(false)
    expect(wrapper.find('[data-testid="systemdata-navigation-save"]').exists()).toBe(false)
  })

  it('renders navigation mutations only with the matching operation permissions', async () => {
    const pinia = createPinia()
    setActivePinia(pinia)
    useAuthStore().adoptSession(persistAuthSession([
      'systemdata.navigation.view',
      'systemdata.navigation.manage',
      'systemdata.navigation.publish',
      'systemdata.navigation.rollback',
    ]))

    const wrapper = mount(NavigationAdminPage, {
      global: { plugins: [pinia, ElementPlus] },
    })
    await flushPromises()

    expect(wrapper.find('[data-testid="systemdata-navigation-validate"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="systemdata-navigation-publish"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="systemdata-navigation-rollback"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="systemdata-navigation-save"]').exists()).toBe(true)
  })
})
