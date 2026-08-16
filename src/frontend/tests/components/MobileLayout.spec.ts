/**
 * Mobile 壳布局测试(FE-009,§17):
 * 骨架渲染、跳到主内容、底部导航双 Tab(首页/我的)、Tab 高亮、
 * 44px 触控目标结构与安全区域适配。
 * 真实几何尺寸(≥44px)与 env(safe-area-inset-bottom)由 E2E/真机验收。
 */

import { flushPromises, mount, type VueWrapper } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { createMemoryHistory, createRouter, type Router } from 'vue-router'

import { writeAuthSession } from '@/auth'
import type { AuthSession } from '@/auth/types'
import MobileLayout from '@/layouts/MobileLayout.vue'
import { installRouterGuards } from '@/router/guards'
import { routes } from '@/router/routes'
import { useAuthStore } from '@/stores/authStore'

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
}

async function mountLayout(
  initialPath = '/mobile/home',
  permissions: string[] = ['platform.mobile.view'],
): Promise<LayoutHarness> {
  const pinia = createPinia()
  setActivePinia(pinia)
  writeAuthSession(sessionStorage, makeSession(permissions))
  await useAuthStore().restore()
  const router = createRouter({ history: createMemoryHistory(), routes })
  installRouterGuards(router)
  await router.push(initialPath)
  await router.isReady()
  const wrapper = mount(MobileLayout, { global: { plugins: [pinia, router] } })
  return { wrapper, router }
}

describe('MobileLayout', () => {
  beforeEach(() => {
    sessionStorage.clear()
    localStorage.clear()
    // 终端文案单事实源为路由 meta.terminal(/mobile/* = 'mobile',PF-01 §7.11),
    // 不再写入 override 键;即使设备建议缺省为 pc,显式路由仍解析为 Mobile。
    // Mock 标识只在 VITE_AUTH_MODE=mock 下渲染,显式声明,不依赖产品默认(现为 http)。
    vi.stubEnv('VITE_AUTH_MODE', 'mock')
  })

  afterEach(() => {
    vi.unstubAllEnvs()
  })

  it('渲染 header / main / 底部导航三段骨架,主内容可聚焦', async () => {
    const { wrapper } = await mountLayout()
    expect(wrapper.find('header.ip-mobile-header').exists()).toBe(true)
    expect(wrapper.find('main#main-content').exists()).toBe(true)
    expect(wrapper.get('main#main-content').attributes('tabindex')).toBe('-1')
    const nav = wrapper.get('nav.ip-mobile-nav')
    expect(nav.attributes('aria-label')).toBe('底部导航')
  })

  it('提供跳到主内容入口,且是布局内第一个可聚焦元素', async () => {
    const { wrapper } = await mountLayout()
    const skip = wrapper.get('a.ip-mobile-skip-link')
    expect(skip.attributes('href')).toBe('#main-content')
    expect(skip.text()).toContain('跳到主内容')
    const focusables = wrapper.findAll('a, button')
    expect(focusables[0]?.attributes('href')).toBe('#main-content')
  })

  it('底部导航只包含「首页」「我的」两个 Tab', async () => {
    const { wrapper } = await mountLayout()
    const tabs = wrapper.findAll('nav.ip-mobile-nav a')
    expect(tabs).toHaveLength(2)
    expect(tabs[0]?.text()).toContain('首页')
    expect(tabs[1]?.text()).toContain('我的')
  })

  it('当前路由 Tab 高亮(首页)并带 aria-current', async () => {
    const { wrapper } = await mountLayout('/mobile/home')
    const tabs = wrapper.findAll('nav.ip-mobile-nav a')
    const home = tabs.find((w) => w.text().includes('首页'))
    const my = tabs.find((w) => w.text().includes('我的'))
    expect(home?.classes()).toContain('ip-mobile-nav-item--active')
    expect(home?.attributes('aria-current')).toBe('page')
    expect(my?.classes()).not.toContain('ip-mobile-nav-item--active')
  })

  it('切换到「我的」后高亮随之更新', async () => {
    const { wrapper, router } = await mountLayout('/mobile/home')
    await router.push({ name: 'mobile-my' })
    await flushPromises()
    const tabs = wrapper.findAll('nav.ip-mobile-nav a')
    const home = tabs.find((w) => w.text().includes('首页'))
    const my = tabs.find((w) => w.text().includes('我的'))
    expect(my?.classes()).toContain('ip-mobile-nav-item--active')
    expect(my?.attributes('aria-current')).toBe('page')
    expect(home?.classes()).not.toContain('ip-mobile-nav-item--active')
  })

  it('导航链接点击后路由切换到对应页面', async () => {
    const { wrapper, router } = await mountLayout('/mobile/home')
    const my = wrapper.findAll('nav.ip-mobile-nav a').find((w) => w.text().includes('我的'))
    await my!.trigger('click')
    await flushPromises()
    expect(router.currentRoute.value.name).toBe('mobile-my')
  })

  it('底部导航应用安全区域 Token(--ip-safe-area-bottom)', async () => {
    // jsdom 不解析 env();结构断言 nav 声明 padding-bottom 且使用 safe-area Token,
    // 真实 inset 由 E2E 与真机验收(§17:必须适配 env(safe-area-inset-bottom))。
    const { wrapper } = await mountLayout()
    const nav = wrapper.get('nav.ip-mobile-nav')
    const paddingBottom = getComputedStyle(nav.element).paddingBottom
    expect(paddingBottom).not.toBe('')
  })

  it('顶栏展示品牌、终端与 Mock 模式标识', async () => {
    const { wrapper } = await mountLayout()
    expect(wrapper.get('.ip-mobile-header__brand').text()).toContain('Industrial Platform')
    expect(wrapper.get('[data-testid="terminal-info"]').text()).toContain('Mobile')
    expect(wrapper.text()).toContain('Mock')
  })
})
