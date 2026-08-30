import { flushPromises, mount } from '@vue/test-utils'
import { h } from 'vue'
import { createMemoryHistory, createRouter } from 'vue-router'
import { createPinia, setActivePinia } from 'pinia'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import { writeAuthSession } from '@/auth'
import type { AuthSession } from '@/auth/types'
import PcExperienceModeControl from '@/components/shell/PcExperienceModeControl.vue'
import { useAuthStore } from '@/stores/authStore'

function session(permissions: string[]): AuthSession {
  return {
    accessToken: 'access',
    refreshToken: 'refresh',
    expiresAt: new Date(Date.now() + 60_000).toISOString(),
    user: {
      userId: 'u1',
      username: 'operator',
      displayName: '操作用户',
      tenantId: 't1',
      roles: ['operator'],
      permissions,
      mustChangePassword: false,
    },
  }
}

describe('PcExperienceModeControl', () => {
  beforeEach(async () => {
    vi.stubEnv('VITE_AUTH_MODE', 'mock')
    setActivePinia(createPinia())
    sessionStorage.clear()
    writeAuthSession(sessionStorage, session(['platform.home.view', 'platform.operation.view']))
    await useAuthStore().restore()
  })

  afterEach(() => vi.unstubAllEnvs())

  it('只有同时拥有两项模式权限时显示切换控件', () => {
    const wrapper = mount(PcExperienceModeControl)
    expect(wrapper.get('[data-testid="pc-experience-mode-control"]')).toBeTruthy()
  })

  it('单模式授权时不显示控件', async () => {
    setActivePinia(createPinia())
    writeAuthSession(sessionStorage, session(['platform.operation.view']))
    await useAuthStore().restore()
    const wrapper = mount(PcExperienceModeControl)
    expect(wrapper.find('[data-testid="pc-experience-mode-control"]').exists()).toBe(false)
  })

  it('从管理业务页切换后返回时恢复原路由、查询与用户管理页签', async () => {
    localStorage.clear()
    const pinia = createPinia()
    setActivePinia(pinia)
    writeAuthSession(
      sessionStorage,
      session(['platform.home.view', 'platform.operation.view', 'identity.user.view']),
    )
    await useAuthStore().restore()
    const router = createRouter({
      history: createMemoryHistory(),
      routes: [
        { path: '/pc/home', name: 'pc-home', component: { render: () => h('div', 'home') } },
        {
          path: '/pc/operation',
          name: 'pc-operation',
          component: { render: () => h('div', 'operation') },
          meta: {
            title: 'Production operation',
            requiresAuth: true,
            terminal: 'pc',
            experience: 'operation',
          },
        },
        {
          path: '/pc/identity/users',
          name: 'identity-users',
          component: { render: () => h('div', 'users') },
          meta: {
            title: 'Users',
            requiresAuth: true,
            terminal: 'pc',
            permission: 'identity.user.view',
          },
        },
      ],
    })
    await router.push({ name: 'identity-users', query: { loginName: 'e2e.admin' } })
    await router.isReady()
    const wrapper = mount(PcExperienceModeControl, {
      global: { plugins: [router, pinia] },
    })

    await wrapper.findAll('button')[1]!.trigger('click')
    await flushPromises()
    expect(router.currentRoute.value.name).toBe('pc-operation')

    await wrapper.findAll('button')[0]!.trigger('click')
    await flushPromises()
    expect(router.currentRoute.value.name).toBe('identity-users')
    expect(router.currentRoute.value.query).toEqual({ loginName: 'e2e.admin' })
  })
})
