import { flushPromises, mount, type VueWrapper } from '@vue/test-utils'
import ElementPlus, { ElDropdown, ElDropdownItem, ElMessageBox } from 'element-plus'
import { createPinia, setActivePinia } from 'pinia'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { VxeTable } from 'vxe-table'

import { persistAuthSession } from '../fixtures/session'
import IdentityUserGroupsPage from '@/pages/pc/identity/IdentityUserGroupsPage.vue'
import { useAuthStore } from '@/stores/authStore'
import { useLocalizationStore } from '@/stores/localizationStore'

const { fakeApi } = vi.hoisted(() => ({
  fakeApi: {
    listUserGroups: vi.fn(),
    listUsers: vi.fn(),
    listRoles: vi.fn(),
    getUserGroup: vi.fn(),
    setUserGroupStatus: vi.fn(),
    deleteUserGroup: vi.fn(),
    restoreUserGroup: vi.fn(),
  },
}))

vi.mock('@/api/identity/managementRegistry', () => ({
  getManagementApi: () => fakeApi,
  registerManagementApi: vi.fn(),
}))

const group = {
  groupNId: 'operators',
  name: 'Operators',
  description: null,
  status: 'Active',
  tenantNId: 't1',
  memberCount: 0,
  roleCount: 0,
  isDeleted: false,
  optimisticVersion: 1,
  concurrencyVersion: 'c1',
}
const allActions = ['update', 'status', 'assign-member', 'assign-role', 'delete', 'restore']
const wrappers: VueWrapper[] = []

async function mountGroups(permissions = allActions): Promise<VueWrapper> {
  const pinia = createPinia()
  setActivePinia(pinia)
  persistAuthSession([
    'identity.user-group.view',
    ...permissions.map((p) => `identity.user-group.${p}`),
  ])
  await useAuthStore().restore()
  const wrapper = mount(IdentityUserGroupsPage, {
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

async function resizeActions(wrapper: VueWrapper, width: number): Promise<void> {
  wrapper.findComponent(VxeTable).vm.$emit('resizable-change', {
    resizeColumn: { field: '__actions' },
    resizeWidth: width,
  })
  await flushPromises()
}

function menuCommands(wrapper: VueWrapper): string[] {
  return wrapper.findAllComponents(ElDropdownItem).map((item) => String(item.props('command')))
}

describe('IdentityUserGroupsPage action overflow', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    vi.stubEnv('VITE_AUTH_MODE', 'mock')
    fakeApi.listUserGroups.mockResolvedValue({
      items: [{ ...group }],
      total: 1,
      pageIndex: 1,
      pageSize: 25,
    })
    fakeApi.listUsers.mockResolvedValue({ items: [], total: 0 })
    fakeApi.listRoles.mockResolvedValue({ items: [], total: 0 })
    fakeApi.getUserGroup.mockResolvedValue({ ...group, memberUserNIds: [], roleNIds: [] })
  })

  afterEach(() => {
    wrappers.splice(0).forEach((wrapper) => wrapper.unmount())
    document.body.innerHTML = ''
    sessionStorage.clear()
    localStorage.clear()
    vi.restoreAllMocks()
    vi.unstubAllEnvs()
  })

  it('moves all overflow into More at 120px and restores direct actions when widened', async () => {
    const wrapper = await mountGroups()
    await resizeActions(wrapper, 120)
    const actions = wrapper.get('[data-testid="identity-group-actions-operators"]')
    expect(actions.text()).toBe('更多')
    expect(menuCommands(wrapper)).toEqual(['edit', 'status', 'members', 'roles', 'delete'])

    await resizeActions(wrapper, 500)
    expect(actions.text()).toContain('编辑用户组')
    expect(actions.text()).toContain('成员')
    expect(actions.text()).toContain('角色')
    expect(actions.find('[data-testid="identity-group-more-operators"]').exists()).toBe(false)
  })

  it('keeps unauthorized actions out of both direct buttons and More', async () => {
    const wrapper = await mountGroups(['assign-member'])
    await resizeActions(wrapper, 120)
    expect(wrapper.get('[data-testid="identity-group-actions-operators"]').text()).toBe('成员')
    expect(menuCommands(wrapper)).toEqual([])
  })

  it('retains restore instead of delete for deleted groups, without hiding other permitted actions', async () => {
    fakeApi.listUserGroups.mockResolvedValue({
      items: [{ ...group, isDeleted: true }],
      total: 1,
      pageIndex: 1,
      pageSize: 25,
    })
    const wrapper = await mountGroups()
    await resizeActions(wrapper, 120)
    expect(menuCommands(wrapper)).toEqual(['edit', 'status', 'members', 'roles', 'restore'])
  })

  it('supports longer English labels and the localized More menu', async () => {
    const wrapper = await mountGroups()
    useLocalizationStore().setLocale('en-US', null)
    await resizeActions(wrapper, 120)
    expect(wrapper.get('[data-testid="identity-group-actions-operators"]').text()).toBe('More')
    expect(menuCommands(wrapper)).toEqual(['edit', 'status', 'members', 'roles', 'delete'])
    await resizeActions(wrapper, 500)
    expect(wrapper.find('[data-testid="identity-group-more-operators"]').exists()).toBe(false)
  })

  it('opens the existing member dialog from More without issuing a business write', async () => {
    const wrapper = await mountGroups()
    await resizeActions(wrapper, 120)
    wrapper.findComponent(ElDropdown).vm.$emit('command', 'members')
    await flushPromises()
    expect(fakeApi.getUserGroup).toHaveBeenCalledWith('operators')
    expect(fakeApi.setUserGroupStatus).not.toHaveBeenCalled()
    expect(fakeApi.deleteUserGroup).not.toHaveBeenCalled()
    expect(fakeApi.restoreUserGroup).not.toHaveBeenCalled()
  })

  it('preserves the status confirmation and does not write when it is cancelled', async () => {
    const confirm = vi.spyOn(ElMessageBox, 'confirm').mockRejectedValue('cancel')
    const wrapper = await mountGroups()
    await resizeActions(wrapper, 120)
    wrapper.findComponent(ElDropdown).vm.$emit('command', 'status')
    await flushPromises()
    expect(confirm).toHaveBeenCalled()
    expect(fakeApi.setUserGroupStatus).not.toHaveBeenCalled()
  })
})
