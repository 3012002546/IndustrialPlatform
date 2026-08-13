/**
 * 主题类型(PF-01 §7.1):三配色、明暗/系统模式、PC 密度与版本化偏好快照。
 * 纯类型,无 Vue/Router 依赖;稳定出口见 index.ts。
 */

/** 三套内置配色。 */
export type ThemePalette = 'industrial-cyan' | 'technology-blue' | 'neutral-gray'

/** 明暗模式:system 表示跟随操作系统,由 resolver 解析为有效明暗。 */
export type ThemeMode = 'light' | 'dark' | 'system'

/** 有效明暗:只有 light/dark 两种落地值,根节点与语义 Token 只消费它。 */
export type EffectiveColorMode = 'light' | 'dark'

/** PC 内容密度。 */
export type PcDensity = 'comfortable' | 'compact'

/**
 * 版本化 UI 偏好快照(v1)。
 * 只保存外观与功能树折叠状态;不保存 Token、密码、权限列表或业务数据。
 */
export interface UiPreferencesV1 {
  version: 1
  palette: ThemePalette
  mode: ThemeMode
  density: PcDensity
  pcFunctionTreeCollapsed: boolean
  /** ISO 8601,仅用于偏好迁移诊断,不参与业务排序或授权。 */
  updatedAt: string
}

/** 用户作用域:主题/标签等本地偏好按 tenantId + userId 隔离。 */
export interface UserUiScope {
  tenantId: string
  userId: string
}

/** 解析后可直接应用到根节点的外观结果。 */
export interface ResolvedUiAppearance {
  palette: ThemePalette
  mode: ThemeMode
  effectiveColorMode: EffectiveColorMode
  density: PcDensity
}
