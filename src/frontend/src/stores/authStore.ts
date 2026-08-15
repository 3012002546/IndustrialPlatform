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
import {
  AUTH_SESSION_HTTP_STORAGE_KEY,
  AUTH_SESSION_STORAGE_KEY,
  clearAuthSession,
  readAuthSession,
  writeAuthSession,
} from '@/auth/sessionStore'
import type { AuthSession, AuthUser, LoginCommand } from '@/auth/types'
import { loadRuntimeConfig } from '@/config/runtimeConfig'

/** 会话存储(sessionStorage);按运行模式选键:http 用真实会话键,其余用 Mock 键。 */
function defaultStorage(): Storage {
  return globalThis.sessionStorage
}

/** 会话键随认证模式:真实 Identity 与 Mock 会话互不串用(README「真实令牌策略」)。 */
function sessionStorageKey(): string {
  return loadRuntimeConfig().authMode === 'http'
    ? AUTH_SESSION_HTTP_STORAGE_KEY
    : AUTH_SESSION_STORAGE_KEY
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
      const key = sessionStorageKey()
      const stored = readAuthSession(storage, Date.now(), key)
      if (stored === null) {
        clearAuthSession(storage, key)
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
    writeAuthSession(defaultStorage(), authenticated, sessionStorageKey())
  }

  /** SSO 票据交换成功后采纳线上会话(§26.5):提交并持久化,不经过 Gateway。 */
  function adoptSession(value: AuthSession): void {
    commitSession(value)
    writeAuthSession(defaultStorage(), value, sessionStorageKey())
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
      writeAuthSession(defaultStorage(), refreshed, sessionStorageKey())
    })()
    try {
      await refreshPromise
    } catch (error) {
      commitSession(null)
      clearAuthSession(defaultStorage(), sessionStorageKey())
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
      clearAuthSession(defaultStorage(), sessionStorageKey())
    }
  }

  /** 修改当前用户密码(§29A.4):成功后服务端撤销全部会话,前端清理本地会话并回登录页。 */
  async function changePassword(currentPassword: string, newPassword: string): Promise<void> {
    await getAuthGateway().changePassword(currentPassword, newPassword)
    commitSession(null)
    clearAuthSession(defaultStorage(), sessionStorageKey())
  }

  return {
    session,
    user,
    isAuthenticated,
    restore,
    login,
    refresh,
    logout,
    changePassword,
    adoptSession,
    hasPermission,
  }
})
