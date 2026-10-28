import { mount, type VueWrapper } from '@vue/test-utils'
import { h, nextTick } from 'vue'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import DashboardTableViewport from '@/components/studio/dashboard/DashboardTableViewport.vue'

let wrapper: VueWrapper | undefined
let callbacks: Map<number, FrameRequestCallback>
let sequence: number
let time: number
function advance(milliseconds: number) {
  for (let remaining = milliseconds; remaining > 0; remaining -= 20) {
    time += Math.min(20, remaining)
    const pending = [...callbacks.values()]
    callbacks.clear()
    pending.forEach((callback) => callback(time))
  }
}
async function viewport(seconds = 8, height = 600) {
  wrapper = mount(DashboardTableViewport, {
    props: { continuous: true, pageSeconds: seconds },
    slots: {
      default: ({ repeat }: { repeat: boolean }) =>
        h('table', [
          h('thead', [h('tr', [h('th', 'Batch')])]),
          ...Array.from({ length: repeat ? 2 : 1 }, (_, i) =>
            h('tbody', { key: i }, [h('tr', [h('td', '205')])]),
          ),
        ]),
    },
  })
  const element = wrapper.element as HTMLElement
  Object.defineProperties(element, {
    clientHeight: { value: 200 },
    scrollHeight: { value: height },
  })
  Object.defineProperty(element.querySelector('thead'), 'offsetHeight', { value: 40 })
  Object.defineProperty(element.querySelector('tbody'), 'offsetHeight', { value: height - 40 })
  advance(20)
  await nextTick()
  return element
}
beforeEach(() => {
  callbacks = new Map()
  sequence = 0
  time = 100
  vi.stubGlobal('requestAnimationFrame', (callback: FrameRequestCallback) => {
    callbacks.set(++sequence, callback)
    return sequence
  })
  vi.stubGlobal('cancelAnimationFrame', (id: number) => callbacks.delete(id))
})
afterEach(() => {
  wrapper?.unmount()
  wrapper = undefined
  vi.unstubAllGlobals()
})
describe('dashboard continuous table scrolling', () => {
  it('scrolls by one body viewport per configured duration and applies speed changes', async () => {
    const element = await viewport()
    advance(1000)
    expect(element.scrollTop).toBeCloseTo(20)
    await wrapper!.setProps({ pageSeconds: 4 })
    advance(1000)
    expect(element.scrollTop).toBeCloseTo(60)
  })
  it('pauses on hover and keyboard focus without catching up on resume', async () => {
    const element = await viewport()
    advance(1000)
    await wrapper!.trigger('mouseenter')
    advance(2000)
    expect(element.scrollTop).toBeCloseTo(20)
    await wrapper!.trigger('mouseleave')
    await wrapper!.trigger('focusin')
    advance(1000)
    expect(element.scrollTop).toBeCloseTo(20)
    await wrapper!.trigger('focusout')
    advance(1000)
    expect(element.scrollTop).toBeCloseTo(40)
  })
  it('keeps complete datasets stationary and cancels animation when disabled or destroyed', async () => {
    const element = await viewport(8, 200)
    advance(2000)
    expect(element.scrollTop).toBe(0)
    await wrapper!.setProps({ continuous: false })
    expect(callbacks.size).toBe(0)
    await wrapper!.setProps({ continuous: true })
    expect(callbacks.size).toBe(1)
    wrapper!.unmount()
    wrapper = undefined
    expect(callbacks.size).toBe(0)
  })
  it('continues through a duplicate body and wraps by exactly one body height without pausing', async () => {
    const element = await viewport(4, 220)
    expect(wrapper!.findAll('tbody')).toHaveLength(2)
    advance(4000)
    expect(element.scrollTop).toBeCloseTo(160)
    advance(600)
    expect(element.scrollTop).toBeCloseTo(4)
  })
  it('keeps subpixel progress when scaled browser geometry rounds the scroll offset', async () => {
    const element = await viewport(4, 220)
    let actual = 0
    Object.defineProperty(element, 'scrollTop', {
      get: () => actual,
      set: (value: number) => {
        actual = Math.round(value)
      },
    })
    advance(4000)
    expect(element.scrollTop).toBe(160)
    advance(600)
    expect(element.scrollTop).toBe(4)
  })
})
