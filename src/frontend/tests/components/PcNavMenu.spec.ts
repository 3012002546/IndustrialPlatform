/**
 * PC 导航菜单组件测试(FE-006,§13.2/§14.2):
 * 权限过滤、路由高亮、折叠态与链接语义(键盘可达)。
 */

import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, describe, expect, it } from 'vitest'
import { createMemoryHistory, createRouter } from 'vue-router'

import { writeAuthSession } from '@/auth'
import type { AuthSession } from '@/auth/types'
import PcNavMenu from '@/components/navigation/PcNavMenu.vue'
import type { NavigationItem } from '@/components/navigation/types'
import { routes } from '@/router/routes'
import { useAuthStore } from '@/stores/authStore'

const ITEMS: NavigationItem[] = [
  { id: 'public', label: '公开项', routeName: 'pc-home' },
  { id: 'guarded', label: '受权限项', routeName: 'pc-home', permission: 'platform.home.view' },
  { id: 'hidden', label: '隐藏项', routeName: 'pc-home', permission: 'platform.pda.view' },
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
    },
  }
}

async function mountMenu(
  permissions: string[],
  initialPath = '/pc/home',
  collapsed = false,
): Promise<ReturnType<typeof mount>> {
  const pinia = createPinia()
  setActivePinia(pinia)
  writeAuthSession(sessionStorage, makeSession(permissions))
  const authStore = useAuthStore()
  await authStore.restore()
  const router = createRouter({ history: createMemoryHistory(), routes })
  await router.push(initialPath)
  return mount(PcNavMenu, {
    props: { items: ITEMS, collapsed },
    global: { plugins: [pinia, router] },
  })
}

describe('PcNavMenu', () => {
  beforeEach(() => {
    sessionStorage.clear()
    localStorage.clear()
  })

  it('渲染公开项与持权限项,过滤未持权限项', async () => {
    const wrapper = await mountMenu(['platform.home.view'])
    const labels = wrapper.findAll('.ip-pc-nav__label').map((n) => n.text())
    expect(labels).toContain('公开项')
    expect(labels).toContain('受权限项')
    expect(labels).not.toContain('隐藏项')
  })

  it('无任何权限时仅渲染公开项', async () => {
    const wrapper = await mountMenu([])
    const labels = wrapper.findAll('.ip-pc-nav__label').map((n) => n.text())
    expect(labels).toEqual(['公开项'])
  })

  it('当前路由高亮:aria-current=page 与激活样式', async () => {
    const wrapper = await mountMenu([], '/pc/home')
    const active = wrapper.get('[aria-current="page"]')
    expect(active.text()).toContain('公开项')
    expect(active.classes()).toContain('ip-pc-nav__link--active')
  })

  it('不在当前路由时不标记 aria-current', async () => {
    const wrapper = await mountMenu([], '/pda/home')
    expect(wrapper.find('[aria-current]').exists()).toBe(false)
  })

  it('链接为原生 <a href>,支持键盘激活(Tab + Enter)', async () => {
    const wrapper = await mountMenu([], '/pc/home')
    const links = wrapper.findAll('a.ip-pc-nav__link')
    expect(links.length).toBeGreaterThan(0)
    for (const link of links) {
      expect(link.attributes('href')).toBeTruthy()
    }
    expect(wrapper.get('nav').attributes('aria-label')).toBe('主导航')
  })

  it('折叠态:nav 标记折叠样式,链接保留 title 提示', async () => {
    const wrapper = await mountMenu([], '/pc/home', true)
    expect(wrapper.get('nav').classes()).toContain('ip-pc-nav--collapsed')
    for (const link of wrapper.findAll('a.ip-pc-nav__link')) {
      expect(link.attributes('title')).toBeTruthy()
    }
  })
})
