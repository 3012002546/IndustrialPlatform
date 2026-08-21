/**
 * PlatformFunctionTree 组件测试(PF-01 §6.3):
 * 授权过滤、路由高亮、收起状态来自 ThemeStore、切换持久化、
 * 折叠时隐藏列表且工具轨仍保留、本组件不直接读写 localStorage。
 */

import { mount, type VueWrapper } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { createMemoryHistory, createRouter, type Router } from 'vue-router'
import { defineComponent, nextTick } from 'vue'

import { writeAuthSession } from '@/auth'
import type { AuthSession } from '@/auth/types'
import type { NavigationItem } from '@/components/navigation/types'
import PlatformFunctionTree from '@/components/shell/PlatformFunctionTree.vue'
import { routes } from '@/router/routes'
import { useAuthStore } from '@/stores/authStore'
import { useThemeStore } from '@/stores/themeStore'

const ItemIcon = defineComponent({ name: 'ItemIcon', template: '<span>■</span>' })

const ITEMS: readonly NavigationItem[] = [
  { id: 'public', label: '公开项', routeName: 'pc-home', icon: ItemIcon },
  {
    id: 'guarded',
    label: '受权限项',
    routeName: 'pc-home',
    permission: 'platform.home.view',
    icon: ItemIcon,
  },
  {
    id: 'hidden',
    label: '隐藏项',
    routeName: 'pc-home',
    permission: 'platform.pda.view',
    icon: ItemIcon,
  },
]

