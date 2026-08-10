import { defineComponent, h, type App as VueApp } from 'vue'
import { describe, expect, it } from 'vitest'

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
  it('creates an app with the default root component', () => {
    const app = createIndustrialApp()
    const el = mountApp(app)
    expect(el.textContent).toContain('Industrial Platform')
    unmountApp(app, el)
  })

  it('installs Element Plus (global $message available)', () => {
    const app = createIndustrialApp()
    expect(app.config.globalProperties.$message).toBeDefined()
  })

  it('accepts a custom root component', () => {
    const app = createIndustrialApp({ rootComponent: TestRoot })
    const el = mountApp(app)
    expect(el.textContent).toBe('test-root')
    unmountApp(app, el)
  })
})
