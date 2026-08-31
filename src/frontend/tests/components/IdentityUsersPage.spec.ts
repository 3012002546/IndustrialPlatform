/**
 * 用户管理页组件测试(TASK-ID-021,§29A.4/§29A.5):
 * - 新建用户表单不再包含初始密码输入;
 * - 提交载荷不含 initialPassword,由服务端返回一次性临时密码弹窗展示。
 * 管理 API 经 vi.mock 替换 registry,页面运行时从 mock 获取 IdentityManagementApi。
 */

import { flushPromises, mount, type VueWrapper } from '@vue/test-utils'
import ElementPlus from 'element-plus'
import { createPinia, setActivePinia } from 'pinia'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { defineComponent } from 'vue'
import { VxeTable } from 'vxe-table'

import { persistAuthSession } from '../fixtures/session'
import AppDataTable from '@/components/management/AppDataTable.vue'
import type { AppDataTableColumn } from '@/components/management/AppDataTable'
import appDataTableSource from '@/components/management/AppDataTable.vue?raw'
import AppPage from '@/components/base/AppPage.vue'
import AppQueryPanel from '@/components/management/AppQueryPanel.vue'
import type { UserSummaryDto } from '@/api/identity/management'
import IdentityUsersPage from '@/pages/pc/identity/IdentityUsersPage.vue'
import identityUsersPageSource from '@/pages/pc/identity/IdentityUsersPage.vue?raw'
import { useAuthStore } from '@/stores/authStore'
import { useLocalizationStore } from '@/stores/localizationStore'
import { buildPageStateKey, writePageState } from '@/workspace/pageState'
import { clearCurrentUserUiCache } from '@/stores/uiCacheStore'

const { fakeApi } = vi.hoisted(() => ({
  fakeApi: {
    listUsers: vi.fn(),
    listUsersOData: vi.fn(),
    exportUsersOData: vi.fn(),
    getUser: vi.fn(),
    createUser: vi.fn(),
    updateUser: vi.fn(),
    setUserStatus: vi.fn(),
    assignUserRoles: vi.fn(),
    resetPassword: vi.fn(),
    deleteUser: vi.fn(),
    restoreUser: vi.fn(),
    listUserGroups: vi.fn(),
    getUserGroup: vi.fn(),
    createUserGroup: vi.fn(),
    updateUserGroup: vi.fn(),
    setUserGroupStatus: vi.fn(),
    setUserGroupMembers: vi.fn(),
    setUserGroupRoles: vi.fn(),
    deleteUserGroup: vi.fn(),
    restoreUserGroup: vi.fn(),
    listRoles: vi.fn(),
    getRole: vi.fn(),
    createRole: vi.fn(),
    updateRole: vi.fn(),
    assignRolePermissions: vi.fn(),
    getPermissionTree: vi.fn(),
    listLoginAudits: vi.fn(),
  },
}))

vi.mock('@/api/identity/managementRegistry', () => ({
  getManagementApi: () => fakeApi,
  registerManagementApi: vi.fn(),
}))

// el-dialog 内部使用 ElFocusTrap(子路径导入),jsdom 下渲染异常,替换为渲染槽的 stub。
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

type AppDataTableTestInstance = { $props: Record<string, unknown> }

function findDataTable(wrapper: VueWrapper): VueWrapper<AppDataTableTestInstance> {
  return wrapper.findComponent(AppDataTable) as unknown as VueWrapper<AppDataTableTestInstance>
}

function emptyPage<T>(): { items: T[]; total: number; pageIndex: number; pageSize: number } {
  return { items: [], total: 0, pageIndex: 1, pageSize: 25 }
}

