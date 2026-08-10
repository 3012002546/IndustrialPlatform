/**
 * 唯一全局守卫(§12.3),决策顺序:
 * 恢复会话 → 公共/受保护 → 权限 → 确认目标终端 → 设置页面标题。
 *
 * 终端规则(§11.2):根路由按生效终端分流;显式三端路由以路由声明的
 * terminal 为准,不得自动改写。重定向只使用站内路由名,禁止开放重定向。
 */

import type { Router } from 'vue-router'

import { useAuthStore } from '@/stores/authStore'
import { useDeviceStore } from '@/stores/deviceStore'

import { ROUTE_NAMES } from './routes'

export const TITLE_SUFFIX = 'Industrial Platform'

/** 设置页面标题;无标题时仅保留平台名。 */
export function setDocumentTitle(title: string | undefined): void {
  if (typeof document === 'undefined') return
  document.title = title === undefined ? TITLE_SUFFIX : `${title} · ${TITLE_SUFFIX}`
}

export function installRouterGuards(router: Router): void {
  router.beforeEach(async (to) => {
    // 1. 恢复会话(幂等:Store 内单飞,重复导航不重复读存储)
    const authStore = useAuthStore()
    await authStore.restore()

    // 2. 公共或受保护路由;无会话访问受保护路由 → 登录,携带站内相对 redirect
    if (to.meta.requiresAuth && !authStore.isAuthenticated) {
      return { name: ROUTE_NAMES.login, query: { redirect: to.fullPath } }
    }

    // 设备终端(惰性初始化一次)
    const deviceStore = useDeviceStore()
    if (!deviceStore.ready) {
      deviceStore.init()
    }

    // 已登录访问登录页 → 回到生效终端首页
    if (to.name === ROUTE_NAMES.login && authStore.isAuthenticated) {
      return { name: `${deviceStore.terminal}-home` }
    }

    // 3. 权限:无权限跳转 403
    const permission = to.meta.permission
    if (permission !== undefined && !authStore.hasPermission(permission)) {
      return { name: ROUTE_NAMES.forbidden }
    }

    // 4. 确认目标终端:根路由按生效终端分流;显式终端路由不改写
    if (to.name === ROUTE_NAMES.root) {
      return { name: `${deviceStore.terminal}-home` }
    }

    // 5. 设置页面标题
    setDocumentTitle(to.meta.title)
    return true
  })
}
