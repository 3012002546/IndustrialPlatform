/**
 * 终端自动识别(§11.1):
 * - 宽度 >=1200 → PC
 * - 宽度 <768   → Mobile
 * - 768–1199 且支持触控 → PDA
 * - 768–1199 且不支持触控 → PC
 */

import type { TerminalType } from './types'

/** 纯函数:按宽度与触控能力判定终端(可单测,不依赖 window)。 */
export function detectTerminal(width: number, hasTouch: boolean): TerminalType {
  if (width >= 1200) return 'pc'
  if (width < 768) return 'mobile'
  return hasTouch ? 'pda' : 'pc'
}
