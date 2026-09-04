import { flushPromises, mount } from '@vue/test-utils'
import ElementPlus from 'element-plus'
import { createPinia, setActivePinia } from 'pinia'
import { describe, expect, it, vi } from 'vitest'

import { persistAuthSession } from '../fixtures/session'
import NavigationAdminPage from '@/components/systemData/NavigationAdminPage.vue'
import { useAuthStore } from '@/stores/authStore'
import { useSystemDataManagementStore } from '@/stores/systemData/managementStore'

const api = {
  getNavigationDraft: vi.fn().mockResolvedValue({ draftRevision: 1, nodes: [] }),
  previewNavigationDefaults: vi.fn(),
  importNavigationDefaults: vi.fn(),
  listResources: vi.fn().mockResolvedValue([]),
  listFeatures: vi.fn().mockResolvedValue([]),
}

vi.mock('@/api/systemData/managementRegistry', () => ({
  getSystemDataManagementApi: () => api,
}))

describe('SystemData action permissions', () => {
  it('shows a read-only default import preview and performs no write on cancel', async () => {
    api.previewNavigationDefaults.mockResolvedValueOnce({
      draftRevision: 1,
      items: [
        {
          nodeNId: 'navigation.group.workspace',
          label: '工作台',
          parentNodeNId: null,
          kind: 'Group',
          level: 1,
          action: 'Add',
          reason: '确认后追加到当前草稿。',
        },
        {
          nodeNId: 'navigation.link.identity-users',
          label: '用户管理',
          parentNodeNId: 'navigation.group.identity-access',
          kind: 'Link',
          level: 3,
          action: 'Skipped',
          reason: '节点已存在,不会覆盖当前草稿。',
        },
        {
          nodeNId: 'navigation.link.blocked',
          label: '受阻入口',
          parentNodeNId: null,
          kind: 'Link',
          level: 1,
          action: 'Blocked',
          reason: '缺少受信任资源。',
        },
      ],
    })
    const pinia = createPinia()
    setActivePinia(pinia)
    useAuthStore().adoptSession(
      persistAuthSession(['systemdata.navigation.view', 'systemdata.navigation.manage']),
    )

    const wrapper = mount(NavigationAdminPage, {
      global: { plugins: [pinia, ElementPlus] },
    })
    await flushPromises()
    await wrapper.get('[data-testid="systemdata-navigation-defaults"]').trigger('click')
    await flushPromises()

    expect(api.previewNavigationDefaults).toHaveBeenCalled()
    const preview = document.body.querySelector(
      '[data-testid="systemdata-navigation-defaults-preview"]',
    )
    expect(preview).not.toBeNull()
    expect(preview?.textContent).toContain('工作台')
    expect(preview?.textContent).toContain('已阻断')
    expect(
      (
        document.body.querySelector(
          '[data-testid="systemdata-navigation-defaults-confirm"]',
        ) as HTMLButtonElement
      ).disabled,
    ).toBe(true)
    ;(
      document.body.querySelector(
        '[data-testid="systemdata-navigation-defaults-cancel"]',
      ) as HTMLButtonElement
    ).click()
    expect(api.importNavigationDefaults).not.toHaveBeenCalled()
  })

  it('keeps the default preview open and asks for re-preview after a draft revision change', async () => {
    api.previewNavigationDefaults.mockReset()
    api.previewNavigationDefaults.mockResolvedValue({
      draftRevision: 1,
      items: [
        {
          nodeNId: 'navigation.group.workspace',
          label: '工作台',
          parentNodeNId: null,
          kind: 'Group',
          level: 1,
          action: 'Add',
          reason: '确认后追加到当前草稿。',
        },
      ],
    })
    const pinia = createPinia()
    setActivePinia(pinia)
    useAuthStore().adoptSession(
      persistAuthSession(['systemdata.navigation.view', 'systemdata.navigation.manage']),
    )

    const wrapper = mount(NavigationAdminPage, {
      global: { plugins: [pinia, ElementPlus] },
    })
    await flushPromises()
    await wrapper.get('[data-testid="systemdata-navigation-defaults"]').trigger('click')
    await flushPromises()

    useSystemDataManagementStore().navigationDraft!.draftRevision = 2
    const confirm = document.body.querySelector(
      '[data-testid="systemdata-navigation-defaults-confirm"]',
    ) as HTMLButtonElement
    expect(confirm).not.toBeNull()
    confirm.click()
    await flushPromises()

    const preview = document.body.querySelector(
      '[data-testid="systemdata-navigation-defaults-preview"]',
    )
    expect(preview).not.toBeNull()
    expect(preview?.textContent).toContain('草稿已被其他操作更新')
    expect(api.importNavigationDefaults).not.toHaveBeenCalled()
  })

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
    expect(wrapper.find('[data-testid="systemdata-navigation-preview"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="systemdata-navigation-new-first-level"]').exists()).toBe(
      false,
    )
    expect(wrapper.find('[data-testid="systemdata-navigation-new"]').exists()).toBe(false)
    expect(wrapper.find('[data-testid="systemdata-navigation-save"]').exists()).toBe(false)
  })

  it('renders navigation mutations only with the matching operation permissions', async () => {
    const pinia = createPinia()
    setActivePinia(pinia)
    useAuthStore().adoptSession(
      persistAuthSession([
        'systemdata.navigation.view',
        'systemdata.navigation.manage',
        'systemdata.navigation.publish',
        'systemdata.navigation.rollback',
      ]),
    )

    const wrapper = mount(NavigationAdminPage, {
      global: { plugins: [pinia, ElementPlus] },
    })
    await flushPromises()

    expect(wrapper.find('[data-testid="systemdata-navigation-validate"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="systemdata-navigation-publish"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="systemdata-navigation-rollback"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="systemdata-navigation-preview"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="systemdata-navigation-new-first-level"]').exists()).toBe(
      true,
    )
    expect(wrapper.find('[data-testid="systemdata-navigation-new"]').exists()).toBe(false)
    expect(wrapper.find('[data-testid="systemdata-navigation-save"]').exists()).toBe(false)
  })
})
