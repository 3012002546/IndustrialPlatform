/**
 * Route Meta 类型(§12.2):通过模块增强扩展 vue-router 的 RouteMeta。
 * 所有路由都必须提供 title;requiresAuth / permission / terminal 可选。
 */

import 'vue-router'

import type { TerminalType } from '@/device/types'

export interface AppRouteMeta {
  /** 页面标题(守卫写入 document.title,格式 `${title} · Industrial Platform`)。 */
  title: string
  /** 受保护路由:无会话跳转登录。 */
  requiresAuth?: boolean
  /** 权限点:无权限跳转 /403。 */
  permission?: string
  /** 显式终端路由(§11.2:显式访问不得自动改写)。 */
  terminal?: TerminalType
}

declare module 'vue-router' {
  // 空接口是 vue-router meta 模块增强的标准形式,成员全部来自 AppRouteMeta。
  // eslint-disable-next-line @typescript-eslint/no-empty-object-type
  interface RouteMeta extends AppRouteMeta {}
}
