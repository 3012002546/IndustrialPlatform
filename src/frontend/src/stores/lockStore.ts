import { defineStore } from 'pinia'
import { computed, ref } from 'vue'

import { useAuthStore } from './authStore'

interface LockedUser {
  username: string
  displayName: string
}

/** 浏览器内锁屏状态；只保留解锁所需的非敏感用户标识，不保存密码。 */
export const useLockStore = defineStore('lock', () => {
  const authStore = useAuthStore()
  const lockedUser = ref<LockedUser | null>(null)
  const isLocked = computed(() => lockedUser.value !== null)
  let unlockPromise: Promise<void> | null = null

  function lock(): void {
    const user = authStore.user
    if (user === null || isLocked.value) return
    lockedUser.value = { username: user.username, displayName: user.displayName }
    authStore.clearLocalSession()
  }

  async function unlock(password: string): Promise<void> {
    const user = lockedUser.value
    if (user === null || unlockPromise !== null) return unlockPromise ?? Promise.resolve()
    unlockPromise = (async () => {
      try {
        await authStore.login({ username: user.username, password })
        lockedUser.value = null
      } finally {
        unlockPromise = null
      }
    })()
    return unlockPromise
  }

  function clear(): void {
    lockedUser.value = null
    unlockPromise = null
  }

  return { isLocked, lockedUser, lock, unlock, clear }
})
