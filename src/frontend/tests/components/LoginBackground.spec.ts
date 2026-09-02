import { mount } from '@vue/test-utils'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'

import LoginBackground from '@/components/login/LoginBackground.vue'

interface FakeMediaQueryList {
  matches: boolean
  addEventListener: ReturnType<typeof vi.fn>
  removeEventListener: ReturnType<typeof vi.fn>
}

class FakeCanvasRenderingContext2D {
  clearRect = vi.fn()
  setTransform = vi.fn()
  beginPath = vi.fn()
  moveTo = vi.fn()
  lineTo = vi.fn()
  quadraticCurveTo = vi.fn()
  stroke = vi.fn()
  arc = vi.fn<CanvasRenderingContext2D['arc']>()
  fillAlphas: number[] = []
  fill = vi.fn(() => this.fillAlphas.push(this.globalAlpha))
  strokeStyle = ''
  fillStyle = ''
  lineWidth = 1
  globalAlpha = 1
}

describe('LoginBackground', () => {
  let hidden = false
  let originalHidden: PropertyDescriptor | undefined
  let originalMatchMedia: PropertyDescriptor | undefined
  let originalGetContext: PropertyDescriptor | undefined
  let context: FakeCanvasRenderingContext2D
  let media: FakeMediaQueryList

  beforeEach(() => {
    originalHidden = Object.getOwnPropertyDescriptor(document, 'hidden')
    originalMatchMedia = Object.getOwnPropertyDescriptor(window, 'matchMedia')
    originalGetContext = Object.getOwnPropertyDescriptor(HTMLCanvasElement.prototype, 'getContext')
    Object.defineProperty(document, 'hidden', {
      configurable: true,
      get: () => hidden,
    })
    context = new FakeCanvasRenderingContext2D()
    media = {
      matches: false,
      addEventListener: vi.fn(),
      removeEventListener: vi.fn(),
    }
    vi.stubGlobal('CanvasRenderingContext2D', FakeCanvasRenderingContext2D)
    vi.stubGlobal(
      'ResizeObserver',
      class {
        observe = vi.fn()
        disconnect = vi.fn()
      },
    )
    vi.stubGlobal('requestAnimationFrame', vi.fn(() => 7))
    vi.stubGlobal('cancelAnimationFrame', vi.fn())
    Object.defineProperty(window, 'matchMedia', {
      configurable: true,
      value: vi.fn(() => media as unknown as MediaQueryList),
    })
    Object.defineProperty(HTMLCanvasElement.prototype, 'getContext', {
      configurable: true,
      value: vi.fn(() => context as unknown as CanvasRenderingContext2D),
    })
  })

  afterEach(() => {
    vi.restoreAllMocks()
    vi.unstubAllGlobals()
    if (originalHidden === undefined) delete (document as { hidden?: boolean }).hidden
    else Object.defineProperty(document, 'hidden', originalHidden)
    if (originalMatchMedia === undefined) delete (window as { matchMedia?: unknown }).matchMedia
    else Object.defineProperty(window, 'matchMedia', originalMatchMedia)
    if (originalGetContext === undefined) {
      Reflect.deleteProperty(HTMLCanvasElement.prototype, 'getContext')
    }
    else Object.defineProperty(HTMLCanvasElement.prototype, 'getContext', originalGetContext)
  })

  it('renders an aria-hidden, pointer-free canvas background and draws a static frame', () => {
    const wrapper = mount(LoginBackground)

    expect(wrapper.get('[data-testid="login-background"]').attributes('aria-hidden')).toBe('true')
    expect(wrapper.get('.login-background').attributes('style')).toBeUndefined()
    expect(context.clearRect).toHaveBeenCalled()
    expect(requestAnimationFrame).toHaveBeenCalled()

    wrapper.unmount()
  })

  it('uses the supplied full-screen factory image as the background asset', () => {
    const source = readFileSync(
      resolve(process.cwd(), 'src/components/login/LoginBackground.vue'),
      'utf8',
    )

    expect(source).toContain("url('/brand/login-background.png')")
    expect(source).toMatch(/background-size:\s*cover/)
    expect(source).not.toContain('industrial-platform-dynamic-login-preview.gif')
  })

  it('keeps particles compact and soft with bounded density, then advances the flow', () => {
    vi.spyOn(HTMLCanvasElement.prototype, 'getBoundingClientRect').mockReturnValue(
      new DOMRect(0, 0, 1280, 720),
    )
    const wrapper = mount(LoginBackground)

    expect(context.fillAlphas.some((alpha) => alpha >= 0.3)).toBe(true)
    expect(Math.max(...context.fillAlphas)).toBeLessThanOrEqual(0.7)
    const radii = context.arc.mock.calls.map((call) => call[2])
    expect(Math.max(...radii)).toBeGreaterThanOrEqual(3)
    expect(Math.max(...radii)).toBeLessThanOrEqual(4)
    expect(context.arc.mock.calls.length).toBeGreaterThan(100)
    expect(context.arc.mock.calls.length).toBeLessThan(250)
    const firstPoint = context.arc.mock.calls[0]
    context.arc.mockClear()
    const tick = vi.mocked(requestAnimationFrame).mock.calls[0]![0]
    tick(performance.now() + 1000)
    expect(context.arc.mock.calls[0]).not.toEqual(firstPoint)
    expect(requestAnimationFrame).toHaveBeenCalledTimes(2)

    wrapper.unmount()
  })

  it('does not animate while reduced-motion or page-hidden, then resumes on visibility', () => {
    media.matches = true
    const reducedWrapper = mount(LoginBackground)
    expect(requestAnimationFrame).not.toHaveBeenCalled()
    reducedWrapper.unmount()

    media.matches = false
    hidden = true
    const hiddenWrapper = mount(LoginBackground)
    expect(requestAnimationFrame).not.toHaveBeenCalled()

    hidden = false
    document.dispatchEvent(new Event('visibilitychange'))
    expect(requestAnimationFrame).toHaveBeenCalledTimes(1)

    hiddenWrapper.unmount()
    expect(cancelAnimationFrame).toHaveBeenCalledWith(7)
  })
})
