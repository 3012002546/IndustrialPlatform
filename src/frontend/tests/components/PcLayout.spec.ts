/**
 * PC 管理框架布局测试(FE-006,§14.1/§14.2):
 * 骨架渲染、跳到主内容、终端信息、Mock 横幅、用户菜单、展开/折叠、
 * 刷新保持(折叠持久化)、退出与路由高亮。
 */

import { flushPromises, mount, type VueWrapper } from '@vue/test-utils'
import { ElDropdown } from 'element-plus'
import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, describe, expect, it } from 'vitest'
import { createMemoryHistory, createRouter, type Router } from 'vue-router'

import { writeAuthSession } from '@/auth'
import type { AuthSession } from '@/auth/types'
import PcLayout from '@/layouts/PcLayout.vue'
import { routes } from '@/router/routes'
import { useAuthStore } from '@/stores/authStore'

const COLLAPSED_KEY = 'industrial-platform.pc.sidebar.collapsed.v1'

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

async function mountLayout(permissions: string[] = ['platform.home.view']): Promise<LayoutHarness> {
  const pinia = createPinia()
  setActivePinia(pinia)
  writeAuthSession(sessionStorage, makeSession(permissions))
  const authStore = useAuthStore()
  await authStore.restore()
  const router = createRouter({ history: createMemoryHistory(), routes })
  await router.push('/pc/home')
  const wrapper = mount(PcLayout, { global: { plugins: [pinia, router] } })
  return { wrapper, router }
}

describe('PcLayout', () => {
  beforeEach(() => {
    sessionStorage.clear()
    localStorage.clear()
  })

  it('渲染 header / sidebar / main 三段骨架', async () => {
    const { wrapper } = await mountLayout()
    expect(wrapper.find('header.ip-pc-header').exists()).toBe(true)
    expect(wrapper.find('aside#ip-pc-sidebar').exists()).toBe(true)
    expect(wrapper.find('main#main-content').exists()).toBe(true)
    expect(wrapper.get('main#main-content').attributes('tabindex')).toBe('-1')
  })

  it('提供跳到主内容入口,且是布局内第一个可聚焦元素', async () => {
    const { wrapper } = await mountLayout()
    const skip = wrapper.get('a.ip-pc-skip-link')
    expect(skip.attributes('href')).toBe('#main-content')
    expect(skip.text()).toContain('跳到主内容')
    const focusables = wrapper.findAll('a, button')
    expect(focusables[0]?.attributes('href')).toBe('#main-content')
  })

  it('顶栏展示品牌名', async () => {
    const { wrapper } = await mountLayout()
    expect(wrapper.get('.ip-pc-brand').text()).toContain('Industrial Platform')
  })

  it('顶栏展示终端信息', async () => {
    const { wrapper } = await mountLayout()
    expect(wrapper.get('[data-testid="terminal-info"]').text()).toContain('PC')
  })

  it('顶栏展示 Mock 模式横幅', async () => {
    const { wrapper } = await mountLayout()
    expect(wrapper.text()).toContain('开发 Mock 模式')
  })

  it('用户菜单显示当前用户 displayName', async () => {
    const { wrapper } = await mountLayout()
    expect(wrapper.get('[data-testid="user-menu"]').text()).toContain('Mock 演示账号')
  })

  it('侧栏默认展开;点击折叠按钮进入折叠态并持久化', async () => {
    const { wrapper } = await mountLayout()
    expect(wrapper.get('aside#ip-pc-sidebar').classes()).not.toContain('ip-pc-sidebar--collapsed')
    await wrapper.get('[data-testid="sidebar-toggle"]').trigger('click')
    expect(wrapper.get('aside#ip-pc-sidebar').classes()).toContain('ip-pc-sidebar--collapsed')
    expect(localStorage.getItem(COLLAPSED_KEY)).toBe('1')
    // 再点一次展开
    await wrapper.get('[data-testid="sidebar-toggle"]').trigger('click')
    expect(localStorage.getItem(COLLAPSED_KEY)).toBe('0')
  })

  it('刷新保持:折叠状态跨实例从 localStorage 恢复', async () => {
    localStorage.setItem(COLLAPSED_KEY, '1')
    const { wrapper } = await mountLayout()
    expect(wrapper.get('aside#ip-pc-sidebar').classes()).toContain('ip-pc-sidebar--collapsed')
    const again = await mountLayout()
    expect(again.wrapper.get('aside#ip-pc-sidebar').classes()).toContain('ip-pc-sidebar--collapsed')
  })

  it('折叠按钮暴露 aria-expanded 状态', async () => {
    const { wrapper } = await mountLayout()
    expect(wrapper.get('[data-testid="sidebar-toggle"]').attributes('aria-expanded')).toBe('true')
    await wrapper.get('[data-testid="sidebar-toggle"]').trigger('click')
    expect(wrapper.get('[data-testid="sidebar-toggle"]').attributes('aria-expanded')).toBe('false')
  })

  it('导航菜单渲染当前路由高亮项', async () => {
    const { wrapper } = await mountLayout(['platform.home.view'])
    const active = wrapper.get('[aria-current="page"]')
    expect(active.text()).toContain('首页')
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
})
