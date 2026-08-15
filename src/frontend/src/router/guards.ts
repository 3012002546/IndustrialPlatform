/**
 * 唯一全局守卫(§12.3),决策顺序:
 * 恢复会话 → 公共/受保护 → 权限 → 工作区标签治理 → 确认目标终端 → 设置页面标题。
 *
 * 终端规则(§11.2):根路由按生效终端分流;显式三端路由以路由声明的
 * terminal 为准,不得自动改写。重定向只使用站内路由名,禁止开放重定向。
 * 工作区规则(§7.9):PC 工作区路由在确认前登记/激活;已达上限阻断导航并保留 pending。
 */

import type { Router } from 'vue-router'

import { useAuthStore } from '@/stores/authStore'
import { useDeviceStore } from '@/stores/deviceStore'
import { useThemeStore } from '@/stores/themeStore'
import { useWorkspaceTabsStore } from '@/stores/workspaceTabsStore'
import { buildTabId, toPersistedRoute } from '@/workspace'
import type { WorkspaceRouteCandidate } from '@/workspace'

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

    // 1.5 主题(PF-01 §7.4):恢复后初始化设备级外观;有用户则绑定用户偏好。
    // 进入受保护布局前完成绑定,避免切换用户后主题串用或受保护壳闪烁。
    const themeStore = useThemeStore()
    await themeStore.initialize()
    const user = authStore.user
    if (authStore.isAuthenticated && user !== null) {
      await themeStore.bindUser({ tenantId: user.tenantId, userId: user.userId })
    }

    // 2. 公共或受保护路由;无会话访问受保护路由 → 登录,携带站内相对 redirect
    if (to.meta.requiresAuth && !authStore.isAuthenticated) {
      return { name: ROUTE_NAMES.login, query: { redirect: to.fullPath } }
    }

    // 2.5 §29A.4 首次登录改密门禁:用户必须改密时,除改密页外一律跳转改密页
    // (普通新用户首次登录只允许改密与注销;内置 admin MustChangePassword=false 不受影响)。
    if (
      to.meta.requiresAuth &&
      authStore.isAuthenticated &&
      authStore.user?.mustChangePassword === true &&
      to.name !== ROUTE_NAMES.changePassword
    ) {
      return { name: ROUTE_NAMES.changePassword }
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

    // 3.5 工作区标签治理(§7.9):PC 工作区路由在确认前登记/激活;已达上限阻断导航。
    // 恢复时只保留 Router 存在、workspace business 且用户仍有权限的路由;非法/未授权项丢弃。
    const workspace = to.meta.workspace
    if (workspace !== undefined && workspace !== 'none') {
      const tabsStore = useWorkspaceTabsStore()
      const user = authStore.user
      if (authStore.isAuthenticated && user !== null) {
        tabsStore.bindUser({ tenantId: user.tenantId, userId: user.userId })
        tabsStore.prune((tab) => {
          const record = router.getRoutes().find((r) => r.name === tab.route.name)
          if (record === undefined) return false
          const meta = record.meta
          if (meta.workspace !== 'business') return false
          return meta.permission === undefined || authStore.hasPermission(meta.permission)
        })
      }
      const candidate: WorkspaceRouteCandidate = {
        id: buildTabId(String(to.name ?? ''), to.params, to.query),
        title: to.meta.title,
        kind: workspace,
        route: toPersistedRoute({
          name: String(to.name ?? ''),
          params: to.params,
          query: to.query,
        }),
        // exactOptionalPropertyTypes:仅当有权限门槛时才携带 permission 字段
        ...(to.meta.permission === undefined ? {} : { permission: to.meta.permission }),
      }
      const result = tabsStore.requestOpen(candidate)
      if (result.kind === 'limit-reached') {
        // 导航前阻断:不渲染第 13 个页面;PcLayout 依据 pending 展示上限对话框。
        // 已确立当前路由时 return false(停留当前页);整页直达被阻断路由时
        // currentRoute 尚无匹配(matched 空),return false 会落成空白壳,故兜底到固定工作台。
        if (router.currentRoute.value.matched.length > 0) return false
        return { name: ROUTE_NAMES.pcHome }
      }
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
