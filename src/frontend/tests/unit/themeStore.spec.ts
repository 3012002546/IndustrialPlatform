/**
 * ThemeStore 测试(PF-01 §7.4):设备级初始化、用户绑定、系统监听、持久化与旧键迁移。
 * jsdom 默认无 matchMedia,通过 stub 提供(符合 resolver 的安全回退前提)。
 */

import { createPinia, setActivePinia } from 'pinia'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import {
  getTenantUiDefaultsSource,
  setTenantUiDefaultsSource,
  useThemeStore,
} from '@/stores/themeStore'
import { buildUserUiPreferenceKey } from '@/theme'

const SCOPE = { tenantId: 't1', userId: 'u1' }
const USER_KEY = buildUserUiPreferenceKey(SCOPE)

/** 默认租户来源:空适配器(PF-01 阶段不发网络请求)。 */
function emptySource(): { load: () => Promise<Record<string, never>> } {
  return { load: async () => ({}) }
}

interface MockMq extends MediaQueryList {
  listeners: Set<EventListener>
  setMatches(value: boolean): void
}

function mockMatchMedia(prefersDark: boolean): MockMq {
  const listeners = new Set<EventListener>()
  let matches = prefersDark
  const mq = {
    get matches() {
      return matches
    },
    media: '(prefers-color-scheme: dark)',
    onchange: null,
    listeners,
    addEventListener: vi.fn((type: string, cb: EventListener) => {
      if (type === 'change') listeners.add(cb)
    }),
    removeEventListener: vi.fn((type: string, cb: EventListener) => {
      if (type === 'change') listeners.delete(cb)
    }),
    setMatches(value: boolean) {
      matches = value
      for (const cb of listeners) cb({ matches: value } as MediaQueryListEvent)
    },
    addListener: vi.fn(),
    removeListener: vi.fn(),
    dispatchEvent: vi.fn(),
  } as unknown as MockMq
  return mq
}

/** 创建独立 pinia + matchMedia stub,返回 store 与 mock(供触发系统变化断言)。 */
function boot(prefersDark = false): {
  store: ReturnType<typeof useThemeStore>
  mq: MockMq
} {
  setActivePinia(createPinia())
  const mq = mockMatchMedia(prefersDark)
  vi.stubGlobal(
    'matchMedia',
    vi.fn(() => mq),
  )
  return { store: useThemeStore(), mq }
}

describe('ThemeStore — 设备级初始化', () => {
  beforeEach(() => {
    localStorage.clear()
    setTenantUiDefaultsSource(emptySource())
    vi.unstubAllGlobals()
  })

  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('无 bootstrap 快照时使用产品默认值并应用根节点外观', async () => {
    const { store } = boot()
    await store.initialize()
    expect(store.preferences).toMatchObject({
      palette: 'industrial-cyan',
      mode: 'system',
      density: 'comfortable',
    })
    expect(document.documentElement.getAttribute('data-ip-palette')).toBe('industrial-cyan')
    expect(document.documentElement.getAttribute('data-ip-color-mode')).toBe('light')
    expect(document.documentElement.getAttribute('data-ip-theme-mode')).toBe('system')
    expect(document.documentElement.getAttribute('data-ip-density')).toBe('comfortable')
    expect(document.documentElement.style.colorScheme).toBe('light')
  })

  it('bootstrap 快照被采纳;system + 系统暗色 → 有效暗色', async () => {
    localStorage.setItem(
      'industrial-platform.ui.bootstrap.v1',
      JSON.stringify({ version: 1, palette: 'neutral-gray', mode: 'system', density: 'compact' }),
    )
    const { store } = boot(true)
    await store.initialize()
    expect(store.preferences.palette).toBe('neutral-gray')
    expect(store.preferences.density).toBe('compact')
    expect(store.effectiveColorMode).toBe('dark')
    expect(document.documentElement.getAttribute('data-ip-color-mode')).toBe('dark')
  })

  it('initialize 幂等:只注册一个系统监听器', async () => {
    const { store, mq } = boot()
    await store.initialize()
    await store.initialize()
    expect(mq.addEventListener).toHaveBeenCalledTimes(1)
  })

  it('系统模式变化仅当 mode=system 时更新有效明暗', async () => {
    const { store, mq } = boot(false)
    await store.initialize()
    expect(store.effectiveColorMode).toBe('light')
    // 模式为 system:系统切暗 → 有效暗色,DOM 同步
    mq.setMatches(true)
    expect(store.effectiveColorMode).toBe('dark')
    expect(document.documentElement.getAttribute('data-ip-color-mode')).toBe('dark')
    // 显式模式不随系统变化
    store.setMode('light')
    mq.setMatches(true)
    expect(store.effectiveColorMode).toBe('light')
  })
})

