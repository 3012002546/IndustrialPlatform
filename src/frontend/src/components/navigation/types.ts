/**
 * 导航模型(§14.2)。NavigationItem 描述一个菜单项;permission 可选,
 * 未持有所声明权限的项在渲染时被过滤(§13.2 权限过滤)。
 */

export interface NavigationItem {
  /** 菜单项唯一标识。 */
  id: string
  /** 菜单显示名。 */
  label: string
  /** 目标路由名(全局唯一,见 router/routes.ts 的 ROUTE_NAMES)。 */
  routeName: string
  /** 图标标识(可选;第一批不启用图标,后续阶段接入)。 */
  icon?: string
  /** 权限点:仅当当前会话持有该权限时渲染;未声明视为公开。 */
  permission?: string
  /** 子菜单(可选;第一批无子菜单)。 */
  children?: NavigationItem[]
}
