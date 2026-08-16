/**
 * Mobile「我的」页面组件测试(FE-009,§17):
 * 展示当前用户信息(displayName/username/roles)与退出入口;
 * 退出清理会话并跳转登录页,不显示伪造数据。
 */

import { flushPromises, mount, type VueWrapper } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { createMemoryHistory, createRouter, type Router } from 'vue-router'

import { persistAuthSession } from '../fixtures/session'
import MobileMyPage from '@/pages/mobile/MobileMyPage.vue'
import { routes } from '@/router/routes'
import { useAuthStore } from '@/stores/authStore'

interface MyHarness {
  wrapper: VueWrapper
  router: Router
}

async function mountMy(permissions: string[] = ['platform.mobile.view']): Promise<MyHarness> {
  const pinia = createPinia()
  setActivePinia(pinia)
  persistAuthSession(permissions)
  await useAuthStore().restore()
  const router = createRouter({ history: createMemoryHistory(), routes })
  await router.push('/mobile/my')
  await router.isReady()
  const wrapper = mount(MobileMyPage, { global: { plugins: [pinia, router] } })
  return { wrapper, router }
}

describe('MobileMyPage', () => {
  beforeEach(() => {
    sessionStorage.clear()
    localStorage.clear()
    // 页面测试经 persistAuthSession 写入 Mock 会话键,显式声明 mock,不依赖产品默认(现为 http)。
    vi.stubEnv('VITE_AUTH_MODE', 'mock')
  })

  afterEach(() => {
    vi.unstubAllEnvs()
  })

  it('展示当前用户 displayName / username / roles', async () => {
    const { wrapper } = await mountMy()
    expect(wrapper.get('[data-testid="display-name"]').text()).toContain('Mock 演示账号')
    expect(wrapper.get('[data-testid="username"]').text()).toBe('mock.admin')
    expect(wrapper.get('[data-testid="roles"]').text()).toContain('admin')
  })

  it('页面标题为「我的」', async () => {
    const { wrapper } = await mountMy()
    expect(wrapper.get('h1').text()).toBe('我的')
  })

  it('不显示伪造的生产数据', async () => {
    const { wrapper } = await mountMy()
    expect(wrapper.text()).not.toMatch(/\d+(\.\d+)?\s*%/)
  })

  it('退出按钮 → 清理会话并跳转登录页', async () => {
    const { wrapper, router } = await mountMy()
    await wrapper.get('[data-testid="logout-button"]').trigger('click')
    await flushPromises()
    await router.isReady()
    expect(router.currentRoute.value.name).toBe('login')
    expect(useAuthStore().isAuthenticated).toBe(false)
  })
})
