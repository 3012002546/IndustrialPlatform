/** 终端识别统一出口。 */

export { detectTerminal } from './detect'
export { getViewportInfo, type ViewportInfo } from './environment'
export {
  TERMINAL_OVERRIDE_STORAGE_KEY,
  TERMINAL_OVERRIDE_VALUES,
  parseTerminalOverride,
  readTerminalOverride,
  resolveTerminal,
  writeTerminalOverride,
  type OverrideStorage,
} from './override'
export type { TerminalOverride, TerminalType } from './types'
