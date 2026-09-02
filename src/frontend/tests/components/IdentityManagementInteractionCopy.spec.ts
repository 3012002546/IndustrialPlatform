import { flushPromises, mount, type VueWrapper } from '@vue/test-utils'
import ElementPlus from 'element-plus'
import { createPinia, setActivePinia } from 'pinia'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { defineComponent } from 'vue'

import { persistAuthSession } from '../fixtures/session'
import IdentityAuditsPage from '@/pages/pc/identity/IdentityAuditsPage.vue'
import IdentityPermissionsPage from '@/pages/pc/identity/IdentityPermissionsPage.vue'
import IdentityRolesPage from '@/pages/pc/identity/IdentityRolesPage.vue'
import IdentityUserGroupsPage from '@/pages/pc/identity/IdentityUserGroupsPage.vue'
import SsoClientsPage from '@/pages/pc/identity/sso/SsoClientsPage.vue'
import SsoProvidersPage from '@/pages/pc/identity/sso/SsoProvidersPage.vue'
import { useAuthStore } from '@/stores/authStore'
import { useLocalizationStore } from '@/stores/localizationStore'

const { fakeManagement, fakeSsoManagement } = vi.hoisted(() => ({
  fakeManagement: {
    listUsers: vi.fn(),
    listUserGroups: vi.fn(),
    listRoles: vi.fn(),
    listLoginAudits: vi.fn(),
    getPermissionTree: vi.fn(),
    createUserGroup: vi.fn(),
    updateUserGroup: vi.fn(),
    setUserGroupStatus: vi.fn(),
    getUserGroup: vi.fn(),
    setUserGroupMembers: vi.fn(),
    setUserGroupRoles: vi.fn(),
    deleteUserGroup: vi.fn(),
    restoreUserGroup: vi.fn(),
    createRole: vi.fn(),
    updateRole: vi.fn(),
    assignRolePermissions: vi.fn(),
  },
  fakeSsoManagement: {
    listProviders: vi.fn(),
    listClients: vi.fn(),
    listAccounts: vi.fn(),
    exportProviders: vi.fn(),
    exportAccounts: vi.fn(),
    exportClients: vi.fn(),
    exportClientEndpoints: vi.fn(),
    createProvider: vi.fn(),
    updateProvider: vi.fn(),
    updateProviderSecret: vi.fn(),
    setProviderEnabled: vi.fn(),
    testProvider: vi.fn(),
    bindAccount: vi.fn(),
    unbindAccount: vi.fn(),
    getClient: vi.fn(),
    createClient: vi.fn(),
    updateClient: vi.fn(),
    setClientEnabled: vi.fn(),
    addClientEndpoint: vi.fn(),
    setClientEndpointEnabled: vi.fn(),
    removeClientEndpoint: vi.fn(),
  },
}))

vi.mock('@/api/identity/managementRegistry', () => ({
  getManagementApi: () => fakeManagement,
  registerManagementApi: vi.fn(),
}))

vi.mock('@/api/identity/ssoManagement', () => ({
  getSsoManagementApi: () => fakeSsoManagement,
  registerSsoManagementApi: vi.fn(),
}))

vi.mock('element-plus/es/components/focus-trap/index', async () => {
  const { defineComponent } = await import('vue')
  return {
    ElFocusTrap: defineComponent({
      name: 'ElFocusTrapStub',
      template: '<div><slot /></div>',
    }),
    default: defineComponent({
      name: 'ElFocusTrapStub',
      template: '<div><slot /></div>',
    }),
  }
})

const TeleportStub = defineComponent({
  name: 'TeleportStub',
  props: { to: { type: String, required: true }, disabled: Boolean },
  template: '<div><slot /></div>',
})

const wrappers: VueWrapper[] = []

function emptyPage<T>(): { items: T[]; total: number; pageIndex: number; pageSize: number } {
  return { items: [], total: 0, pageIndex: 1, pageSize: 25 }
}

async function mountPage(
  component: typeof IdentityUserGroupsPage,
  permissions: readonly string[],
): Promise<VueWrapper> {
  const pinia = createPinia()
  setActivePinia(pinia)
  persistAuthSession([...permissions])
  await useAuthStore().restore()
  useLocalizationStore().setLocale('en-US', null)

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
        'el-descriptions': true,
        'el-descriptions-item': true,
        'el-tooltip': true,
      },
    },
  })
  wrappers.push(wrapper)
  await flushPromises()
  return wrapper
}

describe('Identity management interaction copy', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    vi.stubEnv('VITE_AUTH_MODE', 'mock')

    fakeManagement.listUsers.mockResolvedValue(emptyPage())
    fakeManagement.listUserGroups.mockResolvedValue(emptyPage())
    fakeManagement.listRoles.mockResolvedValue(emptyPage())
    fakeManagement.listLoginAudits.mockResolvedValue(emptyPage())
    fakeManagement.getPermissionTree.mockResolvedValue([])
    fakeSsoManagement.listProviders.mockResolvedValue([])
    fakeSsoManagement.listClients.mockResolvedValue([])
    fakeSsoManagement.listAccounts.mockResolvedValue([])
  })

  afterEach(() => {
    wrappers.splice(0).forEach((wrapper) => wrapper.unmount())
    document.body.innerHTML = ''
    sessionStorage.clear()
    localStorage.clear()
    vi.unstubAllEnvs()
  })

  it.each([
    [IdentityUserGroupsPage, ['identity.user-group.view'], 'User groups'],
    [IdentityRolesPage, ['identity.role.view'], 'Roles & permissions'],
    [IdentityPermissionsPage, ['identity.permission.view'], 'Permission catalog'],
    [IdentityAuditsPage, ['identity.audit.view'], 'Login audit'],
    [SsoProvidersPage, ['identity.sso.view'], 'Enterprise login sources'],
    [SsoClientsPage, ['identity.sso.view'], 'SSO clients'],
  ] as const)('renders %s-owned page copy in English', async (component, permissions, title) => {
    const wrapper = await mountPage(component, permissions)

    expect(wrapper.get('h1').text()).toBe(title)
    expect(wrapper.text()).not.toMatch(/[\u3400-\u9fff]/)
  })

  it.each([
    [
      IdentityUserGroupsPage,
      ['identity.user-group.view', 'identity.user-group.create'],
      '[data-testid="user-groups-create"]',
      'Enter a group name',
    ],
    [
      IdentityRolesPage,
      ['identity.role.view', 'identity.role.create'],
      '[data-testid="roles-create"]',
      'Enter a role name',
    ],
    [
      SsoProvidersPage,
      ['identity.sso.view', 'identity.sso.manage'],
      '[data-testid="sso-provider-create"]',
      'Enter a login source name',
    ],
    [
      SsoClientsPage,
      ['identity.sso.view', 'identity.sso.manage'],
      '[data-testid="sso-client-create"]',
      'Enter a client name',
    ],
  ] as const)(
    'uses localized required validation for %s',
    async (component, permissions, trigger, message) => {
      const wrapper = await mountPage(component, permissions)
      await wrapper.get(trigger).trigger('click')
      await flushPromises()

      const rules = wrapper.findComponent({ name: 'ElForm' }).props('rules') as Record<
        string,
        Array<{ message?: string }>
      >
      const messages = Object.values(rules)
        .flat()
        .map((rule) => rule.message)
        .filter((ruleMessage): ruleMessage is string => ruleMessage !== undefined)

      expect(messages).toContain(message)
      expect(messages.join('')).not.toMatch(/[\u3400-\u9fff]/)
    },
  )
})
