/**
 * 设备 Store 单元测试:生效终端/建议/覆盖的联动与持久化。
 */

import { createPinia, setActivePinia } from 'pinia'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import * as device from '@/device'
import { TERMINAL_OVERRIDE_STORAGE_KEY } from '@/device'
import { useDeviceStore } from '@/stores/deviceStore'

function stubViewport(width: number, hasTouch: boolean): void {
  vi.spyOn(device, 'getViewportInfo').mockReturnValue({ width, hasTouch })
}

describe('useDeviceStore — 自动识别', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    localStorage.clear()
    vi.restoreAllMocks()
  })

  afterEach(() => {
    vi.restoreAllMocks()
  })

  it('1280 无触控 → PC', () => {
    stubViewport(1280, false)
    const store = useDeviceStore()
    store.init()
    expect(store.suggested).toBe('pc')
    expect(store.terminal).toBe('pc')
    expect(store.override).toBe('auto')
    expect(store.ready).toBe(true)
  })

  it('700 无触控 → Mobile', () => {
    stubViewport(700, false)
    const store = useDeviceStore()
    store.init()
    expect(store.terminal).toBe('mobile')
  })

  it('900 触控 → PDA;900 无触控 → PC', () => {
    stubViewport(900, true)
    const store = useDeviceStore()
    store.init()
    expect(store.terminal).toBe('pda')

    stubViewport(900, false)
    store.updateViewport()
    expect(store.terminal).toBe('pc')
  })
})

describe('useDeviceStore — 手动覆盖', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    localStorage.clear()
    vi.restoreAllMocks()
  })

  afterEach(() => {
    vi.restoreAllMocks()
  })

  it('init 读取持久化覆盖并优先于自动识别', () => {
    localStorage.setItem(TERMINAL_OVERRIDE_STORAGE_KEY, 'pda')
    stubViewport(1280, false)
    const store = useDeviceStore()
    store.init()
    expect(store.override).toBe('pda')
    expect(store.terminal).toBe('pda')
  })

  it('setOverride 写入存储并立即重算生效终端', () => {
    stubViewport(1280, false)
    const store = useDeviceStore()
    store.init()
    store.setOverride('mobile')
    expect(store.override).toBe('mobile')
    expect(store.terminal).toBe('mobile')
    expect(localStorage.getItem(TERMINAL_OVERRIDE_STORAGE_KEY)).toBe('mobile')
  })

  it('视口变化只更新建议终端,覆盖优先时生效终端不变', () => {
    stubViewport(1280, false)
    const store = useDeviceStore()
    store.init()
    store.setOverride('pda')
    stubViewport(700, false)
    store.updateViewport()
    expect(store.suggested).toBe('mobile')
    expect(store.terminal).toBe('pda')
  })

  it('非法覆盖值 init 时按 auto 处理', () => {
    localStorage.setItem(TERMINAL_OVERRIDE_STORAGE_KEY, 'tablet')
    stubViewport(1280, false)
    const store = useDeviceStore()
    store.init()
    expect(store.override).toBe('auto')
    expect(store.terminal).toBe('pc')
  })
})
