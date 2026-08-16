/**
 * 登录页组件测试(FE-007,§15.1):
 * 必填校验、错误关联、密码显隐、提交防重、统一错误、安全 redirect、
 * Mock 横幅/演示账号提示与 password 不入存储。
 */

import { flushPromises, mount, type VueWrapper } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { createMemoryHistory, createRouter, type Router } from 'vue-router'

import { AUTH_SESSION_STORAGE_KEY, createMockAuthGateway, setAuthGateway } from '@/auth'
import type { AuthGateway } from '@/auth/types'
import LoginPage from '@/pages/public/LoginPage.vue'
import { routes } from '@/router/routes'
import { useAuthStore } from '@/stores/authStore'

const VALID_LOGIN = { username: 'mock.admin', password: 'Mock@123456' }

interface LoginHarness {
  wrapper: VueWrapper
  router: Router
  gateway: AuthGateway
}

async function mountLogin(
  query: Record<string, string> = {},
  gateway: AuthGateway = createMockAuthGateway({ delayMs: 0 }),
): Promise<LoginHarness> {
  const pinia = createPinia()
  setActivePinia(pinia)
  setAuthGateway(gateway)
  const router = createRouter({ history: createMemoryHistory(), routes })
  await router.push({ path: '/login', query })
  await router.isReady()
  const wrapper = mount(LoginPage, { global: { plugins: [pinia, router] } })
  return { wrapper, router, gateway }
}

/** jsdom 分离 DOM 中点击 submit 按钮不会触发原生表单提交,直接派发 submit 事件。 */
async function fillAndSubmit(
  wrapper: VueWrapper,
  username: string,
  password: string,
): Promise<void> {
  await wrapper.get('[data-testid="login-username"]').setValue(username)
  await wrapper.get('[data-testid="login-password"]').setValue(password)
  await wrapper.find('form').trigger('submit')
  await flushPromises()
}

describe('LoginPage', () => {
  beforeEach(() => {
    sessionStorage.clear()
    localStorage.clear()
    // 本套件验证 Mock 登录行为(横幅/演示账号提示),显式声明 mock,不依赖产品默认(现为 http)。
    vi.stubEnv('VITE_AUTH_MODE', 'mock')
  })

  afterEach(() => {
    vi.unstubAllEnvs()
  })

  it('渲染用户名/密码输入、标签与提交按钮,并展示 Mock 横幅与演示账号提示', async () => {
    const { wrapper } = await mountLogin()
    expect(wrapper.get('label[for="ip-login-username"]').text()).toBe('用户名')
    expect(wrapper.get('label[for="ip-login-password"]').text()).toBe('密码')
    expect(wrapper.get('[data-testid="login-username"]').attributes('autocomplete')).toBe(
      'username',
    )
    expect(wrapper.get('[data-testid="login-password"]').attributes('autocomplete')).toBe(
      'current-password',
    )
    expect(wrapper.text()).toContain('开发 Mock 模式')
    expect(wrapper.text()).toContain('mock.admin / Mock@123456')
  })

  it('空提交:显示必填错误且不调用网关;错误与输入项关联(aria-describedby)', async () => {
    const { wrapper, gateway } = await mountLogin()
    const loginSpy = vi.spyOn(gateway, 'login')
    await fillAndSubmit(wrapper, '', '')
    expect(wrapper.get('[role="alert"]').text()).toContain('请输入用户名')
    expect(wrapper.text()).toContain('请输入密码')
    expect(loginSpy).not.toHaveBeenCalled()
    const username = wrapper.get('[data-testid="login-username"]')
    expect(username.attributes('aria-invalid')).toBe('true')
    expect(username.attributes('aria-describedby')).toContain('ip-login-username-error')
    expect(wrapper.get('#ip-login-username-error').text()).toBe('请输入用户名')
  })

  it('密码显隐切换:默认 password,点击切换为 text 并更新 aria-pressed', async () => {
    const { wrapper } = await mountLogin()
    const password = wrapper.get('[data-testid="login-password"]')
    expect(password.element.getAttribute('type')).toBe('password')
    const toggle = wrapper.get('[data-testid="password-toggle"]')
    expect(toggle.attributes('aria-pressed')).toBe('false')
    await toggle.trigger('click')
    expect(password.element.getAttribute('type')).toBe('text')
    expect(toggle.attributes('aria-pressed')).toBe('true')
  })

  it('错误账号:显示统一错误文案并停留在登录页', async () => {
    const { wrapper, router } = await mountLogin()
    await fillAndSubmit(wrapper, 'mock.admin', 'wrong-password')
    expect(wrapper.text()).toContain('用户名或密码错误')
    expect(router.currentRoute.value.name).toBe('login')
  })

  it('登录成功后跳转站内安全 redirect', async () => {
    const { wrapper, router } = await mountLogin({ redirect: '/pc/home' })
    await fillAndSubmit(wrapper, VALID_LOGIN.username, VALID_LOGIN.password)
    expect(router.currentRoute.value.fullPath).toBe('/pc/home')
  })

  it('不安全的 redirect(协议相对)被拒绝,回落到根路由', async () => {
    const { wrapper, router } = await mountLogin({ redirect: '//evil.example/steal' })
    await fillAndSubmit(wrapper, VALID_LOGIN.username, VALID_LOGIN.password)
    expect(router.currentRoute.value.name).toBe('root')
  })

  it('无 redirect 时登录成功回落到根路由(守卫按终端分流)', async () => {
    const { wrapper, router } = await mountLogin()
    await fillAndSubmit(wrapper, VALID_LOGIN.username, VALID_LOGIN.password)
    expect(router.currentRoute.value.name).toBe('root')
  })

  it('提交中禁用按钮并防重复提交', async () => {
    const gateway = createMockAuthGateway({ delayMs: 30 })
    const loginSpy = vi.spyOn(gateway, 'login')
    const { wrapper } = await mountLogin({}, gateway)
    await wrapper.get('[data-testid="login-username"]').setValue(VALID_LOGIN.username)
    await wrapper.get('[data-testid="login-password"]').setValue(VALID_LOGIN.password)
    const form = wrapper.find('form')
    await form.trigger('submit')
    expect(wrapper.get('[data-testid="login-submit"]').attributes('disabled')).toBeDefined()
    await form.trigger('submit')
    await flushPromises()
    expect(loginSpy).toHaveBeenCalledTimes(1)
  })

  it('登录成功后 sessionStorage 不包含密码明文', async () => {
    const { wrapper } = await mountLogin()
    await fillAndSubmit(wrapper, VALID_LOGIN.username, VALID_LOGIN.password)
    const stored = sessionStorage.getItem(AUTH_SESSION_STORAGE_KEY)
    expect(stored).toBeTruthy()
    expect(stored).not.toContain(VALID_LOGIN.password)
  })

  it('登录成功后 authStore 状态正确(非密码状态可断言)', async () => {
    const { wrapper } = await mountLogin()
    await fillAndSubmit(wrapper, VALID_LOGIN.username, VALID_LOGIN.password)
    expect(useAuthStore().isAuthenticated).toBe(true)
    expect(useAuthStore().user?.displayName).toBe('Mock 演示账号')
  })
})