const NESTED_ITEMS: readonly NavigationItem[] = [
  {
    id: 'parent',
    label: '父级菜单',
    routeName: 'pc-home',
    icon: ItemIcon,
    children: [{ id: 'child', label: '子级菜单', routeName: 'pc-home', icon: ItemIcon }],
  },
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

interface Harness {
  wrapper: VueWrapper
  router: Router
  themeStore: ReturnType<typeof useThemeStore>
}

async function mountTree(
  permissions: string[],
  initialPath = '/pc/home',
  label = '系统管理',
  items: readonly NavigationItem[] = ITEMS,
): Promise<Harness> {
  const pinia = createPinia()
  setActivePinia(pinia)
  writeAuthSession(sessionStorage, makeSession(permissions))
  const authStore = useAuthStore()
  await authStore.restore()
  const themeStore = useThemeStore()
  await themeStore.initialize()
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
  const router = createRouter({ history: createMemoryHistory(), routes })
  await router.push(initialPath)
  const wrapper = mount(PlatformFunctionTree, {
    props: { label, items },
    global: { plugins: [pinia, router] },
  })
  return { wrapper, router, themeStore }
}

describe('PlatformFunctionTree', () => {
  beforeEach(() => {
    sessionStorage.clear()
    localStorage.clear()
    // 经 writeAuthSession 写入 Mock 会话键后 restore,显式声明 mock,不依赖产品默认(现为 http)。
    vi.stubEnv('VITE_AUTH_MODE', 'mock')
  })

  afterEach(() => {
    vi.unstubAllEnvs()
  })

  it('渲染标签与公开/持权限项,过滤未持权限项', async () => {
    const { wrapper } = await mountTree(['platform.home.view'])
    const labels = wrapper.findAll('.ip-function-tree__label').map((n) => n.text())
    expect(wrapper.get('nav').attributes('aria-label')).toBe('系统管理')
    expect(labels).toContain('公开项')
    expect(labels).toContain('受权限项')
    expect(labels).not.toContain('隐藏项')
  })

  it('无任何权限时仅渲染公开项', async () => {
    const { wrapper } = await mountTree([])
    const labels = wrapper.findAll('.ip-function-tree__label').map((n) => n.text())
    expect(labels).toEqual(['公开项'])
  })

  it('当前路由高亮:aria-current=page 与激活样式', async () => {
    const { wrapper } = await mountTree([], '/pc/home')
    const active = wrapper.get('[aria-current="page"]')
    expect(active.text()).toContain('公开项')
    expect(active.classes()).toContain('ip-function-tree__link--active')
  })

  it('链接为原生 <a href>,支持键盘激活', async () => {
    const { wrapper } = await mountTree([], '/pc/home')
    const links = wrapper.findAll('a.ip-function-tree__link')
    expect(links.length).toBeGreaterThan(0)
    for (const link of links) {
      expect(link.attributes('href')).toBeTruthy()
    }
  })

  it('默认展开;点击切换把收起状态写入 ThemeStore 而非 localStorage', async () => {
    const { wrapper, themeStore } = await mountTree([])
    expect(themeStore.preferences.pcFunctionTreeCollapsed).toBe(false)
    await wrapper.get('[data-testid="function-tree-toggle"]').trigger('click')
    expect(themeStore.preferences.pcFunctionTreeCollapsed).toBe(true)
    // 本组件不直接读写旧侧栏键
    expect(localStorage.getItem('industrial-platform.pc.sidebar.collapsed.v1')).toBeNull()
    // 收起后列表仍保留图标入口,aria-expanded=false
    expect(wrapper.find('#ip-function-tree-list').exists()).toBe(true)
    expect(wrapper.find('.ip-function-tree__label').exists()).toBe(false)
    expect(wrapper.get('[data-testid="function-tree-toggle"]').attributes('aria-expanded')).toBe(
      'false',
    )
  })

  it('从 ThemeStore 读取收起初始状态:store 折叠时列表不渲染', async () => {
    const { themeStore, wrapper } = await mountTree([])
    themeStore.setPcFunctionTreeCollapsed(true)
    await nextTick()
    expect(wrapper.find('#ip-function-tree-list').exists()).toBe(true)
    expect(wrapper.find('.ip-function-tree__label').exists()).toBe(false)
    expect(wrapper.get('nav').classes()).toContain('ip-function-tree--collapsed')
    expect(wrapper.get('[data-testid="function-tree-toggle"]').attributes('title')).toBe(
      '展开功能树',
    )
  })

  it('再点一次展开恢复列表与 aria-expanded=true', async () => {
    const { wrapper, themeStore } = await mountTree([])
    await wrapper.get('[data-testid="function-tree-toggle"]').trigger('click')
    await wrapper.get('[data-testid="function-tree-toggle"]').trigger('click')
    expect(themeStore.preferences.pcFunctionTreeCollapsed).toBe(false)
    expect(wrapper.find('#ip-function-tree-list').exists()).toBe(true)
    expect(wrapper.get('[data-testid="function-tree-toggle"]').attributes('aria-expanded')).toBe(
      'true',
    )
  })

  it('收起时仍保留授权菜单图标并可直接点击跳转', async () => {
    const { wrapper, router, themeStore } = await mountTree(['platform.home.view'])
    themeStore.setPcFunctionTreeCollapsed(true)
    await nextTick()

    const link = wrapper.get('a.ip-function-tree__link')
    expect(link.attributes('aria-label')).toBe('公开项')
    expect(link.attributes('title')).toBe('公开项')
    expect(link.find('.ip-function-tree__label').exists()).toBe(false)
    expect(link.find('.ip-function-tree__icon').exists()).toBe(true)
    await link.trigger('click')
    await router.isReady()
    expect(router.currentRoute.value.name).toBe('pc-home')
  })

  it('收起时有子菜单的图标打开键盘可达浮层', async () => {
    const { wrapper, themeStore } = await mountTree([], '/pc/home', '系统管理', NESTED_ITEMS)
    themeStore.setPcFunctionTreeCollapsed(true)
    await nextTick()

    const trigger = wrapper.get('[data-testid="function-tree-parent-parent"]')
    expect(trigger.attributes('aria-haspopup')).toBe('menu')
    expect(trigger.attributes('aria-expanded')).toBe('false')
    // 原生 button 的 Enter/Space 由浏览器转换为 click；VTU 不模拟该默认行为。
    await trigger.trigger('click')

    expect(trigger.attributes('aria-expanded')).toBe('true')
    expect(wrapper.get('[data-testid="function-tree-popover-parent"]').text()).toContain('子级菜单')
    expect(
      wrapper.get('[data-testid="function-tree-popover-parent"] [role="menuitem"]'),
    ).toBeTruthy()
  })
})
