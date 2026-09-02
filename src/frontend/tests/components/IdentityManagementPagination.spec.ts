import { flushPromises, mount, type VueWrapper } from '@vue/test-utils'
import ElementPlus from 'element-plus'
import { createPinia, setActivePinia } from 'pinia'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { defineComponent, type Component } from 'vue'

import { persistAuthSession } from '../fixtures/session'
import AppDataTable from '@/components/management/AppDataTable.vue'
import IdentityAuditsPage from '@/pages/pc/identity/IdentityAuditsPage.vue'
import IdentityRolesPage from '@/pages/pc/identity/IdentityRolesPage.vue'
import IdentityUserGroupsPage from '@/pages/pc/identity/IdentityUserGroupsPage.vue'
import { useAuthStore } from '@/stores/authStore'

const { fakeManagement } = vi.hoisted(() => ({
  fakeManagement: {
    listUsers: vi.fn(),
    listUserGroups: vi.fn(),
    listRoles: vi.fn(),
    listLoginAudits: vi.fn(),
    getPermissionTree: vi.fn(),
  },
}))

vi.mock('@/api/identity/managementRegistry', () => ({
  getManagementApi: () => fakeManagement,
  registerManagementApi: vi.fn(),
}))

const wrappers: VueWrapper[] = []
const TeleportStub = defineComponent({
  name: 'TeleportStub',
  props: { to: { type: String, required: true }, disabled: Boolean },
  template: '<div><slot /></div>',
})

function pageResult<T>(params?: { pageIndex?: number }): {
  items: T[]
  total: number
  pageIndex: number
  pageSize: number
} {
  return { items: [], total: 0, pageIndex: params?.pageIndex ?? 1, pageSize: 25 }
}

async function mountPage(
  component: Component,
  permissions: readonly string[],
): Promise<VueWrapper> {
  const pinia = createPinia()
  setActivePinia(pinia)
  persistAuthSession([...permissions])
  await useAuthStore().restore()

  const wrapper = mount(component, {
    attachTo: document.body,
    global: {
      plugins: [pinia, ElementPlus],
      stubs: {
        teleport: TeleportStub,
        'el-table': true,
        'el-table-column': true,
        'el-select': true,
        'el-option': true,
        'el-checkbox': true,
        'el-pagination': true,
        'el-tooltip': true,
      },
    },
  })
  wrappers.push(wrapper)
  await flushPromises()
  return wrapper
}

type AppDataTableVm = {
  currentPage: number
  onPageChange: (page: number) => void
}

describe('Identity loader page pagination contract', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    vi.stubEnv('VITE_AUTH_MODE', 'mock')
    fakeManagement.listUsers.mockResolvedValue(pageResult())
    fakeManagement.getPermissionTree.mockResolvedValue([])
    fakeManagement.listUserGroups.mockImplementation((params: { pageIndex?: number }) =>
      Promise.resolve(pageResult(params)),
    )
    fakeManagement.listRoles.mockImplementation((params: { pageIndex?: number }) =>
      Promise.resolve(pageResult(params)),
    )
    fakeManagement.listLoginAudits.mockImplementation((params: { pageIndex?: number }) =>
      Promise.resolve(pageResult(params)),
    )
  })

  afterEach(() => {
    wrappers.splice(0).forEach((wrapper) => wrapper.unmount())
    document.body.innerHTML = ''
    sessionStorage.clear()
    localStorage.clear()
    vi.unstubAllEnvs()
  })

  it.each([
    [IdentityUserGroupsPage, ['identity.user-group.view'], fakeManagement.listUserGroups],
    [IdentityRolesPage, ['identity.role.view'], fakeManagement.listRoles],
    [IdentityAuditsPage, ['identity.audit.view'], fakeManagement.listLoginAudits],
  ] as const)(
    'returns %s from page 2 to page 1 with one top-query request',
    async (component, permissions, list) => {
      const wrapper = await mountPage(component, permissions)
      const table = wrapper.findComponent(AppDataTable) as unknown as VueWrapper
      const tableVm = table.vm as unknown as AppDataTableVm

      tableVm.onPageChange(2)
      await flushPromises()
      expect(tableVm.currentPage).toBe(2)

      const callsBeforeQuery = list.mock.calls.length
      await wrapper.get('[data-testid="query-panel-submit"]').trigger('click')
      await flushPromises()

      expect(list.mock.calls.length).toBe(callsBeforeQuery + 1)
      expect(list).toHaveBeenLastCalledWith(expect.objectContaining({ pageIndex: 1 }))
      expect(tableVm.currentPage).toBe(1)
    },
  )
})
