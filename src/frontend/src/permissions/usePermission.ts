/**
 * 权限判断组合式函数(§13.2):页面与组件经它查询当前会话权限。
 * 基于 AuthStore 的响应式 permissions,权限刷新/失权时自动更新。
 */

import { useAuthStore } from '@/stores/authStore'

export interface PermissionChecks {
  /** 是否持有指定权限。 */
  has(permission: string): boolean
  /** 是否持有任一权限(多选入口常用)。 */
  hasAny(permissions: readonly string[]): boolean
  /** 是否持有全部权限。 */
  hasAll(permissions: readonly string[]): boolean
}

export function usePermission(): PermissionChecks {
  const authStore = useAuthStore()

  function has(permission: string): boolean {
    return authStore.hasPermission(permission)
  }

  function hasAny(permissions: readonly string[]): boolean {
    return permissions.some(has)
  }

  function hasAll(permissions: readonly string[]): boolean {
    return permissions.every(has)
  }

  return { has, hasAny, hasAll }
}
