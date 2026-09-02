import { flushPromises, mount, type VueWrapper } from '@vue/test-utils'
import ElementPlus from 'element-plus'
import { createPinia, setActivePinia } from 'pinia'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { VxeTable } from 'vxe-table'

import { persistAuthSession } from '../fixtures/session'
import IdentityRolesPage from '@/pages/pc/identity/IdentityRolesPage.vue'
import { useAuthStore } from '@/stores/authStore'

const { fakeApi } = vi.hoisted(() => ({
  fakeApi: {
    listRoles: vi.fn(),
    exportRoles: vi.fn(),
    createRole: vi.fn(),
    updateRole: vi.fn(),
    assignRolePermissions: vi.fn(),
    getPermissionTree: vi.fn(),
  },
}))

vi.mock('@/api/identity/managementRegistry', () => ({
  getManagementApi: () => fakeApi,
  registerManagementApi: vi.fn(),
}))

const wrappers: VueWrapper[] = []

async function mountRolesPage(): Promise<VueWrapper> {
  const pinia = createPinia()
  setActivePinia(pinia)
  persistAuthSession([
    'identity.role.view',
    'identity.role.update',
    'identity.role.assign-permission',
  ])
  await useAuthStore().restore()
  const wrapper = mount(IdentityRolesPage, {
    attachTo: document.body,
    global: {
      plugins: [pinia, ElementPlus],
      stubs: {
        'el-table': true,
        'el-table-column': true,
        'el-select': true,
        'el-option': true,
        'el-pagination': true,
      },
    },
  })
  wrappers.push(wrapper)
  await flushPromises()
  return wrapper
}

describe('IdentityRolesPage', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    vi.stubEnv('VITE_AUTH_MODE', 'mock')
    fakeApi.listRoles.mockResolvedValue({
      items: [
        {
          roleNId: 'operator',
          name: 'Operator',
          description: null,
          isSystem: false,
          tenantNId: 't1',
          permissionNIds: [],
          optimisticVersion: 1,
          concurrencyVersion: 'c1',
        },
      ],
      total: 1,
      pageIndex: 1,
      pageSize: 25,
    })
  })

  afterEach(() => {
    wrappers.splice(0).forEach((wrapper) => wrapper.unmount())
    document.body.innerHTML = ''
    sessionStorage.clear()
    localStorage.clear()
    vi.unstubAllEnvs()
  })

  it('uses More at the 120px action-column minimum and expands direct actions when widened', async () => {
    const wrapper = await mountRolesPage()
    const actions = wrapper.get('[data-testid="identity-role-actions-operator"]')

    expect(actions.text()).toContain('编辑')
    expect(actions.text()).toContain('分配权限')
    expect(actions.find('[data-testid="identity-role-more-operator"]').exists()).toBe(false)

    wrapper.findComponent(VxeTable).vm.$emit('resizable-change', {
      resizeColumn: { field: '__actions' },
      resizeWidth: 120,
    })
    await flushPromises()

    expect(actions.text()).toContain('编辑')
    expect(actions.text()).not.toContain('分配权限')
    expect(actions.find('[data-testid="identity-role-more-operator"]').exists()).toBe(true)

    wrapper.findComponent(VxeTable).vm.$emit('resizable-change', {
      resizeColumn: { field: '__actions' },
      resizeWidth: 220,
    })
    await flushPromises()

    expect(actions.text()).toContain('分配权限')
    expect(actions.find('[data-testid="identity-role-more-operator"]').exists()).toBe(false)
  })
})
