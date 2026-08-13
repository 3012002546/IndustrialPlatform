/**
 * 系统模式解析与根节点外观应用(PF-01 §7.3)。
 * 纯函数:matchMedia 只解析有效明暗,不覆盖用户保存的 mode=system。
 */

import type { EffectiveColorMode, ResolvedUiAppearance, ThemeMode } from './types'

/** 根节点外观属性名(与 index.html bootstrap 脚本保持一致)。 */
export const ROOT_PALETTE_ATTR = 'data-ip-palette'
export const ROOT_MODE_ATTR = 'data-ip-theme-mode'
export const ROOT_COLOR_MODE_ATTR = 'data-ip-color-mode'
export const ROOT_DENSITY_ATTR = 'data-ip-density'

/** 解析有效明暗:mode=system 跟随系统偏好;light/dark 原样返回。 */
export function resolveEffectiveColorMode(
  mode: ThemeMode,
  systemPrefersDark: boolean,
): EffectiveColorMode {
  if (mode === 'system') return systemPrefersDark ? 'dark' : 'light'
  return mode
}

/**
 * 把解析结果应用到根节点:设置四个 data-ip-* 属性并同步 style.colorScheme。
 * 组件只匹配这些属性,不直接匹配系统媒体查询决定品牌主题。
 */
export function applyAppearanceToRoot(root: HTMLElement, appearance: ResolvedUiAppearance): void {
  root.setAttribute(ROOT_PALETTE_ATTR, appearance.palette)
  root.setAttribute(ROOT_MODE_ATTR, appearance.mode)
  root.setAttribute(ROOT_COLOR_MODE_ATTR, appearance.effectiveColorMode)
  root.setAttribute(ROOT_DENSITY_ATTR, appearance.density)
  root.style.colorScheme = appearance.effectiveColorMode
}
