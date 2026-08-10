import { defineComponent, h, type App as VueApp } from 'vue'
import { describe, expect, it, vi } from 'vitest'

import { createIndustrialApp } from '@/app/createIndustrialApp'

const TestRoot = defineComponent({
  render: () => h('div', 'test-root'),
})

function mountApp(app: VueApp): HTMLElement {
  const el = document.createElement('div')
  document.body.appendChild(el)
  app.mount(el)
  return el
}

function unmountApp(app: VueApp, el: HTMLElement): void {
  app.unmount()
  document.body.removeChild(el)
}

describe('createIndustrialApp', () => {
  it('boots to the login page via the wired router (default root component)', async () => {
    const app = createIndustrialApp()
    const el = mountApp(app)
    // 无会话 → 守卫重定向到 /login;登录页渲染(挂载后异步导航)
    await vi.waitFor(() => {
      expect(el.textContent).toContain('登录')
    })
    unmountApp(app, el)
  })

  it('installs Element Plus and Vue Router', () => {
    const app = createIndustrialApp()
    expect(app.config.globalProperties.$message).toBeDefined()
    expect(app.config.globalProperties.$router).toBeDefined()
  })

  it('accepts a custom root component', () => {
    const app = createIndustrialApp({ rootComponent: TestRoot })
    const el = mountApp(app)
    expect(el.textContent).toBe('test-root')
    unmountApp(app, el)
  })
})
