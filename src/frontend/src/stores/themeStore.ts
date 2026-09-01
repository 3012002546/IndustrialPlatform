/**
 * 主题 Store(PF-01 §7.4):当前偏好、系统监听与用户绑定。
 * - initialize() 幂等,只注册一个 matchMedia change 监听器。
 * - bindUser() 对同一 scope 幂等;切换用户时一次性替换完整偏好后再写 DOM。
 * - set*() 先更新状态与 DOM,再尽力持久化;存储失败不回滚用户可见选择。
 * - dispose() 只供测试和应用销毁,必须移除系统监听器。
 */

import { defineStore } from 'pinia'
import { ref } from 'vue'

import { DEFAULT_UI_PREFERENCES } from '@/theme/defaults'
import {
  readBootstrapAppearance,
  readLegacyPcSidebarCollapsed,
  readUiPreferences,
  removeLegacyPcSidebarCollapsed,
  readPcNavigationMode,
  writePcNavigationMode,
  writeBootstrapAppearance,
  writeUiPreferences,
  mergeUiPreferences,
  type UiPreferencesStorage,
} from '@/theme/preferences'
import { applyAppearanceToRoot, resolveEffectiveColorMode } from '@/theme/resolver'
import type {
  EffectiveColorMode,
  PcDensity,
  ResolvedUiAppearance,
  ThemeMode,
  ThemePalette,
  UiPreferencesV1,
  UserUiScope,
  PcNavigationMode,
} from '@/theme/types'

/** 租户默认主题来源(PF-01 阶段为空适配器;PF-02 通过 setTenantUiDefaultsSource 安装)。 */
export interface TenantUiDefaultsSource {
  load(scope: UserUiScope): Promise<Partial<Omit<UiPreferencesV1, 'version' | 'updatedAt'>>>
}

let tenantUiDefaultsSource: TenantUiDefaultsSource = {
  load: async () => ({}),
}

export function setTenantUiDefaultsSource(source: TenantUiDefaultsSource): void {
  tenantUiDefaultsSource = source
}

export function getTenantUiDefaultsSource(): TenantUiDefaultsSource {
  return tenantUiDefaultsSource
}

function defaultStorage(): UiPreferencesStorage {
  return globalThis.localStorage
}

function getSystemMedia(): MediaQueryList | null {
  if (typeof window === 'undefined') return null
  const query = '(prefers-color-scheme: dark)'
  return window.matchMedia ? window.matchMedia(query) : null
}

function sameUserScope(a: UserUiScope, b: UserUiScope): boolean {
  return a.tenantId === b.tenantId && a.userId === b.userId
}

