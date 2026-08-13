/**
 * 主题产品默认值与枚举白名单(PF-01 §7.1)。
 * 默认对象不可在运行时直接修改;写入时生成新对象和真实 updatedAt。
 */

import type { PcDensity, ThemeMode, ThemePalette, UiPreferencesV1 } from './types'

export const THEME_PALETTES: readonly ThemePalette[] = [
  'industrial-cyan',
  'technology-blue',
  'neutral-gray',
]

export const THEME_MODES: readonly ThemeMode[] = ['light', 'dark', 'system']

export const PC_DENSITIES: readonly PcDensity[] = ['comfortable', 'compact']

export function isThemePalette(value: unknown): value is ThemePalette {
  return typeof value === 'string' && (THEME_PALETTES as readonly string[]).includes(value)
}

export function isThemeMode(value: unknown): value is ThemeMode {
  return typeof value === 'string' && (THEME_MODES as readonly string[]).includes(value)
}

export function isPcDensity(value: unknown): value is PcDensity {
  return typeof value === 'string' && (PC_DENSITIES as readonly string[]).includes(value)
}

/** 产品默认值:运行时不得直接修改,写入偏好时基于它生成新对象。 */
export const DEFAULT_UI_PREFERENCES: Readonly<UiPreferencesV1> = {
  version: 1,
  palette: 'industrial-cyan',
  mode: 'system',
  density: 'comfortable',
  pcFunctionTreeCollapsed: false,
  updatedAt: '1970-01-01T00:00:00.000Z',
}
