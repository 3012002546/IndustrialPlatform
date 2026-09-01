import { mount } from '@vue/test-utils'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

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
  arc = vi.fn()
  fill = vi.fn()
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
