/**
 * PC 平台外壳布局测试(PF-01 §7.8):
 * 四区结构(顶栏/工具轨/功能树/主内容)、跳到主内容入口、品牌与终端信息、
 * 工具轨当前分组跟随路由、分组切换联动功能树、功能树收起走 ThemeStore(不直接写旧侧栏键)、
 * 功能树授权过滤、用户菜单退出登录。
 */

import { flushPromises, mount, type VueWrapper } from '@vue/test-utils'
import { ElDropdown } from 'element-plus'
import { createPinia, setActivePinia } from 'pinia'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { nextTick } from 'vue'
import { createMemoryHistory, createRouter, type Router, RouterView } from 'vue-router'

import { writeAuthSession } from '@/auth'
import type { AuthSession } from '@/auth/types'
import PcLayout from '@/layouts/PcLayout.vue'
import { PERMISSIONS } from '@/permissions'
import { routes } from '@/router/routes'
import WorkspaceTabLimitDialog from '@/components/shell/WorkspaceTabLimitDialog.vue'
import { useAuthStore } from '@/stores/authStore'
import { useThemeStore } from '@/stores/themeStore'
import { useWorkspaceTabsStore } from '@/stores/workspaceTabsStore'
import type { WorkspaceRouteCandidate } from '@/workspace'

/** 旧侧栏折叠键:PF-01 已迁移到 ThemeStore,本组件不应再读写。 */
const LEGACY_COLLAPSED_KEY = 'industrial-platform.pc.sidebar.collapsed.v1'

const ALL_PC_PERMISSIONS = [
  PERMISSIONS.platformHomeView,
  PERMISSIONS.platformPdaView,
  PERMISSIONS.platformMobileView,
  PERMISSIONS.userView,
  PERMISSIONS.roleView,
  PERMISSIONS.permissionView,
  PERMISSIONS.auditLoginView,
  PERMISSIONS.ssoView,
]

function makeSession(permissions: string[]): AuthSession {
  return {
    accessToken: 'at',
    refreshToken: 'rt',
    expiresAt: new Date(Date.now() + 3_600_000).toISOString(),
    user: {
      userId: 'u1',
      username: 'mock.admin',
      displayName: 'Mock 演示账号',
      tenantId: 't1',
      roles: ['admin'],
      permissions,
      mustChangePassword: false,
    },
  }
}

interface LayoutHarness {
  wrapper: VueWrapper
  router: Router
  themeStore: ReturnType<typeof useThemeStore>
}

function sandboxCandidate(slot: number): WorkspaceRouteCandidate {
  return {
    id: `sandbox:${slot}`,
    title: `沙箱 ${slot}`,
    kind: 'business',
    route: { name: 'workspace-tabs-sandbox', params: {}, query: { slot: String(slot) } },
  }
}

async function mountLayout(permissions: string[] = ALL_PC_PERMISSIONS): Promise<LayoutHarness> {
  const pinia = createPinia()
  setActivePinia(pinia)
  writeAuthSession(sessionStorage, makeSession(permissions))
  const authStore = useAuthStore()
  await authStore.restore()
  vi.stubGlobal(
    'matchMedia',
    vi.fn(() => ({
      matches: false,
      addEventListener: vi.fn(),
      removeEventListener: vi.fn(),
      addListener: vi.fn(),
      removeListener: vi.fn(),
    })),
  )
  const themeStore = useThemeStore()
  await themeStore.initialize()
  // 守卫在真实运行时绑定工作区作用域;组件测试手动等价绑定(scope 与 mock 用户 t1/u1 一致)
  useWorkspaceTabsStore().bindUser({ tenantId: 't1', userId: 'u1' })
  const router = createRouter({ history: createMemoryHistory(), routes })
  await router.push('/pc/home')
  const wrapper = mount(PcLayout, { global: { plugins: [pinia, router] } })
  return { wrapper, router, themeStore }
}

