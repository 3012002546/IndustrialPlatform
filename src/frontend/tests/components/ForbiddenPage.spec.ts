/**
 * 403 页面组件测试(FE-007,§15.3):
 * 返回有权限首页(优先当前终端)、重新登录、TraceId 仅在有值时展示。
 */

import { flushPromises, mount, type VueWrapper } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { createMemoryHistory, createRouter, type Router } from 'vue-router'

import { persistAuthSession } from '../fixtures/session'
import ForbiddenPage from '@/pages/public/ForbiddenPage.vue'
import { routes } from '@/router/routes'
import { useAuthStore } from '@/stores/authStore'

interface ForbiddenHarness {
  wrapper: VueWrapper
  router: Router
}

async function mountForbidden(
  permissions: string[],
  query: Record<string, string> = {},
): Promise<ForbiddenHarness> {
  const pinia = createPinia()
  setActivePinia(pinia)
  persistAuthSession(permissions)
  const authStore = useAuthStore()
  await authStore.restore()
  const router = createRouter({ history: createMemoryHistory(), routes })
  await router.push({ path: '/403', query })
  await router.isReady()
  const wrapper = mount(ForbiddenPage, { global: { plugins: [pinia, router] } })
  return { wrapper, router }
}

describe('ForbiddenPage', () => {
  beforeEach(() => {
    sessionStorage.clear()
    localStorage.clear()
    // 页面测试经 persistAuthSession 写入 Mock 会话键,显式声明 mock,不依赖产品默认(现为 http)。
    vi.stubEnv('VITE_AUTH_MODE', 'mock')
  })

  afterEach(() => {
    vi.unstubAllEnvs()
  })

  it('渲染页面标题与统一错误文案', async () => {
    const { wrapper } = await mountForbidden([])
    expect(wrapper.get('h1').text()).toBe('无权限')
    expect(wrapper.text()).toContain('无权访问')
  })

  it('TraceId 仅在查询参数提供时展示', async () => {
    const noTrace = await mountForbidden([])
    expect(noTrace.wrapper.text()).not.toContain('TraceId')

    const withTrace = await mountForbidden([], { traceId: 'trace-123' })
    expect(withTrace.wrapper.text()).toContain('TraceId: trace-123')
  })

  it('返回有权限首页:优先当前终端首页', async () => {
    const { wrapper, router } = await mountForbidden(['platform.home.view'])
    await wrapper.get('[data-testid="go-home"]').trigger('click')
    await flushPromises()
    expect(router.currentRoute.value.name).toBe('pc-home')
  })

  it('当前终端首页无权限时回退到任一有权限终端首页', async () => {
    const { wrapper, router } = await mountForbidden(['platform.pda.view'])
    await wrapper.get('[data-testid="go-home"]').trigger('click')
    await flushPromises()
    expect(router.currentRoute.value.name).toBe('pda-home')
  })

  it('没有任何终端权限时隐藏「返回有权限首页」', async () => {
    const { wrapper } = await mountForbidden([])
    expect(wrapper.findAll('[data-testid="go-home"]')).toHaveLength(0)
    expect(wrapper.find('[data-testid="relogin"]').exists()).toBe(true)
  })

  it('重新登录:清理会话并跳转登录页', async () => {
    const { wrapper, router } = await mountForbidden(['platform.home.view'])
    await wrapper.get('[data-testid="relogin"]').trigger('click')
    await flushPromises()
    expect(useAuthStore().isAuthenticated).toBe(false)
    expect(router.currentRoute.value.name).toBe('login')
  })
})
