/**
 * 主题模块稳定公共出口(PF-01 §5)。
 * theme/** 不引用 Vue 组件、Router 实例或业务模块;Store 只消费此出口。
 */

export { DEFAULT_UI_PREFERENCES, isPcDensity, isThemeMode, isThemePalette } from './defaults'
export { PC_DENSITIES, THEME_MODES, THEME_PALETTES } from './defaults'
export {
  LEGACY_PC_SIDEBAR_COLLAPSED_KEY,
  PC_NAVIGATION_MODE_KEY_PREFIX,
  UI_BOOTSTRAP_STORAGE_KEY,
  buildUserUiPreferenceKey,
  buildPcNavigationModeKey,
  mergeUiPreferences,
  parseBootstrapAppearance,
  parseUiPreferences,
  readBootstrapAppearance,
  readLegacyPcSidebarCollapsed,
  readUiPreferences,
  readPcNavigationMode,
  removeLegacyPcSidebarCollapsed,
  serializeUiPreferences,
  writeBootstrapAppearance,
  writeUiPreferences,
  writePcNavigationMode,
  type BootstrapAppearance,
  type UiPreferencesStorage,
} from './preferences'
export {
  ROOT_COLOR_MODE_ATTR,
  ROOT_DENSITY_ATTR,
  ROOT_MODE_ATTR,
  ROOT_PALETTE_ATTR,
  applyAppearanceToRoot,
  resolveEffectiveColorMode,
} from './resolver'
export {
  contrastRatio,
  isNonTextContrastPassing,
  isTextContrastPassing,
  parseHexColor,
  relativeLuminance,
} from './contrast'
export type {
  EffectiveColorMode,
  PcDensity,
  ResolvedUiAppearance,
  ThemeMode,
  ThemePalette,
  PcNavigationMode,
  UiPreferencesV1,
  UserUiScope,
} from './types'