export const useThemeStore = defineStore('theme', () => {
  /** 当前生效偏好(含用户作用域合并结果)。 */
  const preferences = ref<UiPreferencesV1>({ ...DEFAULT_UI_PREFERENCES })
  /** 有效明暗(由 mode 与系统偏好解析)。 */
  const effectiveColorMode = ref<EffectiveColorMode>('light')
  /** 已绑定用户作用域(null 表示仅设备级外观)。 */
  const scope = ref<UserUiScope | null>(null)
  /** 是否已完成首次初始化(幂等)。 */
  const ready = ref(false)
  const navigationMode = ref<PcNavigationMode>('expanded')

  let systemMedia: MediaQueryList | null = null
  let systemListener: (() => void) | null = null
  let initializePromise: Promise<void> | null = null
  let bindPromise: Promise<void> | null = null
  let boundScope: UserUiScope | null = null

  function systemPrefersDark(): boolean {
    return getSystemMedia()?.matches ?? false
  }

  /** 构建并应用根节点外观(基于当前偏好与有效明暗)。 */
  function applyToDom(): void {
    const appearance: ResolvedUiAppearance = {
      palette: preferences.value.palette,
      mode: preferences.value.mode,
      effectiveColorMode: effectiveColorMode.value,
      density: preferences.value.density,
    }
    applyAppearanceToRoot(document.documentElement, appearance)
  }

  /** 尽力持久化设备级 bootstrap 外观(用户显式保存后同步更新)。 */
  function persistBootstrap(): void {
    writeBootstrapAppearance(defaultStorage(), {
      version: 1,
      palette: preferences.value.palette,
      mode: preferences.value.mode,
      density: preferences.value.density,
    })
  }

  /** 尽力持久化用户快照;无作用域时跳过。 */
  function persistUser(): void {
    if (scope.value === null) return
    writeUiPreferences(defaultStorage(), scope.value, preferences.value)
  }

  /** 系统模式变化回调:仅当 mode=system 时更新有效明暗与 DOM。 */
  function onSystemChange(): void {
    if (preferences.value.mode !== 'system') return
    effectiveColorMode.value = systemPrefersDark() ? 'dark' : 'light'
    applyToDom()
  }

  function registerSystemListener(): void {
    if (systemListener !== null) return
    const mq = getSystemMedia()
    if (mq === null) return
    systemMedia = mq
    systemListener = onSystemChange
    mq.addEventListener('change', onSystemChange)
  }

  /** 设备级初始化:读取 bootstrap 快照、解析有效明暗、注册系统监听。幂等。 */
  async function initialize(): Promise<void> {
    if (initializePromise !== null) return initializePromise
    initializePromise = (async () => {
      const bootstrap = readBootstrapAppearance(defaultStorage())
      if (bootstrap !== null) {
        preferences.value = {
          ...DEFAULT_UI_PREFERENCES,
          palette: bootstrap.palette,
          mode: bootstrap.mode,
          density: bootstrap.density,
        }
      }
      effectiveColorMode.value = resolveEffectiveColorMode(
        preferences.value.mode,
        systemPrefersDark(),
      )
      applyToDom()
      registerSystemListener()
      ready.value = true
    })()
    try {
      await initializePromise
    } finally {
      initializePromise = null
    }
  }

  /**
   * 绑定用户作用域:合并产品默认 + 租户默认 + 用户快照后整体替换并写 DOM。
   * 同一 scope 幂等;旧侧栏键仅在用户无新快照时迁移一次。
   */
  async function bindUser(nextScope: UserUiScope): Promise<void> {
    if (boundScope !== null && sameUserScope(boundScope, nextScope) && ready.value) return
    if (bindPromise !== null) return bindPromise
    bindPromise = (async () => {
      const storage = defaultStorage()
      const userPrefs = readUiPreferences(storage, nextScope)
      const tenantDefaults = await tenantUiDefaultsSource.load(nextScope)
      const merged = mergeUiPreferences(DEFAULT_UI_PREFERENCES, tenantDefaults, userPrefs, () =>
        Date.now(),
      )
      const legacy = userPrefs === null ? readLegacyPcSidebarCollapsed(storage) : null
      const storedNavigationMode = readPcNavigationMode(storage, nextScope)

      scope.value = nextScope
      if (legacy !== null) {
        // 无用户快照且有旧侧栏键:先尝试持久化(含迁移值),成功才采纳并删除旧键。
        const candidate = { ...merged, pcFunctionTreeCollapsed: legacy }
        const persisted = writeUiPreferences(storage, nextScope, candidate)
        if (persisted) {
          removeLegacyPcSidebarCollapsed(storage)
          preferences.value = candidate
        } else {
          preferences.value = merged
        }
      } else if (userPrefs === null) {
        preferences.value = merged
        writeUiPreferences(storage, nextScope, merged)
      } else {
        preferences.value = merged
      }
      navigationMode.value =
        storedNavigationMode ?? (legacy === true ? 'secondary-collapsed' : 'expanded')
      writePcNavigationMode(storage, nextScope, navigationMode.value)

      effectiveColorMode.value = resolveEffectiveColorMode(
        preferences.value.mode,
        systemPrefersDark(),
      )
      applyToDom()
      persistBootstrap()
      ready.value = true
      boundScope = nextScope
    })()
    try {
      await bindPromise
    } finally {
      bindPromise = null
    }
  }

  /** 设置配色:先更新状态与 DOM,再尽力持久化。 */
  function setPalette(value: ThemePalette): void {
    preferences.value = { ...preferences.value, palette: value }
    applyToDom()
    persistUser()
    persistBootstrap()
  }

  /** 设置明暗模式(light/dark/system)。 */
  function setMode(value: ThemeMode): void {
    preferences.value = { ...preferences.value, mode: value }
    effectiveColorMode.value = resolveEffectiveColorMode(value, systemPrefersDark())
    applyToDom()
    persistUser()
    persistBootstrap()
  }

  /** 设置 PC 密度。 */
  function setDensity(value: PcDensity): void {
    preferences.value = { ...preferences.value, density: value }
    applyToDom()
    persistUser()
    persistBootstrap()
  }

  /** 设置 PC 功能树折叠状态(接入 ThemeStore,不再直接读写旧侧栏键)。 */
  function setPcFunctionTreeCollapsed(value: boolean): void {
    setPcNavigationMode(value ? 'secondary-collapsed' : 'expanded')
  }

  function setPcNavigationMode(value: PcNavigationMode): void {
    navigationMode.value = value
    preferences.value = { ...preferences.value, pcFunctionTreeCollapsed: value !== 'expanded' }
    persistUser()
    if (scope.value !== null) writePcNavigationMode(defaultStorage(), scope.value, value)
  }

  /** 移除系统模式监听器(测试与应用销毁时调用)。 */
  function dispose(): void {
    if (systemListener !== null && systemMedia !== null) {
      systemMedia.removeEventListener('change', systemListener)
    }
    systemMedia = null
    systemListener = null
  }

  return {
    preferences,
    effectiveColorMode,
    scope,
    ready,
    navigationMode,
    initialize,
    bindUser,
    setPalette,
    setMode,
    setDensity,
    setPcFunctionTreeCollapsed,
    setPcNavigationMode,
    dispose,
  }
})
