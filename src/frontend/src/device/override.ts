/**
 * 终端手动覆盖(§11.2):键 `industrial-platform.terminal.override.v1`,localStorage 持久化。
 * 允许值 pc / pda / mobile / auto;非法值按 auto 处理。
 * 优先级:显式路由 > 手动覆盖 > 自动识别。
 */

import type { TerminalOverride, TerminalType } from './types'

export const TERMINAL_OVERRIDE_STORAGE_KEY = 'industrial-platform.terminal.override.v1'
export const TERMINAL_OVERRIDE_VALUES = ['pc', 'pda', 'mobile', 'auto'] as const

/** 解析覆盖值;非法或 null 按 auto 处理。 */
export function parseTerminalOverride(raw: string | null): TerminalOverride {
  if (raw === 'pc' || raw === 'pda' || raw === 'mobile' || raw === 'auto') return raw
  return 'auto'
}

export type OverrideStorage = Pick<Storage, 'getItem' | 'setItem'>

export function readTerminalOverride(storage: OverrideStorage): TerminalOverride {
  return parseTerminalOverride(storage.getItem(TERMINAL_OVERRIDE_STORAGE_KEY))
}

export function writeTerminalOverride(storage: OverrideStorage, value: TerminalOverride): void {
  storage.setItem(TERMINAL_OVERRIDE_STORAGE_KEY, value)
}

/** 生效终端:auto 用自动识别结果,否则用显式覆盖值。 */
export function resolveTerminal(automatic: TerminalType, override: TerminalOverride): TerminalType {
  return override === 'auto' ? automatic : override
}
