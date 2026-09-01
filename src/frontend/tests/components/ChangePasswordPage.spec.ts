import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, describe, expect, it } from 'vitest'
import { createMemoryHistory, createRouter } from 'vue-router'

import ChangePasswordPage from '@/pages/public/ChangePasswordPage.vue'
import { useLocalizationStore } from '@/stores/localizationStore'

async function mountChangePassword(locale: 'zh-CN' | 'en-US') {
  const pinia = createPinia()
  setActivePinia(pinia)
  useLocalizationStore().setLocale(locale, null)
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [{ path: '/change-password', component: ChangePasswordPage }],
  })
  await router.push('/change-password')
  await router.isReady()
  return mount(ChangePasswordPage, { global: { plugins: [pinia, router] } })
}

describe('ChangePasswordPage', () => {
  beforeEach(() => {
    sessionStorage.clear()
    localStorage.clear()
  })

  it('renders localized Chinese copy', async () => {
    const wrapper = await mountChangePassword('zh-CN')

    expect(wrapper.get('h1').text()).toBe('修改密码')
    expect(wrapper.get('label[for="ip-change-current-password"]').text()).toBe('当前密码')
    expect(wrapper.get('[data-testid="change-submit"]').text()).toBe('修改密码')
  })

  it('renders localized English copy without Chinese page chrome', async () => {
    const wrapper = await mountChangePassword('en-US')

    expect(wrapper.get('h1').text()).toBe('Change password')
    expect(wrapper.get('label[for="ip-change-current-password"]').text()).toBe('Current password')
    expect(wrapper.get('label[for="ip-change-new-password"]').text()).toBe('New password')
    expect(wrapper.get('label[for="ip-change-confirm-password"]').text()).toBe(
      'Confirm new password',
    )
    expect(wrapper.get('[data-testid="change-submit"]').text()).toBe('Change password')
    expect(wrapper.get('[data-testid="change-logout"]').text()).toBe('Sign out')
    expect(wrapper.text()).not.toContain('修改密码')
    expect(wrapper.text()).not.toContain('退出登录')
  })
})
