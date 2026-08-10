/**
 * PC 管理框架导航模型(§14.2)。第一批菜单仅「首页」,
 * 后续阶段在数组末尾追加,保持已发布菜单的语义与路由名稳定。
 */

import type { NavigationItem } from './types'

export const pcNavigationItems: NavigationItem[] = [
  {
    id: 'pc-home',
    label: '首页',
    routeName: 'pc-home',
    permission: 'platform.home.view',
  },
]