async function mountUsersPage(
  permissions: string[],
  options: { stubTeleport?: boolean; stubTooltip?: boolean; locale?: 'zh-CN' | 'en-US' } = {},
): Promise<VueWrapper> {
  const pinia = createPinia()
  setActivePinia(pinia)
  persistAuthSession(permissions)
  await useAuthStore().restore()
  if (options.locale !== undefined) useLocalizationStore().setLocale(options.locale, null)
  const wrapper = mount(IdentityUsersPage, {
    global: {
      plugins: [pinia, ElementPlus],
      stubs: {
        ...(options.stubTeleport === false ? {} : { teleport: TeleportStub }),
        // jsdom 下 Element Plus 表格/下拉的布局副作用(递归更新告警)与断言无关,统一打桩。
        'el-table': true,
        'el-table-column': true,
        'el-select': true,
        'el-option': true,
        ...(options.stubTooltip === false ? {} : { 'el-tooltip': true }),
        'el-checkbox': true,
        'el-pagination': true,
        'el-descriptions': true,
        'el-descriptions-item': true,
      },
    },
  })
  wrappers.push(wrapper)
  await flushPromises()
  return wrapper
}

async function openCreateDialog(wrapper: VueWrapper): Promise<void> {
  const createButton = wrapper.findAll('button').find((b) => b.text() === '新建用户')
  expect(createButton).toBeDefined()
  await createButton!.trigger('click')
  await flushPromises()
}

async function clickSave(wrapper: VueWrapper): Promise<void> {
  const saveButton = wrapper.findAll('button').find((b) => b.text() === '保存')
  expect(saveButton).toBeDefined()
  await saveButton!.trigger('click')
  await flushPromises()
}

