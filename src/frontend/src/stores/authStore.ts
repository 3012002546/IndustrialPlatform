/**
 * 认证 Store(§10.2):登录/刷新/退出/恢复/权限判断。
 * - 并发 restore/refresh 单飞执行,避免重复请求与会话覆盖。
 * - password 不进入 Store/Storage。
 * - 退出时即使 Gateway 调用失败也清理本地会话。
 */

import { defineStore } from 'pinia'
import { computed, ref } from 'vue'

import { createApiError, DEFAULT_ERROR_MESSAGES } from '@/api/errors'
import { createCorrelationId } from '@/api/correlation'
import { getAuthGateway, setCurrentSession } from '@/auth/gateway'
import { clearAuthSession, readAuthSession, writeAuthSession } from '@/auth/sessionStore'
import type { AuthSession, AuthUser, LoginCommand } from '@/auth/types'

/** 会话存储(Phase 2 限定 sessionStorage)。 */
function defaultStorage(): Storage {
  return globalThis.sessionStorage
}

export const useAuthStore = defineStore('auth', () => {
  const session = ref<AuthSession | null>(null)
  const user = computed<AuthUser | null>(() => session.value?.user ?? null)
  const isAuthenticated = computed<boolean>(() => session.value !== null)

  let restorePromise: Promise<void> | null = null
  let refreshPromise: Promise<void> | null = null

  /** 提交会话并同步令牌镜像(HTTP 层 getToken 读取点)。 */
  function commitSession(value: AuthSession | null): void {
    session.value = value
    setCurrentSession(value)
  }

  function hasPermission(permission: string): boolean {
    return session.value?.user.permissions.includes(permission) ?? false
  }

  /** 从 sessionStorage 恢复会话;损坏/过期/未知版本视为无效并清理,单飞执行。 */
  async function restore(): Promise<void> {
    if (restorePromise !== null) return restorePromise
    restorePromise = (async () => {
      const storage = defaultStorage()
      const stored = readAuthSession(storage, Date.now())
      if (stored === null) {
        clearAuthSession(storage)
      }
      commitSession(stored)
    })()
    try {
      await restorePromise
    } finally {
      restorePromise = null
    }
  }

  /** 登录:成功才提交会话并持久化;失败不改变状态。 */
  async function login(command: LoginCommand): Promise<void> {
    const authenticated = await getAuthGateway().login(command)
    commitSession(authenticated)
    writeAuthSession(defaultStorage(), authenticated)
  }

  /** 刷新:单飞;无会话视为未登录;失败视为会话不可续,清理本地会话后抛出。 */
  async function refresh(): Promise<void> {
    if (refreshPromise !== null) return refreshPromise
    const current = session.value
    if (current === null) {
      throw createApiError(
        'unauthorized',
        DEFAULT_ERROR_MESSAGES.unauthorized,
        createCorrelationId(),
      )
    }
    refreshPromise = (async () => {
      const refreshed = await getAuthGateway().refresh(current.refreshToken)
      commitSession(refreshed)
      writeAuthSession(defaultStorage(), refreshed)
    })()
    try {
      await refreshPromise
    } catch (error) {
      commitSession(null)
      clearAuthSession(defaultStorage())
      throw error
    } finally {
      refreshPromise = null
    }
  }

  /** 退出:即使 Gateway 调用失败也必须清理本地会话。 */
  async function logout(): Promise<void> {
    try {
      await getAuthGateway().logout()
    } catch {
      // 网关退出失败不阻断本地清理
    } finally {
      commitSession(null)
      clearAuthSession(defaultStorage())
    }
  }

  return { session, user, isAuthenticated, restore, login, refresh, logout, hasPermission }
})
