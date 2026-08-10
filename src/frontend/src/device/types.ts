/** 终端类型(§11):PC / PDA / Mobile。 */

export type TerminalType = 'pc' | 'pda' | 'mobile'

/** 开发期手动覆盖值(§11.2):auto 表示使用自动识别。 */
export type TerminalOverride = TerminalType | 'auto'
