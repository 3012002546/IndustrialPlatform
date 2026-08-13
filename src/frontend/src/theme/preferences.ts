/**
 * 版本化偏好解析、用户键与安全回退(PF-01 §7.2)。
 * 纯模块:无 Vue/Router 依赖,Store 通过它读写 localStorage。
 * 非法 JSON、未知版本、非法枚举与存储异常一律安全回退,不抛到页面。
 */

import { isPcDensity, isThemeMode, isThemePalette } from './defaults'
import type { PcDensity, ThemeMode, ThemePalette, UiPreferencesV1, UserUiScope } from './types'

/** 首帧设备级外观快照(无用户/租户/会话信息)。 */
export interface BootstrapAppearance {
  version: 1
  palette: ThemePalette
  mode: ThemeMode
  density: PcDensity
}

/** 首帧 bootstrap 键。 */
export const UI_BOOTSTRAP_STORAGE_KEY = 'industrial-platform.ui.bootstrap.v1'

/** 用户 UI 偏好键前缀(实际键见 buildUserUiPreferenceKey)。 */
const USER_PREFERENCES_KEY_PREFIX = 'industrial-platform.ui.preferences.v1'

/** 第一批侧栏折叠键:仅作为一次迁移输入(§7.2),成功后删除。 */
export const LEGACY_PC_SIDEBAR_COLLAPSED_KEY = 'industrial-platform.pc.sidebar.collapsed.v1'

/** 存储抽象:jsdom/浏览器均可用,异常由读写封装捕获。 */
export interface UiPreferencesStorage {
  getItem(key: string): string | null
  setItem(key: string, value: string): void
  removeItem(key: string): void
}

/** 用户作用域 → 版本化用户键;标识只出现在键中,不重复进入 JSON 值。 */
export function buildUserUiPreferenceKey(scope: UserUiScope): string {
  const tenant = encodeURIComponent(scope.tenantId)
  const user = encodeURIComponent(scope.userId)
  return `${USER_PREFERENCES_KEY_PREFIX}:${tenant}:${user}`
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null
}

/** 解析用户偏好快照:任何字段非法/版本未知均返回 null(安全回退)。 */
export function parseUiPreferences(raw: string | null): UiPreferencesV1 | null {
  if (raw === null) return null
  let data: unknown
  try {
    data = JSON.parse(raw)
  } catch {
    return null
  }
  if (!isRecord(data)) return null
  if (data['version'] !== 1) return null
  if (!isThemePalette(data['palette'])) return null
  if (!isThemeMode(data['mode'])) return null
  if (!isPcDensity(data['density'])) return null
  if (typeof data['pcFunctionTreeCollapsed'] !== 'boolean') return null
  if (typeof data['updatedAt'] !== 'string') return null
  return {
    version: 1,
    palette: data['palette'],
    mode: data['mode'],
    density: data['density'],
    pcFunctionTreeCollapsed: data['pcFunctionTreeCollapsed'],
    updatedAt: data['updatedAt'],
  }
}

/** 序列化用户偏好快照(写入方保证结构合法)。 */
export function serializeUiPreferences(preferences: UiPreferencesV1): string {
  return JSON.stringify(preferences)
}

/** 解析首帧 bootstrap 外观:非法即忽略,返回 null 表示使用产品默认值。 */
export function parseBootstrapAppearance(raw: string | null): BootstrapAppearance | null {
  if (raw === null) return null
  let data: unknown
  try {
    data = JSON.parse(raw)
  } catch {
    return null
  }
  if (!isRecord(data)) return null
  if (data['version'] !== 1) return null
  if (!isThemePalette(data['palette'])) return null
  if (!isThemeMode(data['mode'])) return null
  if (!isPcDensity(data['density'])) return null
  return {
    version: 1,
    palette: data['palette'],
    mode: data['mode'],
    density: data['density'],
  }
}

/**
 * 合并偏好:逐字段优先级「用户显式值 > 租户默认值 > 产品默认值」。
 * 租户默认来源在本阶段为空适配器;now() 在用户无快照时生成新的 updatedAt。
 */
export function mergeUiPreferences(
  productDefaults: UiPreferencesV1,
  tenantDefaults: Partial<Omit<UiPreferencesV1, 'version' | 'updatedAt'>>,
  userPreferences: UiPreferencesV1 | null,
  now: () => number,
): UiPreferencesV1 {
  const palette = userPreferences?.palette ?? tenantDefaults.palette ?? productDefaults.palette
  const mode = userPreferences?.mode ?? tenantDefaults.mode ?? productDefaults.mode
  const density = userPreferences?.density ?? tenantDefaults.density ?? productDefaults.density
  const pcFunctionTreeCollapsed =
    userPreferences?.pcFunctionTreeCollapsed ??
    tenantDefaults.pcFunctionTreeCollapsed ??
    productDefaults.pcFunctionTreeCollapsed
  const updatedAt = userPreferences?.updatedAt ?? new Date(now()).toISOString()
  return { version: 1, palette, mode, density, pcFunctionTreeCollapsed, updatedAt }
}

/** 读取用户偏好:存储异常/非法 JSON 均返回 null,不抛到调用方。 */
export function readUiPreferences(
  storage: UiPreferencesStorage,
  scope: UserUiScope,
): UiPreferencesV1 | null {
  try {
    return parseUiPreferences(storage.getItem(buildUserUiPreferenceKey(scope)))
  } catch {
    return null
  }
}

/** 写入用户偏好:返回是否成功(QuotaExceeded/SecurityError 等返回 false)。 */
export function writeUiPreferences(
  storage: UiPreferencesStorage,
  scope: UserUiScope,
  preferences: UiPreferencesV1,
): boolean {
  try {
    storage.setItem(buildUserUiPreferenceKey(scope), serializeUiPreferences(preferences))
    return true
  } catch {
    return false
  }
}

/** 读取首帧 bootstrap 外观:异常返回 null。 */
export function readBootstrapAppearance(storage: UiPreferencesStorage): BootstrapAppearance | null {
  try {
    return parseBootstrapAppearance(storage.getItem(UI_BOOTSTRAP_STORAGE_KEY))
  } catch {
    return null
  }
}

/** 写入首帧 bootstrap 外观:返回是否成功。 */
export function writeBootstrapAppearance(
  storage: UiPreferencesStorage,
  appearance: BootstrapAppearance,
): boolean {
  try {
    storage.setItem(UI_BOOTSTRAP_STORAGE_KEY, JSON.stringify(appearance))
    return true
  } catch {
    return false
  }
}

/** 读取第一批侧栏折叠键:键缺失返回 null,否则返回解析出的布尔值。 */
export function readLegacyPcSidebarCollapsed(storage: UiPreferencesStorage): boolean | null {
  try {
    const raw = storage.getItem(LEGACY_PC_SIDEBAR_COLLAPSED_KEY)
    if (raw === null) return null
    return raw === '1'
  } catch {
    return null
  }
}

/** 删除第一批侧栏折叠键(迁移成功后调用)。 */
export function removeLegacyPcSidebarCollapsed(storage: UiPreferencesStorage): void {
  try {
    storage.removeItem(LEGACY_PC_SIDEBAR_COLLAPSED_KEY)
  } catch {
    // 删除失败不阻断迁移结果
  }
}
