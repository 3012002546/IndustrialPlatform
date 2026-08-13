/**
 * PDA 现场壳布局测试(FE-008,§16):
 * 骨架渲染、跳到主内容、返回(有/无历史)、首页、退出、
 * 当前用户/终端/Mock 标识与 48px 触控目标结构。
 * 真实几何尺寸(≥48px)由 E2E bounding box 验收。
 */

import { flushPromises, mount, type VueWrapper } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createMemoryHistory, createRouter, createWebHistory, type Router } from 'vue-router'

import { writeAuthSession } from '@/auth'
import type { AuthSession } from '@/auth/types'
import PdaLayout from '@/layouts/PdaLayout.vue'
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
    },
  }
}

interface LayoutHarness {
  wrapper: VueWrapper
  router: Router
}

async function mountLayout(permissions: string[] = ['platform.pda.view']): Promise<LayoutHarness> {
  const pinia = createPinia()
  setActivePinia(pinia)
  writeAuthSession(sessionStorage, makeSession(permissions))
  await useAuthStore().restore()
  const router = createRouter({ history: createMemoryHistory(), routes })
  installRouterGuards(router)
  await router.push('/pda/home')
  await router.isReady()
  const wrapper = mount(PdaLayout, { global: { plugins: [pinia, router] } })
  return { wrapper, router }
}

describe('PdaLayout', () => {
  beforeEach(() => {
    sessionStorage.clear()
    localStorage.clear()
    // 终端文案单事实源为路由 meta.terminal(/pda/home = 'pda',PF-01 §7.11),
    // 不再写入 override 键;即使设备建议缺省为 pc,显式路由仍解析为 PDA。
  })

  it('渲染 header / main 骨架,主内容可聚焦', async () => {
    const { wrapper } = await mountLayout()
    expect(wrapper.find('header.ip-pda-header').exists()).toBe(true)
    expect(wrapper.find('main#main-content').exists()).toBe(true)
    expect(wrapper.get('main#main-content').attributes('tabindex')).toBe('-1')
  })

  it('提供跳到主内容入口,且是布局内第一个可聚焦元素', async () => {
    const { wrapper } = await mountLayout()
    const skip = wrapper.get('a.ip-pda-skip-link')
    expect(skip.attributes('href')).toBe('#main-content')
    expect(skip.text()).toContain('跳到主内容')
    const focusables = wrapper.findAll('a, button')
    expect(focusables[0]?.attributes('href')).toBe('#main-content')
  })

  it('顶栏提供返回 / 首页 / 退出三个 48px 触控入口', async () => {
    const { wrapper } = await mountLayout()
    for (const testid of ['back-button', 'home-button', 'logout-button']) {
      const button = wrapper.get(`[data-testid="${testid}"]`)
      expect(button.classes()).toContain('ip-pda-icon-button')
    }
    expect(wrapper.get('[data-testid="back-button"]').attributes('aria-label')).toBe('返回')
    expect(wrapper.get('[data-testid="home-button"]').attributes('aria-label')).toBe('首页')
    expect(wrapper.get('[data-testid="logout-button"]').attributes('aria-label')).toBe('退出登录')
  })

  it('顶栏展示当前用户、终端与 Mock 模式标识', async () => {
    const { wrapper } = await mountLayout()
    expect(wrapper.get('[data-testid="pda-user"]').text()).toContain('Mock 演示账号')
    expect(wrapper.get('[data-testid="terminal-info"]').text()).toContain('PDA')
    expect(wrapper.text()).toContain('Mock')
  })

  it('返回按钮:无历史记录时回落 PDA 首页(不误退出应用)', async () => {
    // memory history 的 state 恒无 back 字段 → canGoBack()=false,回落首页
    const { wrapper, router } = await mountLayout()
    await wrapper.get('[data-testid="back-button"]').trigger('click')
    await flushPromises()
    expect(router.currentRoute.value.name).toBe('pda-home')
  })

  it('返回按钮:有历史时返回上一页(web history,与生产一致)', async () => {
    // jsdom 真实 web history 携带 back 字段;上一页用真实受保护路由 /pc/home,
    // popstate 为宏任务,waitFor 轮询导航完成。
    const pinia = createPinia()
    setActivePinia(pinia)
    writeAuthSession(sessionStorage, makeSession(['platform.home.view', 'platform.pda.view']))
    await useAuthStore().restore()
    const router = createRouter({ history: createWebHistory(), routes })
    installRouterGuards(router)
    await router.push('/pc/home')
    await router.push('/pda/home')
    await router.isReady()
    const wrapper = mount(PdaLayout, { global: { plugins: [pinia, router] } })
    expect(router.currentRoute.value.name).toBe('pda-home')
    await wrapper.get('[data-testid="back-button"]').trigger('click')
    await vi.waitFor(() => {
      expect(router.currentRoute.value.fullPath).toBe('/pc/home')
    })
  })

  it('首页按钮导航到 PDA 首页', async () => {
    const { wrapper, router } = await mountLayout()
    await wrapper.get('[data-testid="home-button"]').trigger('click')
    await flushPromises()
    expect(router.currentRoute.value.name).toBe('pda-home')
  })

  it('退出 → 清理会话并跳转登录页', async () => {
    const { wrapper, router } = await mountLayout()
    await wrapper.get('[data-testid="logout-button"]').trigger('click')
    await flushPromises()
    await router.isReady()
    expect(router.currentRoute.value.name).toBe('login')
    expect(useAuthStore().isAuthenticated).toBe(false)
  })
})