describe('ThemeStore — 用户绑定与持久化', () => {
  beforeEach(() => {
    localStorage.clear()
    setTenantUiDefaultsSource(emptySource())
    vi.unstubAllGlobals()
  })

  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('bindUser 合并并持久化用户快照,并同步 bootstrap', async () => {
    const { store } = boot()
    await store.initialize()
    await store.bindUser(SCOPE)
    const stored = JSON.parse(localStorage.getItem(USER_KEY) ?? 'null')
    expect(stored).toMatchObject({ version: 1, palette: 'industrial-cyan', mode: 'system' })
    expect(
      JSON.parse(localStorage.getItem('industrial-platform.ui.bootstrap.v1') ?? 'null'),
    ).toMatchObject({ version: 1, palette: 'industrial-cyan' })
    expect(store.scope).toEqual(SCOPE)
  })

  it('已存在用户快照时 bindUser 采纳用户值', async () => {
    localStorage.setItem(
      USER_KEY,
      JSON.stringify({
        version: 1,
        palette: 'technology-blue',
        mode: 'dark',
        density: 'compact',
        pcFunctionTreeCollapsed: true,
        updatedAt: '2026-08-12T00:00:00.000Z',
      }),
    )
    const { store } = boot()
    await store.initialize()
    await store.bindUser(SCOPE)
    expect(store.preferences.palette).toBe('technology-blue')
    expect(store.preferences.mode).toBe('dark')
    expect(store.preferences.pcFunctionTreeCollapsed).toBe(true)
    expect(document.documentElement.getAttribute('data-ip-palette')).toBe('technology-blue')
    expect(document.documentElement.getAttribute('data-ip-color-mode')).toBe('dark')
  })

  it('旧侧栏键迁移:无用户快照时写入迁移值并删除旧键', async () => {
    localStorage.setItem('industrial-platform.pc.sidebar.collapsed.v1', '1')
    const { store } = boot()
    await store.initialize()
    await store.bindUser(SCOPE)
    expect(store.preferences.pcFunctionTreeCollapsed).toBe(true)
    expect(localStorage.getItem('industrial-platform.pc.sidebar.collapsed.v1')).toBeNull()
    const stored = JSON.parse(localStorage.getItem(USER_KEY) ?? 'null')
    expect(stored.pcFunctionTreeCollapsed).toBe(true)
  })

  it('旧侧栏键迁移写入失败:保留旧键并使用产品默认值', async () => {
    localStorage.setItem('industrial-platform.pc.sidebar.collapsed.v1', '1')
    const setItemSpy = vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new Error('QuotaExceededError')
    })
    const { store } = boot()
    await store.initialize()
    await store.bindUser(SCOPE)
    expect(store.preferences.pcFunctionTreeCollapsed).toBe(false)
    expect(localStorage.getItem('industrial-platform.pc.sidebar.collapsed.v1')).toBe('1')
    setItemSpy.mockRestore()
  })

  it('set* 更新状态/DOM 并持久化用户快照与 bootstrap', async () => {
    const { store } = boot()
    await store.initialize()
    await store.bindUser(SCOPE)
    store.setPalette('neutral-gray')
    store.setMode('dark')
    store.setDensity('compact')
    store.setPcFunctionTreeCollapsed(true)
    expect(store.preferences.palette).toBe('neutral-gray')
    expect(store.preferences.mode).toBe('dark')
    expect(store.preferences.density).toBe('compact')
    expect(document.documentElement.getAttribute('data-ip-density')).toBe('compact')
    const stored = JSON.parse(localStorage.getItem(USER_KEY) ?? 'null')
    expect(stored.palette).toBe('neutral-gray')
    expect(stored.pcFunctionTreeCollapsed).toBe(true)
    expect(
      JSON.parse(localStorage.getItem('industrial-platform.ui.bootstrap.v1') ?? 'null').palette,
    ).toBe('neutral-gray')
  })

  it('三态导航模式与旧功能树折叠偏好保持兼容', async () => {
    const { store } = boot()
    await store.initialize()
    await store.bindUser(SCOPE)
    store.setPcNavigationMode('compact')
    expect(store.navigationMode).toBe('compact')
    expect(store.preferences.pcFunctionTreeCollapsed).toBe(true)
    store.setPcNavigationMode('expanded')
    expect(store.navigationMode).toBe('expanded')
    expect(store.preferences.pcFunctionTreeCollapsed).toBe(false)
  })

  it('bindUser 同一作用域幂等;setTenantUiDefaultsSource 可替换默认来源', async () => {
    const source = {
      load: vi.fn(async () => ({ palette: 'neutral-gray' as const, mode: 'light' as const })),
    }
    setTenantUiDefaultsSource(source)
    expect(getTenantUiDefaultsSource()).toBe(source)
    const { store } = boot()
    await store.initialize()
    await store.bindUser(SCOPE)
    await store.bindUser(SCOPE)
    expect(source.load).toHaveBeenCalledTimes(2)
    expect(store.preferences.palette).toBe('neutral-gray')
    expect(store.preferences.mode).toBe('light')
  })
})

describe('ThemeStore — dispose 与存储异常', () => {
  beforeEach(() => {
    localStorage.clear()
    setTenantUiDefaultsSource(emptySource())
    vi.unstubAllGlobals()
  })

  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('dispose 移除系统监听器', async () => {
    const { store, mq } = boot()
    await store.initialize()
    expect(mq.removeEventListener).not.toHaveBeenCalled()
    store.dispose()
    expect(mq.removeEventListener).toHaveBeenCalled()
  })

  it('localStorage 不可用时 initialize/bindUser 不抛出', async () => {
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
      throw new Error('SecurityError')
    })
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new Error('SecurityError')
    })
    const { store } = boot()
    await expect(store.initialize()).resolves.toBeUndefined()
    await expect(store.bindUser(SCOPE)).resolves.toBeUndefined()
    // 安全回退:保留产品默认外观字段(updatedAt 因合并生成真实时间,不做断言)
    expect(store.preferences).toMatchObject({
      version: 1,
      palette: 'industrial-cyan',
      mode: 'system',
      density: 'comfortable',
      pcFunctionTreeCollapsed: false,
    })
  })
})
