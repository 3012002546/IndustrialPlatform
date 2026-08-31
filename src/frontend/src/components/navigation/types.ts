/**
 * 导航模型(§7.7 授权导航视图)。NavigationItem 描述一个菜单项;permission 可选,
 * 未持有所声明权限的项在渲染时被过滤(§13.2 权限过滤)。
 * NavigationGroup 描述工具轨上的一个一级平台分组。
 */

import type { Component } from 'vue'

export interface NavigationText {
  labelKey: string
  fallbackLabel: string
}

export type PcNavigationMode = 'expanded' | 'secondary-collapsed' | 'compact'

export interface NavigationItem {
  /** 菜单项唯一标识。 */
  id: string
  /** 菜单显示名。 */
  label: string
  /** Stable resource key and safe fallback for dynamic/localized labels. */
  labelKey?: string
  fallbackLabel?: string
  /** 目标路由名(全局唯一,见 router/routes.ts 的 ROUTE_NAMES)。 */
  routeName: string
  /** 图标组件(正式 Element Plus 图标,§7.7;禁止 Emoji/文本占位)。 */
  icon?: Component
  /** 权限点:仅当当前会话持有该权限时渲染;未声明视为公开。 */
  permission?: string
  /** SystemData 功能开关标识;由运行适配器在进入 PF-01 公开端口前求值。 */
  featureNId?: string
  /** 任一权限满足即可渲染,用于跨 PDA/Mobile 能力入口。 */
  anyPermissions?: readonly string[]
  /** 子菜单(可选;第一批无子菜单)。 */
  children?: NavigationItem[]
  /** 二级导航中的真实业务分组,仅由导航数据声明,不创建额外入口。 */
  sectionId?: string
  /** 菜单需要携带的显式查询参数,用于正式终端预览等场景。 */
  routeQuery?: Readonly<Record<string, string>>
  displayOrder?: number
}

/** 一级平台分组:工具轨渲染分组,功能树渲染当前组的授权 items。 */
export interface NavigationGroup {
  id: string
  label: string
  labelKey?: string
  fallbackLabel?: string
  icon: Component
  items: readonly NavigationItem[]
  /** 当前分组内的二级业务分组定义,缺省时保持原有平面菜单。 */
  sections?: readonly NavigationSection[] | undefined
  displayOrder?: number
}

export interface NavigationSection {
  id: string
  label: string
  labelKey?: string
  fallbackLabel?: string
}
