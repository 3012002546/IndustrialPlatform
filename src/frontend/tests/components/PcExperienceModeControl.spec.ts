import { mount } from '@vue/test-utils'
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
})
