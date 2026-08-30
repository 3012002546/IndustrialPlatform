import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { createMemoryHistory, createRouter } from 'vue-router'

import { writeAuthSession } from '@/auth'
import ProfilePage from '@/pages/pc/ProfilePage.vue'
import { routes } from '@/router/routes'
import { useAuthStore } from '@/stores/authStore'

describe('ProfilePage', () => {
  let pinia: ReturnType<typeof createPinia>

  afterEach(() => vi.unstubAllEnvs())

  beforeEach(async () => {
    vi.stubEnv('VITE_AUTH_MODE', 'mock')
    sessionStorage.clear()
    pinia = createPinia()
    setActivePinia(pinia)
    writeAuthSession(sessionStorage, {
      accessToken: 'access',
      refreshToken: 'refresh',
      expiresAt: new Date(Date.now() + 60_000).toISOString(),
      user: {
        userId: 'u1',
        username: 'profile.user',
        displayName: 'Profile User',
        tenantId: 'tenant-1',
        roles: ['operator'],
        permissions: ['platform.home.view'],
        mustChangePassword: false,
      },
    })
    await useAuthStore().restore()
  })

  it('shows the authenticated user snapshot and reuses the change-password route', async () => {
    const router = createRouter({ history: createMemoryHistory(), routes })
    await router.push('/pc/profile')
    await router.isReady()
    const wrapper = mount(ProfilePage, { global: { plugins: [pinia, router] } })

    expect(wrapper.text()).toContain('profile.user')
    expect(wrapper.text()).toContain('Profile User')
    expect(wrapper.text()).toContain('tenant-1')
    expect(wrapper.text()).toContain('operator')

    await wrapper.get('button').trigger('click')
    await flushPromises()
    expect(router.currentRoute.value.name).toBe('change-password')
  })
})
