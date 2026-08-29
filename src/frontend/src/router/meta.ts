/**
 * Route Meta 类型(§12.2):通过模块增强扩展 vue-router 的 RouteMeta。
 * 所有路由都必须提供 title;requiresAuth / permission / anyPermissions / terminal / workspace 可选。
 */

import 'vue-router'

import type { TerminalType } from '@/device/types'
import type { PcExperienceMode } from '@/operation/types'

export interface AppRouteMeta {
  /** 页面标题(守卫写入 document.title,格式 `${title} · Industrial Platform`)。 */
  title: string
  /** 受保护路由:无会话跳转登录。 */
  requiresAuth?: boolean
  /** 权限点:无权限跳转 /403。 */
  permission?: string
  /** 任一权限满足即可进入，用于跨终端能力入口。 */
  anyPermissions?: readonly string[]
  /** 显式终端路由(§11.2:显式访问不得自动改写)。 */
  terminal?: TerminalType
  /** PC 体验模式约束;模式入口由权限守卫与此元数据共同声明。 */
  experience?: PcExperienceMode
  /** 工作区语义:fixed 固定工作台、business 受控业务标签、none 非工作区(PF-01 §7.9)。 */
  workspace?: 'fixed' | 'business' | 'none'
}

declare module 'vue-router' {
  // 空接口是 vue-router meta 模块增强的标准形式,成员全部来自 AppRouteMeta。
  // eslint-disable-next-line @typescript-eslint/no-empty-object-type
  interface RouteMeta extends AppRouteMeta {}
}