describe('PcLayout', () => {
  beforeEach(() => {
    sessionStorage.clear()
    localStorage.clear()
    // 组件测试验证 Mock 行为(横幅/演示账号/网关),显式声明 mock,不依赖产品默认(现为 http)。
    vi.stubEnv('VITE_AUTH_MODE', 'mock')
  })

  afterEach(() => {
    vi.unstubAllGlobals()
    vi.unstubAllEnvs()
  })

  it('四区结构:顶栏、工具轨、功能树与主内容区', async () => {
    const { wrapper } = await mountLayout()
    expect(wrapper.find('header.ip-topbar').exists()).toBe(true)
    expect(wrapper.get('nav.ip-toolrail').attributes('aria-label')).toBe('平台分组')
    expect(wrapper.find('nav.ip-function-tree').exists()).toBe(true)
    expect(wrapper.find('main#main-content').exists()).toBe(true)
    expect(wrapper.get('main#main-content').attributes('tabindex')).toBe('-1')
  })

  it('跳到主内容入口是布局内第一个可聚焦元素', async () => {
    const { wrapper } = await mountLayout()
    const skip = wrapper.get('a.ip-pc-skip-link')
    expect(skip.attributes('href')).toBe('#main-content')
    expect(skip.text()).toContain('跳到主内容')
    const focusables = wrapper.findAll('a, button')
    expect(focusables[0]?.attributes('href')).toBe('#main-content')
  })

  it('顶栏展示品牌名与终端信息', async () => {
    const { wrapper } = await mountLayout()
    expect(wrapper.get('.ip-pc-brand img').attributes('alt')).toBe('Industrial Platform')
    expect(wrapper.get('.ip-pc-brand').findAll('.ip-brand__name')).toHaveLength(0)
    expect(wrapper.get('[data-testid="terminal-info"]').text()).toContain('PC')
  })

  it('顶栏按左中右三段组织且租户与用户入口保持单行可读', async () => {
    const { wrapper } = await mountLayout()
    expect(wrapper.find('.ip-topbar__brand').exists()).toBe(true)
    expect(wrapper.find('.ip-topbar__search').exists()).toBe(true)
    expect(wrapper.find('.ip-topbar__right').exists()).toBe(true)
    expect(wrapper.get('[data-testid="tenant-context"]').classes()).toContain(
      'ip-context-switcher--single-line',
    )
    expect(wrapper.find('[data-testid="user-menu"] .ip-pc-user__name').exists()).toBe(true)
  })

  it('顶栏展示 Mock 模式横幅', async () => {
    const { wrapper } = await mountLayout()
    expect(wrapper.text()).toContain('开发 Mock 模式')
  })

  it('用户菜单显示当前用户 displayName', async () => {
    const { wrapper } = await mountLayout()
    expect(wrapper.get('[data-testid="user-menu"]').text()).toContain('Mock 演示账号')
  })

  it('当前分组跟随初始路由:工作台按钮 aria-current,功能树标签为工作台', async () => {
    const { wrapper } = await mountLayout()
    const active = wrapper.get('nav.ip-toolrail [aria-current="page"]')
    expect(active.attributes('aria-label')).toBe('工作台')
    expect(wrapper.get('nav.ip-function-tree').attributes('aria-label')).toBe('工作台')
    const activeLink = wrapper.get('nav.ip-function-tree [aria-current="page"]')
    expect(activeLink.text()).toContain('首页')
  })

  it('工作台分组提供受终端权限保护的统一终端预览入口', async () => {
    const { wrapper } = await mountLayout()

    const preview = wrapper
      .findAll('a.ip-function-tree__link')
      .find((link) => link.text().includes('终端预览'))!
    expect(preview.attributes('href')).toBe('/pc/terminal-preview')
  })

  it('点击工具轨切换分组:功能树联动为系统管理并按权限过滤', async () => {
    const { wrapper } = await mountLayout()
    const systemButton = wrapper
      .findAll('nav.ip-toolrail button')
      .find((b) => b.attributes('aria-label') === '系统管理')!
    await systemButton.trigger('click')
    expect(wrapper.get('nav.ip-function-tree').attributes('aria-label')).toBe('系统管理')
    const labels = wrapper
      .findAll('nav.ip-function-tree .ip-function-tree__label')
      .map((n) => n.text())
    expect(labels).toContain('用户管理')
    expect(labels).toContain('企业登录源')
    expect(wrapper.get('nav.ip-toolrail [aria-current="page"]').attributes('aria-label')).toBe(
      '系统管理',
    )
  })

  it('功能树收起:切换写入 ThemeStore,不写旧侧栏 localStorage', async () => {
    const { wrapper, themeStore } = await mountLayout()
    expect(themeStore.preferences.pcFunctionTreeCollapsed).toBe(false)
    await wrapper.get('[data-testid="function-tree-toggle"]').trigger('click')
    expect(themeStore.preferences.pcFunctionTreeCollapsed).toBe(true)
    expect(wrapper.get('nav.ip-function-tree').classes()).toContain('ip-function-tree--collapsed')
    expect(localStorage.getItem(LEGACY_COLLAPSED_KEY)).toBeNull()
    // 再点一次恢复展开
    await wrapper.get('[data-testid="function-tree-toggle"]').trigger('click')
    expect(themeStore.preferences.pcFunctionTreeCollapsed).toBe(false)
    expect(wrapper.find('nav.ip-function-tree .ip-function-tree__list').exists()).toBe(true)
  })

  it('无权限时功能树隐藏对应菜单(仅剩公开项)', async () => {
    const { wrapper } = await mountLayout([PERMISSIONS.platformHomeView])
    const systemButton = wrapper
      .findAll('nav.ip-toolrail button')
      .find((b) => b.attributes('aria-label') === '系统管理')!
    await systemButton.trigger('click')
    const labels = wrapper
      .findAll('nav.ip-function-tree .ip-function-tree__label')
      .map((n) => n.text())
    expect(labels).not.toContain('用户管理')
  })

  it('功能树链接可路由跳转:首页链接 href 指向 /pc/home', async () => {
    const { wrapper } = await mountLayout()
    const homeLink = wrapper.get('nav.ip-function-tree a')
    expect(homeLink.attributes('href')).toContain('/pc/home')
  })

  it('用户菜单命令 logout → 清理会话并跳转登录页', async () => {
    const { wrapper, router } = await mountLayout()
    const dropdown = wrapper.findComponent(ElDropdown)
    expect(dropdown.exists()).toBe(true)
    dropdown.vm.$emit('command', 'logout')
    await flushPromises()
    await router.isReady()
    expect(router.currentRoute.value.name).toBe('login')
    expect(useAuthStore().isAuthenticated).toBe(false)
  })

  it('工作区标签栏:固定工作台标签存在且不可关闭', async () => {
    const { wrapper } = await mountLayout()
    const nav = wrapper.get('nav.ip-pc-tabs')
    expect(nav.attributes('aria-label')).toBe('工作台标签')
    expect(nav.text()).toContain('工作台')
    expect(nav.findAll('.ip-pc-tabs__close')).toHaveLength(0)
  })

  it('关闭激活业务标签后确定性导航到固定工作台', async () => {
    const { wrapper, router } = await mountLayout()
    const tabsStore = useWorkspaceTabsStore()
    tabsStore.requestOpen(sandboxCandidate(1))
    await router.push('/pc/dev/workspace-tabs?slot=1')
    await nextTick()
    expect(tabsStore.activeTabId).toBe('sandbox:1')
    await wrapper.get('[aria-label="关闭 沙箱 1"]').trigger('click')
    await flushPromises()
    expect(tabsStore.activeTabId).toBe('pc-home')
    expect(router.currentRoute.value.name).toBe('pc-home')
  })

  it('重新加载:菜单命令递增激活标签 reloadVersion(驱动 RouterView key)', async () => {
    const { wrapper, router } = await mountLayout()
    const tabsStore = useWorkspaceTabsStore()
    tabsStore.requestOpen(sandboxCandidate(1))
    await router.push('/pc/dev/workspace-tabs?slot=1')
    await nextTick()
    const before = tabsStore.activeTab?.reloadVersion ?? 0
    const tabDropdown = wrapper.get('nav.ip-pc-tabs').findAllComponents(ElDropdown)[0]!
    await tabDropdown.vm.$emit('command', 'reload')
    await nextTick()
    expect(tabsStore.activeTab?.reloadVersion).toBe(before + 1)
    // 标签栏未关闭;RouterView 仍在渲染(重挂载由 contentKey 派生)
    expect(wrapper.findComponent(RouterView).exists()).toBe(true)
  })

  it('业务标签达上限:展示上限对话框,复用决议导航到复用标签', async () => {
    const { wrapper, router } = await mountLayout()
    const tabsStore = useWorkspaceTabsStore()
    for (let i = 0; i < 12; i += 1) tabsStore.requestOpen(sandboxCandidate(i))
    tabsStore.requestOpen(sandboxCandidate(12)) // 触发 pending
    await nextTick()
    const dialog = wrapper.findComponent(WorkspaceTabLimitDialog)
    expect(dialog.exists()).toBe(true)
    dialog.vm.$emit('resolve', { action: 'reuse', tabId: 'sandbox:0' })
    await flushPromises()
    expect(router.currentRoute.value.name).toBe('workspace-tabs-sandbox')
    expect(router.currentRoute.value.query.slot).toBe('0')
    expect(tabsStore.activeTabId).toBe('sandbox:0')
  })
})
