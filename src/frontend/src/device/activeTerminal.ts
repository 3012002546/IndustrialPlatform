/**
 * 路由终端权威(PF-01 §7.11):显式路由终端优先,无显式路由时回退设备建议。
 * PDA/Mobile 布局与首页的终端文案统一消费该解析结果,
 * 不再把 deviceStore.terminal 当作显式路由事实源。
 */

import type { TerminalType } from './types'

/** 生效终端:显式路由 meta.terminal 优先,否则回退设备自动/覆盖建议。 */
export function resolveActiveTerminal(
  routeTerminal: TerminalType | undefined,
  deviceTerminal: TerminalType,
): TerminalType {
  return routeTerminal ?? deviceTerminal
}