describe('IdentityUsersPage — 创建用户(服务端随机临时密码)', () => {
  it('uses existing platform icons for create and advanced-condition actions', () => {
    expect(identityUsersPageSource).toMatch(
      /import\s*\{[^}]*ArrowDown[^}]*Plus[^}]*\}\s*from\s*'@element-plus\/icons-vue'/s,
    )
    expect(identityUsersPageSource).toMatch(
      /<ElIcon\s+class="users-page__create-icon"\s+aria-hidden="true">\s*<Plus\s*\/>\s*<\/ElIcon>/s,
    )
    expect(identityUsersPageSource).toMatch(/<ArrowDown\s+aria-hidden="true"\s*\/>/)
  })
  beforeEach(() => {
    vi.clearAllMocks()
    // 页面经 persistAuthSession 写入 Mock 会话键后 restore,显式声明 mock,不依赖产品默认(现为 http)。
    vi.stubEnv('VITE_AUTH_MODE', 'mock')
    fakeApi.listUsers.mockResolvedValue(emptyPage())
    fakeApi.listUsersOData.mockResolvedValue(emptyPage())
    fakeApi.listRoles.mockResolvedValue(emptyPage())
    fakeApi.listUserGroups.mockResolvedValue(emptyPage())
  })

  afterEach(() => {
    vi.unstubAllEnvs()
  })

  afterEach(() => {
    wrappers.splice(0).forEach((w) => w.unmount())
    document.body.innerHTML = ''
    sessionStorage.clear()
    localStorage.clear()
  })

  it('新建用户表单不再包含初始密码输入', async () => {
    const wrapper = await mountUsersPage(['identity.user.view', 'identity.user.create'])
    await openCreateDialog(wrapper)

    // 表单字段:业务标识/登录名/姓名/邮箱/手机号;绝无初始密码。
    expect(wrapper.text()).not.toContain('初始密码')
    expect(wrapper.find('input[type="password"]').exists()).toBe(false)
    expect(wrapper.find('input[placeholder="登录用户名"]').exists()).toBe(true)
    expect(wrapper.find('input[placeholder="显示姓名"]').exists()).toBe(true)
  })

  it('固定使用共享 PageHeader、QueryPanel 和 DataTable 组合', async () => {
    const wrapper = await mountUsersPage(['identity.user.view', 'identity.user.create'])

    const page = wrapper.findComponent(AppPage)
    expect(page.exists()).toBe(true)
    expect(page.attributes('data-testid')).toBe('identity-users-page')
    expect(page.get('h1').text()).toBe('用户管理')
    expect(page.findComponent(AppQueryPanel).exists()).toBe(true)
    expect(page.findComponent(AppQueryPanel).props('showActions')).toBe(true)
    expect(page.findComponent(AppDataTable).exists()).toBe(true)
    expect(page.find('[data-testid="identity-users-create"]').exists()).toBe(true)
    expect(page.findAll('.users-page__toolbar')).toHaveLength(0)
  })

  it('把真实总数作为标题旁的轻量计数 pill,表面只保留 VXE 自己的横向滚动', async () => {
    const wrapper = await mountUsersPage(['identity.user.view'])

    const title = wrapper.get('.app-page__title')
    const count = wrapper.get('.users-page__count')
    expect(title.element.parentElement?.contains(count.element)).toBe(true)
    expect(count.attributes('data-testid')).toBe('identity-users-total')
    expect(appDataTableSource).toMatch(
      /\.app-data-table__surface\s*\{[\s\S]*?overflow:\s*hidden;/,
    )
  })

  it('五个主查询条件默认可见,低频条件单独收纳在更多条件中', async () => {
    const wrapper = await mountUsersPage(['identity.user.view'])

    expect(wrapper.find('.users-page__field-login').text()).toContain('登录名')
    expect(wrapper.find('.users-page__field-name').text()).toContain('姓名')
    expect(wrapper.find('.users-page__field-status').text()).toContain('状态')
    expect(wrapper.find('.users-page__field-group').text()).toContain('用户组')
    expect(wrapper.find('.users-page__field-role').text()).toContain('角色')
    const toggle = wrapper.get('[data-testid="query-panel-toggle"]')
    expect(toggle.text()).toContain('更多条件')
    expect(toggle.attributes('aria-expanded')).toBe('false')
    expect(wrapper.find('.app-query-panel__header').exists()).toBe(false)
    expect(wrapper.get('.app-query-panel__body').find('[data-testid="query-panel-toggle"]').exists()).toBe(true)
    expect(wrapper.find('.users-page__field-business-id').exists()).toBe(false)

    await toggle.trigger('click')
    expect(toggle.attributes('aria-expanded')).toBe('true')
    expect(wrapper.find('.users-page__field-business-id').exists()).toBe(true)

    const table = findDataTable(wrapper)
    expect(table.props('toolbarTitle')).toBe('用户列表')
    expect(table.props('toolbarLabels')).toBe(true)
  })

  it('1280px 窄 PC 保持五个主条件与操作同一行', () => {
    expect(identityUsersPageSource).toMatch(
      /@media\s*\(min-width:\s*960px\)\s+and\s+\(max-width:\s*1280px\)[\s\S]*?\.users-page :deep\(\.app-query-panel__body--grid\)[\s\S]*?flex-wrap:\s*nowrap;/,
    )
    expect(identityUsersPageSource).toMatch(
      /\.users-page :deep\(\.app-query-panel__body-actions\)[\s\S]*?flex-wrap:\s*nowrap;/,
    )
  })

  it('无创建权限时隐藏页面主操作，并且查询动作可用键盘聚焦', async () => {
    const wrapper = await mountUsersPage(['identity.user.view'])

    expect(wrapper.find('[data-testid="identity-users-create"]').exists()).toBe(false)
    const submit = wrapper.get('[data-testid="query-panel-submit"]')
    const reset = wrapper.get('[data-testid="query-panel-reset"]')
    expect(submit.attributes('type')).toBe('button')
    expect(reset.attributes('type')).toBe('button')
    expect(submit.attributes('disabled')).toBeUndefined()
    expect(reset.attributes('disabled')).toBeUndefined()
  })

  it('按当前用户作用域恢复 page-state 后再发起首次列表查询', async () => {
    const scope = { tenantId: 't1', userId: 'u1' }
    writePageState(sessionStorage, scope, 'identity-users', {
      query: { loginName: 'e2e.admin' },
      pageIndex: 2,
      pageSize: 10,
      scrollTop: 180,
    })
    fakeApi.listUsers.mockResolvedValue({ items: [], total: 0, pageIndex: 2, pageSize: 10 })

    const wrapper = await mountUsersPage(['identity.user.view'])

    expect(wrapper.get('input[aria-label="登录名"]').element).toHaveProperty('value', 'e2e.admin')
    expect(fakeApi.listUsers).toHaveBeenCalledWith(
      expect.objectContaining({ loginName: 'e2e.admin', pageIndex: 2, pageSize: 10 }),
    )
    expect(sessionStorage.getItem(buildPageStateKey(scope, 'identity-users'))).not.toBeNull()
  })

  it('恢复列头查询模式及字段值,避免生产模式往返后退回顶部查询', async () => {
    const scope = { tenantId: 't1', userId: 'u1' }
    writePageState(sessionStorage, scope, 'identity-users', {
      queryMode: 'header',
      headerFilters: { loginName: 'e2e.admin', mustChangePassword: true },
      pageIndex: 2,
      pageSize: 10,
    })
    fakeApi.listUsersOData.mockResolvedValue({ items: [], total: 0, pageIndex: 2, pageSize: 10 })

    const wrapper = await mountUsersPage(['identity.user.view'])
    const table = findDataTable(wrapper)

    expect(table.props('queryMode')).toBe('header')
    expect(table.props('initialHeaderFilters')).toEqual({
      loginName: 'e2e.admin',
      mustChangePassword: true,
    })
    expect(table.props('pageSize')).toBe(10)

    await wrapper.get('[data-testid="app-data-table-query-toggle"]').trigger('click')
    await flushPromises()
    expect(table.props('queryMode')).toBe('top')
  })

  it('将共享表格的列头查询与分页写入当前用户 page-state', async () => {
    const scope = { tenantId: 't1', userId: 'u1' }
    const wrapper = await mountUsersPage(['identity.user.view'])
    const table = findDataTable(wrapper)

    ;(table.vm as unknown as { switchQueryMode: (mode: 'top' | 'header') => void }).switchQueryMode(
      'header',
    )
    await flushPromises()
    table.vm.$emit('query-change', {
      pageIndex: 2,
      pageSize: 10,
      queryMode: 'header',
      filters: { loginName: 'e2e.admin' },
      columns: ['loginName'],
    })
    await wrapper.vm.$nextTick()

    expect(JSON.parse(sessionStorage.getItem(buildPageStateKey(scope, 'identity-users'))!)).toEqual(
      expect.objectContaining({
        queryMode: 'header',
        headerFilters: { loginName: 'e2e.admin' },
        pageIndex: 2,
        pageSize: 10,
      }),
    )
  })

  it('将列头文本与布尔筛选一起写入 page-state,避免往返丢失条件', async () => {
    const scope = { tenantId: 't1', userId: 'u1' }
    const wrapper = await mountUsersPage(['identity.user.view'])
    const table = findDataTable(wrapper)

    ;(table.vm as unknown as { switchQueryMode: (mode: 'top' | 'header') => void }).switchQueryMode(
      'header',
    )
    await flushPromises()
    table.vm.$emit('query-change', {
      pageIndex: 1,
      pageSize: 25,
      queryMode: 'header',
      filters: { loginName: 'e2e.admin', mustChangePassword: true },
      columns: ['loginName', 'mustChangePassword'],
    })
    await wrapper.vm.$nextTick()

    expect(JSON.parse(sessionStorage.getItem(buildPageStateKey(scope, 'identity-users'))!)).toEqual(
      expect.objectContaining({
        queryMode: 'header',
        headerFilters: { loginName: 'e2e.admin', mustChangePassword: true },
      }),
    )
  })

  it('收到当前作用域清理事件后清空用户页查询,而不调用旧查询回写', async () => {
    const scope = { tenantId: 't1', userId: 'u1' }
    const wrapper = await mountUsersPage(['identity.user.view'])
    const input = wrapper.get('input[aria-label="登录名"]')
    await input.setValue('e2e.admin')
    await clearCurrentUserUiCache(scope)
    await wrapper.vm.$nextTick()

    expect((input.element as HTMLInputElement).value).toBe('')
    expect(findDataTable(wrapper).props('queryMode')).toBe('top')
  })

  it('页面标题和说明随 locale 使用稳定资源', async () => {
    const wrapper = await mountUsersPage(['identity.user.view'], { locale: 'en-US' })

    expect(wrapper.get('h1').text()).toBe('User management')
    expect(wrapper.find('.app-page__description').text()).toBe(
      'Manage platform users, status, and access',
    )
  })

  it('列头查询覆盖用户数据库字段，但不虚接派生统计字段', async () => {
    const wrapper = await mountUsersPage(['identity.user.view'])

    const columns = findDataTable(wrapper).props('columns') as AppDataTableColumn[]
    expect(columns.find((column) => column.field === 'lastLoginOn')).toEqual(
      expect.objectContaining({ width: 240, minWidth: 240 }),
    )
    expect(columns.find((column) => column.field === 'createdOn')).toEqual(
      expect.objectContaining({ width: 240, minWidth: 240 }),
    )
    expect(columns.find((column) => column.field === 'lastLoginOn')?.fixed).toBeUndefined()
    expect(columns.find((column) => column.field === 'createdOn')?.fixed).toBeUndefined()

    await wrapper.get('[data-testid="app-data-table-query-toggle"]').trigger('click')
    await flushPromises()

    expect(
      wrapper.findAll('[data-testid="app-data-table-header-filter-loginName"]'),
    ).toHaveLength(1)
    expect(wrapper.find('[data-testid="app-data-table-header-filter-email"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="app-data-table-header-filter-phone"]').exists()).toBe(true)
    expect(
      wrapper.find('[data-testid="app-data-table-header-filter-lastLoginOn-range"]').exists(),
    ).toBe(true)
    expect(
      wrapper.find('[data-testid="app-data-table-header-filter-createdOn-range"]').exists(),
    ).toBe(true)
    expect(
      wrapper.find('[data-testid="app-data-table-header-filter-effectiveRoleCount"]').exists(),
    ).toBe(false)
  })

  it('列头查询接受 OData 部分投影并保持角色统计可渲染', async () => {
    fakeApi.listUsersOData.mockResolvedValue({
      items: [
        {
          userNId: 'u-odata',
          loginName: 'odata.user',
          name: 'OData User',
          email: null,
          phone: null,
          status: 'Active',
          tenantNId: 't1',
          createdOn: '2026-01-01T00:00:00Z',
          lastLoginOn: null,
          mustChangePassword: false,
          effectiveRoleCount: 2,
        } as unknown as UserSummaryDto,
      ],
      total: 1,
      pageIndex: 1,
      pageSize: 25,
    })

    const wrapper = await mountUsersPage(['identity.user.view'])
    await wrapper.get('[data-testid="app-data-table-query-toggle"]').trigger('click')
    await flushPromises()

    expect(wrapper.text()).toContain('2')
    expect(wrapper.find('[data-testid="identity-users-page"]').exists()).toBe(true)
  })

  it('keeps both date range filters scrollable instead of widening the narrow fixed area', async () => {
    const wrapper = await mountUsersPage(['identity.user.view'])

    await wrapper.get('[data-testid="app-data-table-query-toggle"]').trigger('click')
    await flushPromises()

    const columns = findDataTable(wrapper).props('columns') as AppDataTableColumn[]
    const dateColumns = columns.filter(
      (column) => column.field === 'lastLoginOn' || column.field === 'createdOn',
    )
    expect(dateColumns).toHaveLength(2)
    expect(dateColumns.every((column) => column.fixed === undefined)).toBe(true)
    expect(dateColumns.every((column) => column.width === 240 && column.minWidth === 240)).toBe(
      true,
    )
    expect(
      wrapper.find('[data-testid="app-data-table-header-filter-lastLoginOn-range"]').exists(),
    ).toBe(true)
    expect(
      wrapper.find('[data-testid="app-data-table-header-filter-createdOn-range"]').exists(),
    ).toBe(true)
  })

  it('maps header filters, sort and pagination into the users API loader', async () => {
    const wrapper = await mountUsersPage(['identity.user.view'])

    await wrapper.get('[data-testid="app-data-table-query-toggle"]').trigger('click')
    await flushPromises()
    await wrapper.get('[data-testid="app-data-table-header-filter-email"]').setValue('alice@')
    await wrapper.get('[data-testid="app-data-table-header-filter-email"]').trigger('keyup.enter')
    await flushPromises()

    const table = wrapper.findComponent({ name: 'VxeTable' })
    table.vm.$emit('sort-change', { field: 'lastLoginOn', order: 'desc' })
    await flushPromises()

    expect(fakeApi.listUsersOData).toHaveBeenLastCalledWith(
      expect.objectContaining({
        pageIndex: 1,
        pageSize: 25,
        filters: expect.arrayContaining([
          expect.objectContaining({ field: 'email', value: 'alice@' }),
        ]),
        orderBy: expect.arrayContaining([
          expect.objectContaining({ field: 'lastLoginOn', direction: 'desc' }),
        ]),
      }),
    )
  })

  it('keeps top-level group, role and deleted filters when the table reloads', () => {
    expect(identityUsersPageSource).toMatch(/query\.groupNId\.trim\(\) !== ''/)
    expect(identityUsersPageSource).toMatch(/query\.roleNId\.trim\(\) !== ''/)
    expect(identityUsersPageSource).toMatch(/query\.includeDeleted/)
    expect(identityUsersPageSource).toMatch(
      /request\.queryMode === 'top' && hasLegacyOnlyTopConditions\(\)/,
    )
    expect(identityUsersPageSource).toMatch(/groupNId: query\.groupNId\.trim\(\) \|\| undefined/)
    expect(identityUsersPageSource).toMatch(/roleNId: query\.roleNId\.trim\(\) \|\| undefined/)
    expect(identityUsersPageSource).toMatch(/includeDeleted: query\.includeDeleted \|\| undefined/)
  })

  it('maps the table quick search to the active users server query', async () => {
    const wrapper = await mountUsersPage(['identity.user.view'])

    await wrapper.get('[data-testid="app-data-table-quick-search"]').setValue('alice')
    await flushPromises()

    expect(fakeApi.listUsers).toHaveBeenCalledTimes(1)
    expect(fakeApi.listUsers).not.toHaveBeenLastCalledWith(
      expect.objectContaining({ keyword: 'alice' }),
    )
  })

  it('shows user actions progressively from the actual action-column width', async () => {
    fakeApi.listUsers.mockResolvedValue({
      items: [
        {
          userNId: 'u1',
          loginName: 'alice',
          name: 'Alice',
          email: 'alice@example.com',
          phone: null,
          status: 'Active',
          tenantNId: 't1',
          createdOn: '2026-01-01T00:00:00Z',
          lastLoginOn: null,
          mustChangePassword: false,
          directRoleNIds: [],
          groupRoleNIds: [],
          effectiveRoleNIds: [],
          optimisticVersion: 1,
          concurrencyVersion: 'c1',
          isDeleted: false,
        },
      ],
      total: 1,
      pageIndex: 1,
      pageSize: 25,
    })
    const wrapper = await mountUsersPage(
      [
        'identity.user.view',
        'identity.user.update',
        'identity.user.status',
        'identity.user.assign-role',
        'identity.user.reset-password',
        'identity.user.delete',
      ],
      { stubTeleport: false, stubTooltip: false },
    )
    const actions = wrapper.get('[data-testid="identity-user-actions-u1"]')

    expect(actions.text()).toContain('详情')
    expect(actions.text()).toContain('编辑')
    expect(actions.text()).toContain('禁用')
    expect(actions.find('[data-testid="identity-user-more-u1"]').exists()).toBe(true)
    const closedMenu = document.body.querySelector('[data-testid="identity-user-more-menu-u1"]')
    expect(closedMenu?.closest('.el-popper')?.getAttribute('style')).toContain('display: none')
    await actions.get('[data-testid="identity-user-more-u1"]').trigger('click')
    await flushPromises()
    const menu = document.body.querySelector('[data-testid="identity-user-more-menu-u1"]')
    expect(menu).not.toBeNull()
    expect(menu?.textContent).toContain('分配角色')
    expect(menu?.textContent).toContain('重置密码')
    expect(menu?.textContent).toContain('删除')
    expect(menu?.closest('.vxe-table--fixed-right-body-wrapper')).toBeNull()
    const assignRoles = menu?.querySelector('[data-testid="identity-user-action-assign-role-u1"]')
    expect(assignRoles).not.toBeNull()
    ;(assignRoles as HTMLElement).click()
    await flushPromises()
    const closedAfterCommand = document.body.querySelector(
      '[data-testid="identity-user-more-menu-u1"]',
    )
    expect(closedAfterCommand?.closest('.el-popper')?.getAttribute('style')).toContain(
      'display: none',
    )
    expect((wrapper.vm as unknown as { rolesDialogOpen: boolean }).rolesDialogOpen).toBe(true)
    expect(wrapper.find('.app-data-table__dialog-backdrop').exists()).toBe(false)

    wrapper.findComponent(VxeTable).vm.$emit('resizable-change', {
      resizeColumn: { field: '__actions' },
      resizeWidth: 367,
    })
    await flushPromises()
    expect(actions.text()).toContain('分配角色')
    expect(actions.text()).toContain('重置密码')
    expect(actions.text()).toContain('删除')
    expect(actions.find('[data-testid="identity-user-more-u1"]').exists()).toBe(false)
  })

  it('创建提交正确载荷(不含 initialPassword),并在成功后弹出一次性临时密码', async () => {
    fakeApi.createUser.mockResolvedValue({
      user: {
        userNId: 'u9',
        loginName: 'alice',
        name: 'Alice',
        email: null,
        phone: null,
        status: 'Active',
        tenantNId: 't1',
        createdOn: '2026-01-01T00:00:00Z',
        lastLoginOn: null,
        mustChangePassword: true,
        directRoleNIds: [],
        groupRoleNIds: [],
        effectiveRoleNIds: [],
        optimisticVersion: 1,
        concurrencyVersion: 'c1',
      },
      temporaryPassword: 'Tmp!Pass123',
    })

    const wrapper = await mountUsersPage(['identity.user.view', 'identity.user.create'])
    await openCreateDialog(wrapper)

    await wrapper.get('input[placeholder="登录用户名"]').setValue('alice')
    await wrapper.get('input[placeholder="显示姓名"]').setValue('Alice')
    await clickSave(wrapper)

    expect(fakeApi.createUser).toHaveBeenCalledTimes(1)
    expect(fakeApi.createUser).toHaveBeenCalledWith({
      nId: undefined,
      loginName: 'alice',
      name: 'Alice',
      email: null,
      phone: null,
    })
    const [body] = fakeApi.createUser.mock.calls[0] as [unknown]
    expect(JSON.stringify(body)).not.toContain('initialPassword')

    // 创建成功后展示一次性临时密码弹窗。
    await flushPromises()
    expect(wrapper.text()).toContain('临时密码')
    expect(wrapper.text()).toContain('Tmp!Pass123')
  })
})
